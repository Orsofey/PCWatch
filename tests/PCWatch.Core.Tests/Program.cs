using System;
using System.Collections.Generic;
using System.Diagnostics;
using PCWatch.Core;

namespace PCWatch.Core.Tests
{
    public static class Program
    {
        private static int _failures;

        public static int Main()
        {
            TestRingBuffer();
            TestSustainedDiagnostics();
            TestSingleSpikeIgnored();
            TestHealthyHistoryIsGood();
            TestHealthScore();
            TestAlertDurationAndCooldown();
            TestAlertCooldownExpiry();
            TestAlertNullTemperature();
            TestRussianFormat();
            TestRecommendations();
            TestProcessSafety();
            _failures += CleanupTests.Run();

            Console.WriteLine();
            Console.WriteLine(_failures == 0 ? "Все тесты пройдены." : "Провалено тестов: " + _failures);
            return _failures == 0 ? 0 : 1;
        }

        private static void Check(string name, bool condition)
        {
            if (condition)
            {
                Console.WriteLine("[OK]   " + name);
            }
            else
            {
                _failures++;
                Console.WriteLine("[FAIL] " + name);
            }
        }

        private static void TestRingBuffer()
        {
            RingBuffer<int> buffer = new RingBuffer<int>(3);
            Check("RingBuffer: пустой буфер", buffer.Count == 0);
            buffer.Add(1);
            buffer.Add(2);
            buffer.Add(3);
            buffer.Add(4);
            buffer.Add(5);
            Check("RingBuffer: счётчик после переполнения", buffer.Count == 3);
            int[] items = buffer.Snapshot();
            Check("RingBuffer: порядок после переполнения", items.Length == 3 && items[0] == 3 && items[1] == 4 && items[2] == 5);
            buffer.Clear();
            Check("RingBuffer: очистка", buffer.Count == 0 && buffer.Snapshot().Length == 0);
        }

        private static SystemSnapshot MakeSnapshot(DateTime timestamp, double cpu, double memory, double diskActive)
        {
            SystemSnapshot snapshot = new SystemSnapshot();
            snapshot.Timestamp = timestamp;
            snapshot.CpuUsage = cpu;
            snapshot.MemoryUsage = memory;
            snapshot.DiskActive = diskActive;
            snapshot.SystemDriveFreePercent = 100.0;
            snapshot.SystemDriveFreeGB = 100.0;
            snapshot.PageFileUsage = 10.0;
            snapshot.CommitUsage = 10.0;
            return snapshot;
        }

        private static bool HasIssue(IList<DiagnosticIssue> issues, string code)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Code == code)
                {
                    return true;
                }
            }
            return false;
        }

        private static void TestSustainedDiagnostics()
        {
            List<SystemSnapshot> history = new List<SystemSnapshot>();
            DateTime time = new DateTime(2026, 1, 1, 12, 0, 0);
            for (int i = 0; i < 12; i++)
            {
                SystemSnapshot snapshot = MakeSnapshot(time.AddSeconds(i), 96.0, 55.0, 5.0);
                ProcessSample process = new ProcessSample();
                process.Id = 1000 + i;
                process.Name = "chrome.exe";
                process.CpuUsage = 90.0;
                snapshot.Processes.Add(process);
                history.Add(snapshot);
            }

            IList<DiagnosticIssue> issues = DiagnosticsEngine.Analyze(history);
            Check("Диагностика: устойчиво высокий ЦП фиксируется", HasIssue(issues, "CPU_HIGH"));
            Check("Диагностика: устойчиво высокий ЦП не Critical (96%)", !HasCritical(issues, "CPU_HIGH"));

            bool attributed = false;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Code == "CPU_HIGH" && issues[i].ProcessName == "chrome.exe")
                {
                    attributed = true;
                }
            }
            Check("Диагностика: атрибуция процесса-потребителя", attributed);
        }

        private static bool HasCritical(IList<DiagnosticIssue> issues, string code)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Code == code && issues[i].Severity == IssueSeverity.Critical)
                {
                    return true;
                }
            }
            return false;
        }

        private static void TestSingleSpikeIgnored()
        {
            List<SystemSnapshot> history = new List<SystemSnapshot>();
            DateTime time = new DateTime(2026, 1, 1, 12, 0, 0);
            for (int i = 0; i < 12; i++)
            {
                double cpu = i == 5 ? 99.0 : 5.0;
                history.Add(MakeSnapshot(time.AddSeconds(i), cpu, 40.0, 2.0));
            }

            IList<DiagnosticIssue> issues = DiagnosticsEngine.Analyze(history);
            Check("Диагностика: одиночный всплеск ЦП не фиксируется", !HasIssue(issues, "CPU_HIGH"));
            Check("Диагностика: одиночный всплеск даёт только Good", issues.Count == 1 && issues[0].Severity == IssueSeverity.Good);
        }

        private static void TestHealthyHistoryIsGood()
        {
            List<SystemSnapshot> history = new List<SystemSnapshot>();
            DateTime time = new DateTime(2026, 1, 1, 12, 0, 0);
            for (int i = 0; i < 6; i++)
            {
                history.Add(MakeSnapshot(time.AddSeconds(i), 10.0, 30.0, 3.0));
            }

            IList<DiagnosticIssue> issues = DiagnosticsEngine.Analyze(history);
            Check("Диагностика: здоровая история даёт один Good", issues.Count == 1 && issues[0].Code == "OK" && issues[0].Severity == IssueSeverity.Good);
        }

        private static void TestHealthScore()
        {
            DiagnosticIssue warning = new DiagnosticIssue();
            warning.Code = "CPU_HIGH";
            warning.Severity = IssueSeverity.Warning;

            DiagnosticIssue critical = new DiagnosticIssue();
            critical.Code = "RAM_PRESSURE";
            critical.Severity = IssueSeverity.Critical;

            List<DiagnosticIssue> empty = new List<DiagnosticIssue>();
            Check("HealthScore: пусто = 100", DiagnosticsEngine.HealthScore(empty) == 100);

            List<DiagnosticIssue> oneWarning = new List<DiagnosticIssue>();
            oneWarning.Add(warning);
            Check("HealthScore: одно Warning = 88", DiagnosticsEngine.HealthScore(oneWarning) == 88);

            List<DiagnosticIssue> both = new List<DiagnosticIssue>();
            both.Add(warning);
            both.Add(critical);
            Check("HealthScore: Warning+Critical = 58", DiagnosticsEngine.HealthScore(both) == 58);

            List<DiagnosticIssue> onlyGood = new List<DiagnosticIssue>();
            DiagnosticIssue good = new DiagnosticIssue();
            good.Severity = IssueSeverity.Good;
            onlyGood.Add(good);
            Check("WorstStatus: только Good = Excellent", DiagnosticsEngine.WorstStatus(onlyGood) == HealthStatus.Excellent);
            Check("WorstStatus: Warning = Warning", DiagnosticsEngine.WorstStatus(oneWarning) == HealthStatus.Warning);
            Check("WorstStatus: Critical = Critical", DiagnosticsEngine.WorstStatus(both) == HealthStatus.Critical);
        }

        private static AppSettings MakeAlertSettings(int duration, int cooldown)
        {
            AppSettings settings = new AppSettings();
            settings.AlertsEnabled = true;
            settings.Alerts = new List<AlertRule>();
            AlertRule rule = new AlertRule();
            rule.Enabled = true;
            rule.Metric = AlertMetric.Cpu;
            rule.Threshold = 90.0;
            rule.TriggerAbove = true;
            rule.DurationSeconds = duration;
            rule.CooldownSeconds = cooldown;
            settings.Alerts.Add(rule);
            return settings;
        }

        private static void TestAlertDurationAndCooldown()
        {
            AlertEngine engine = new AlertEngine(MakeAlertSettings(3, 60));
            DateTime time = new DateTime(2026, 1, 1, 12, 0, 0);

            Check("Алерт: ниже порога — нет события", engine.Evaluate(MakeSnapshot(time, 50.0, 30.0, 2.0)).Count == 0);
            Check("Алерт: 1 с выше порога — нет события", engine.Evaluate(MakeSnapshot(time.AddSeconds(1), 95.0, 30.0, 2.0)).Count == 0);
            Check("Алерт: 2 с выше порога — нет события", engine.Evaluate(MakeSnapshot(time.AddSeconds(2), 95.0, 30.0, 2.0)).Count == 0);
            Check("Алерт: удержание 3 с не достигнуто (t=3)", engine.Evaluate(MakeSnapshot(time.AddSeconds(3), 95.0, 30.0, 2.0)).Count == 0);

            IList<AlertEvent> fired = engine.Evaluate(MakeSnapshot(time.AddSeconds(4), 95.0, 30.0, 2.0));
            Check("Алерт: удержание 3 с достигнуто — событие", fired.Count == 1 && fired[0].Metric == AlertMetric.Cpu && fired[0].Value == 95.0);
            Check("Алерт: сообщение на русском", fired.Count == 1 && fired[0].Message.Contains("Процессор") && fired[0].Message.Contains("95,0"));

            Check("Алерт: сразу после события — тишина (cooldown)", engine.Evaluate(MakeSnapshot(time.AddSeconds(5), 95.0, 30.0, 2.0)).Count == 0);

            engine.Reset();
            Check("Алерт: после Reset одиночная выборка — нет события", engine.Evaluate(MakeSnapshot(time.AddSeconds(6), 95.0, 30.0, 2.0)).Count == 0);
        }

        private static void TestAlertCooldownExpiry()
        {
            AlertEngine engine = new AlertEngine(MakeAlertSettings(2, 10));
            DateTime time = new DateTime(2026, 1, 1, 12, 0, 0);

            Check("Cooldown: 1 с — нет события", engine.Evaluate(MakeSnapshot(time, 95.0, 30.0, 2.0)).Count == 0);
            Check("Cooldown: 2 с — событие", engine.Evaluate(MakeSnapshot(time.AddSeconds(2), 95.0, 30.0, 2.0)).Count == 1);
            Check("Cooldown: +5 с — тишина", engine.Evaluate(MakeSnapshot(time.AddSeconds(7), 95.0, 30.0, 2.0)).Count == 0);
            Check("Cooldown: +11 с от события — снова событие", engine.Evaluate(MakeSnapshot(time.AddSeconds(13), 95.0, 30.0, 2.0)).Count == 1);
        }

        private static void TestAlertNullTemperature()
        {
            AppSettings settings = new AppSettings();
            settings.AlertsEnabled = true;
            settings.Alerts = new List<AlertRule>();
            AlertRule rule = new AlertRule();
            rule.Enabled = true;
            rule.Metric = AlertMetric.Temperature;
            rule.Threshold = 90.0;
            rule.TriggerAbove = true;
            rule.DurationSeconds = 1;
            rule.CooldownSeconds = 60;
            settings.Alerts.Add(rule);

            AlertEngine engine = new AlertEngine(settings);
            SystemSnapshot snapshot = MakeSnapshot(new DateTime(2026, 1, 1, 12, 0, 0), 20.0, 30.0, 2.0);
            snapshot.CpuTemperature = null;
            Check("Алерт: температура null безопасна", engine.Evaluate(snapshot).Count == 0);
        }

        private static void TestRussianFormat()
        {
            Check("Формат: Percent", RussianFormat.Percent(85.5) == "85,5 %");
            Check("Формат: GigaBytes", RussianFormat.GigaBytes(12.34) == "12,3 ГБ");
            Check("Формат: Bytes КБ", RussianFormat.Bytes(2048) == "2,0 КБ");
            Check("Формат: Bytes МБ", RussianFormat.Bytes(1048576) == "1,0 МБ");
            Check("Формат: BytesPerSec", RussianFormat.BytesPerSec(1048576) == "1,0 МБ/с");
            Check("Формат: MegaHertz", RussianFormat.MegaHertz(850) == "850 МГц");
            Check("Формат: Milliseconds", RussianFormat.Milliseconds(12.34) == "12,3 мс");
            Check("Формат: Rate", RussianFormat.Rate(120) == "120,0 /с");
        }

        private static List<SystemSnapshot> RecommendationHistory(string name, bool foreground)
        {
            List<SystemSnapshot> history = new List<SystemSnapshot>();
            for (int i = 0; i < 4; i++)
            {
                SystemSnapshot snapshot = MakeSnapshot(new DateTime(2026, 1, 1, 12, 0, 0).AddSeconds(i), 10, 40, 2);
                ProcessSample process = new ProcessSample { Id = 300, Name = name, MemoryMB = 250, CpuUsage = 1.0 };
                process.IsForeground = foreground && i == 3;
                snapshot.Processes.Add(process);
                history.Add(snapshot);
            }
            return history;
        }

        private static void TestRecommendations()
        {
            int currentPid = Process.GetCurrentProcess().Id;
            RecommendationResult updater = RecommendationEngine.Evaluate(RecommendationHistory("googleupdate", false), currentPid, null);
            Check("Рекомендации: фоновый updater без окон можно закрыть", updater.Items.Count == 1 && updater.Items[0].IsSafeCandidate && updater.PotentialMemoryMB == 250);

            RecommendationResult foreground = RecommendationEngine.Evaluate(RecommendationHistory("googleupdate", true), currentPid, null);
            Check("Рекомендации: активное приложение исключено", foreground.Items.Count == 0);

            RecommendationResult vm = RecommendationEngine.Evaluate(RecommendationHistory("qemu-system-x86_64", false), currentPid, null);
            Check("Рекомендации: виртуальная машина исключена", vm.Items.Count == 0);

            RecommendationResult service = RecommendationEngine.Evaluate(RecommendationHistory("svchost", false), currentPid, null);
            Check("Рекомендации: svchost исключён", service.Items.Count == 0);

            RecommendationResult adobe = RecommendationEngine.Evaluate(RecommendationHistory("adobeipcbroker", false), currentPid, null);
            Check("Рекомендации: Adobe helper распознан", adobe.Items.Count == 1 && adobe.Items[0].IsSafeCandidate);

            RecommendationResult hidden = RecommendationEngine.Evaluate(RecommendationHistory("googleupdate", false), currentPid, new[] { "googleupdate" });
            Check("Рекомендации: скрытый процесс исключён", hidden.Items.Count == 0);

            List<SystemSnapshot> pidReuse = RecommendationHistory("googleupdate", false);
            DateTime oldStart = new DateTime(2026, 1, 1, 11, 0, 0), newStart = oldStart.AddMinutes(1);
            for (int i = 0; i < pidReuse.Count - 1; i++)
            {
                pidReuse[i].Processes[0].StartTime = oldStart;
                pidReuse[i].Processes[0].MemoryMB = 1000;
            }
            pidReuse[pidReuse.Count - 1].Processes[0].StartTime = newStart;
            pidReuse[pidReuse.Count - 1].Processes[0].MemoryMB = 5;
            RecommendationResult reused = RecommendationEngine.Evaluate(pidReuse, currentPid, null);
            Check("Рекомендации: PID reuse не смешивает историю", reused.Items.Count == 0);

            List<SystemSnapshot> unknownHistory = RecommendationHistory("unknown-heavy", false);
            foreach (SystemSnapshot snapshot in unknownHistory) snapshot.Processes[0].MemoryMB = 1500;
            RecommendationResult unknown = RecommendationEngine.Evaluate(unknownHistory, currentPid, null);
            Check("Рекомендации: неизвестный процесс не считается безопасным", unknown.Items.Count == 1 && !unknown.Items[0].IsSafeCandidate && unknown.Items[0].Category == "Проверить вручную");

            List<SystemSnapshot> game = RecommendationHistory("steamwebhelper", false);
            ProcessSample activeGame = new ProcessSample { Id = 900, Name = "cs2", GpuUsage = 60 };
            activeGame.Windows.Add(new WindowDescriptor { Title = "Game" });
            game[3].Processes.Add(activeGame);
            RecommendationResult steam = RecommendationEngine.Evaluate(game, currentPid, null);
            Check("Рекомендации: игровой helper исключён при активной игре", steam.Items.Count == 0);
        }

        private static void TestProcessSafety()
        {
            int currentPid = Process.GetCurrentProcess().Id;

            string reason;
            bool ok;
            using (Process current = Process.GetCurrentProcess())
            {
                ProcessSample expected = new ProcessSample { Id = current.Id, Name = current.ProcessName, StartTime = current.StartTime };
                Check("Идентичность: текущий процесс подтверждён", ProcessSafety.IsCurrentIdentity(current, expected));
                expected.StartTime = expected.StartTime.AddMilliseconds(-1);
                Check("Идентичность: другой StartTime отклонён", !ProcessSafety.IsCurrentIdentity(current, expected));
                expected.StartTime = current.StartTime;
                expected.Name = "notepad.exe";
                Check("Идентичность: другое имя отклонено", !ProcessSafety.IsCurrentIdentity(current, expected));
            }

            ok = ProcessSafety.CanTerminate(new ProcessSample { Id = 0, Name = "System Idle Process" }, currentPid, out reason);
            Check("Безопасность: PID 0 запрещён", !ok && reason.Length > 0);

            ok = ProcessSafety.CanTerminate(new ProcessSample { Id = 4, Name = "System" }, currentPid, out reason);
            Check("Безопасность: PID 4 запрещён", !ok && reason.Length > 0);

            ok = ProcessSafety.CanTerminate(new ProcessSample { Id = currentPid, Name = "PCWatch.exe" }, currentPid, out reason);
            Check("Безопасность: собственный процесс запрещён", !ok && reason.Length > 0);

            ok = ProcessSafety.CanTerminate(new ProcessSample { Id = 100, Name = "csrss.exe" }, currentPid, out reason);
            Check("Безопасность: csrss запрещён", !ok && reason.Length > 0);

            ok = ProcessSafety.CanTerminate(new ProcessSample { Id = 101, Name = "lsass" }, currentPid, out reason);
            Check("Безопасность: lsass запрещён", !ok && reason.Length > 0);

            ok = ProcessSafety.CanTerminate(new ProcessSample { Id = 1011, Name = "LsaIso.exe" }, currentPid, out reason);
            Check("Безопасность: LsaIso запрещён", !ok && reason.Length > 0);

            ok = ProcessSafety.CanTerminate(new ProcessSample { Id = 1012, Name = "MsMpEng.exe" }, currentPid, out reason);
            Check("Безопасность: Defender запрещён", !ok && reason.Length > 0);

            ok = ProcessSafety.CanTerminate(new ProcessSample { Id = 102, Name = "svchost.exe" }, currentPid, out reason);
            Check("Безопасность: svchost запрещён", !ok && reason.Length > 0);

            ok = ProcessSafety.CanTerminate(null, currentPid, out reason);
            Check("Безопасность: null-процесс запрещён", !ok && reason.Length > 0);

            ProcessSample notepad = new ProcessSample { Id = 200, Name = "notepad.exe" };
            ok = ProcessSafety.CanTerminate(notepad, currentPid, out reason);
            Check("Безопасность: notepad разрешён", ok && reason.Length == 0);
        }
    }
}
