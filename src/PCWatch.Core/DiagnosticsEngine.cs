using System;
using System.Collections.Generic;

namespace PCWatch.Core
{
    /// <summary>
    /// Диагностика по устойчивой истории снимков: одиночные всплески не считаются проблемой.
    /// Проблема фиксируется, если условие выполнялось минимум в 60% выборок окна
    /// и не менее чем в 2 выборках.
    /// </summary>
    public static class DiagnosticsEngine
    {
        private const double SustainedFraction = 0.6;
        private const int MinSustainedSamples = 2;

        private const double CpuThreshold = 90.0;
        private const double MemoryThreshold = 92.0;
        private const double PageReadsThreshold = 400.0;
        private const double DiskActiveThreshold = 90.0;
        private const double DiskLatencyThresholdMs = 20.0;
        private const double SystemDriveFreeThreshold = 10.0;
        private const double TempWarningC = 85.0;
        private const double ThrottleLoadThreshold = 50.0;
        private const double PageFileThreshold = 80.0;
        private const double CommitThreshold = 90.0;
        private const double GpuThreshold = 95.0;

        public static IList<DiagnosticIssue> Analyze(IReadOnlyList<SystemSnapshot> history)
        {
            List<DiagnosticIssue> issues = new List<DiagnosticIssue>();
            if (history == null || history.Count == 0)
            {
                issues.Add(BuildGood());
                return issues;
            }

            int count = history.Count;
            int required = (int)Math.Ceiling(count * SustainedFraction);
            if (required < MinSustainedSamples)
            {
                required = MinSustainedSamples;
            }
            bool sustained = required <= count;
            if (!sustained)
            {
                issues.Add(BuildGood());
                return issues;
            }

            AnalyzeCpu(history, count, required, issues);
            AnalyzeMemory(history, count, required, issues);
            AnalyzeDisk(history, count, required, issues);
            AnalyzeSystemDrive(history, count, required, issues);
            AnalyzeTemperature(history, count, required, issues);
            AnalyzeThrottling(history, count, required, issues);
            AnalyzePageFile(history, count, required, issues);
            AnalyzeGpu(history, count, required, issues);

            if (issues.Count == 0)
            {
                issues.Add(BuildGood());
            }
            return issues;
        }

        public static int HealthScore(IList<DiagnosticIssue> issues)
        {
            int score = 100;
            if (issues == null)
            {
                return score;
            }
            for (int i = 0; i < issues.Count; i++)
            {
                IssueSeverity severity = issues[i].Severity;
                if (severity == IssueSeverity.Critical)
                {
                    score -= 30;
                }
                else if (severity == IssueSeverity.Warning)
                {
                    score -= 12;
                }
                else if (severity == IssueSeverity.Info)
                {
                    score -= 4;
                }
            }
            if (score < 0)
            {
                score = 0;
            }
            if (score > 100)
            {
                score = 100;
            }
            return score;
        }

        public static HealthStatus WorstStatus(IList<DiagnosticIssue> issues)
        {
            if (issues == null)
            {
                return HealthStatus.Unknown;
            }
            HealthStatus worst = HealthStatus.Excellent;
            for (int i = 0; i < issues.Count; i++)
            {
                IssueSeverity severity = issues[i].Severity;
                if (severity == IssueSeverity.Critical)
                {
                    return HealthStatus.Critical;
                }
                if (severity == IssueSeverity.Warning && worst < HealthStatus.Warning)
                {
                    worst = HealthStatus.Warning;
                }
                else if (severity == IssueSeverity.Info && worst == HealthStatus.Excellent)
                {
                    worst = HealthStatus.Good;
                }
            }
            return worst;
        }

        private static void AnalyzeCpu(IReadOnlyList<SystemSnapshot> history, int count, int required, List<DiagnosticIssue> issues)
        {
            int matches = CountTrue(history, delegate(SystemSnapshot s) { return s.CpuUsage >= CpuThreshold; });
            if (matches < required)
            {
                return;
            }
            double avg = Average(history, delegate(SystemSnapshot s) { return s.CpuUsage; });
            IssueSeverity severity = avg >= 97.0 ? IssueSeverity.Critical : IssueSeverity.Warning;

            double topValue;
            string topName = TopProcessName(history, delegate(ProcessSample p) { return p.CpuUsage; }, out topValue);

            string evidence = "Загрузка ЦП стабильно высокая: среднее " + RussianFormat.Percent(avg)
                + " (порог " + RussianFormat.Percent(CpuThreshold) + ", окно " + count.ToString("N0", RussianFormat.Culture) + " выборок).";
            if (topName.Length > 0 && topValue >= 25.0)
            {
                evidence += " Основной потребитель: " + topName + " (" + RussianFormat.Percent(topValue) + " ЦП в среднем).";
            }

            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "CPU_HIGH";
            issue.Title = "Высокая загрузка процессора";
            issue.Evidence = evidence;
            issue.Recommendation = "Закройте или перезапустите процесс-потребитель, проверьте систему на майнеры и вирусы, отключите лишние фоновые задачи.";
            issue.Severity = severity;
            issue.ProcessName = topName;
            issues.Add(issue);
        }

        private static void AnalyzeMemory(IReadOnlyList<SystemSnapshot> history, int count, int required, List<DiagnosticIssue> issues)
        {
            int matches = CountTrue(history, delegate(SystemSnapshot s)
            {
                return s.MemoryUsage >= MemoryThreshold || s.PageReadsPerSec >= PageReadsThreshold;
            });
            if (matches < required)
            {
                return;
            }
            double memAvg = Average(history, delegate(SystemSnapshot s) { return s.MemoryUsage; });
            double pageReadAvg = Average(history, delegate(SystemSnapshot s) { return s.PageReadsPerSec; });
            IssueSeverity severity = (memAvg >= 97.0 || pageReadAvg >= 1500.0) ? IssueSeverity.Critical : IssueSeverity.Warning;

            double topValue;
            string topName = TopProcessName(history, delegate(ProcessSample p) { return p.MemoryMB; }, out topValue);

            string evidence = "Давление на память: средняя загрузка ОЗУ " + RussianFormat.Percent(memAvg)
                + ", чтение страниц с диска " + RussianFormat.Rate(pageReadAvg)
                + " (окно " + count.ToString("N0", RussianFormat.Culture) + " выборок).";
            if (topName.Length > 0 && topValue >= 256.0)
            {
                evidence += " Основной потребитель: " + topName + " (" + RussianFormat.Number(topValue) + " МБ в среднем).";
            }

            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "RAM_PRESSURE";
            issue.Title = "Нехватка оперативной памяти";
            issue.Evidence = evidence;
            issue.Recommendation = "Закройте приложения с наибольшим потреблением памяти, проверьте их на утечки; при постоянной нехватке добавьте ОЗУ.";
            issue.Severity = severity;
            issue.ProcessName = topName;
            issues.Add(issue);
        }

        private static void AnalyzeDisk(IReadOnlyList<SystemSnapshot> history, int count, int required, List<DiagnosticIssue> issues)
        {
            int matches = CountTrue(history, delegate(SystemSnapshot s)
            {
                double latency = Math.Max(s.DiskReadLatencyMs, s.DiskWriteLatencyMs);
                return s.DiskActive >= DiskActiveThreshold && latency >= DiskLatencyThresholdMs;
            });
            if (matches < required)
            {
                return;
            }
            double activeAvg = Average(history, delegate(SystemSnapshot s) { return s.DiskActive; });
            double latencyAvg = Average(history, delegate(SystemSnapshot s)
            {
                return Math.Max(s.DiskReadLatencyMs, s.DiskWriteLatencyMs);
            });
            double rateAvg = Average(history, delegate(SystemSnapshot s)
            {
                return s.DiskReadBytesPerSec + s.DiskWriteBytesPerSec;
            });
            IssueSeverity severity = latencyAvg >= 50.0 ? IssueSeverity.Critical : IssueSeverity.Warning;

            double topValue;
            string topName = TopProcessName(history, delegate(ProcessSample p) { return p.DiskBytesPerSec; }, out topValue);

            string evidence = "Диск перегружен: средняя активность " + RussianFormat.Percent(activeAvg)
                + ", средняя задержка " + RussianFormat.Milliseconds(latencyAvg)
                + ", поток " + RussianFormat.BytesPerSec(rateAvg)
                + " (окно " + count.ToString("N0", RussianFormat.Culture) + " выборок).";
            if (topName.Length > 0 && topValue > 0.0)
            {
                evidence += " Основной потребитель: " + topName + " (" + RussianFormat.BytesPerSec(topValue) + ").";
            }

            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "DISK_SATURATED";
            issue.Title = "Диск перегружен: высокая активность и задержки";
            issue.Evidence = evidence;
            issue.Recommendation = "Определите процесс с максимальным дисковым вводом-выводом, отложите индексацию и обновления, проверьте здоровье диска (SMART).";
            issue.Severity = severity;
            issue.ProcessName = topName;
            issues.Add(issue);
        }

        private static void AnalyzeSystemDrive(IReadOnlyList<SystemSnapshot> history, int count, int required, List<DiagnosticIssue> issues)
        {
            int matches = CountTrue(history, delegate(SystemSnapshot s)
            {
                return s.SystemDriveFreePercent < SystemDriveFreeThreshold;
            });
            if (matches < required)
            {
                return;
            }
            double freeAvg = Average(history, delegate(SystemSnapshot s) { return s.SystemDriveFreePercent; });
            double gbAvg = Average(history, delegate(SystemSnapshot s) { return s.SystemDriveFreeGB; });
            IssueSeverity severity = freeAvg < 5.0 ? IssueSeverity.Critical : IssueSeverity.Warning;

            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "SYSTEM_DRIVE_LOW";
            issue.Title = "Мало свободного места на системном диске";
            issue.Evidence = "Свободно " + RussianFormat.Percent(freeAvg) + " (" + RussianFormat.GigaBytes(gbAvg)
                + ") в среднем за окно " + count.ToString("N0", RussianFormat.Culture) + " выборок (порог "
                + RussianFormat.Percent(SystemDriveFreeThreshold) + ").";
            issue.Recommendation = "Освободите место: «Очистка диска», удаление временных файлов и старых загрузок, перенос данных на другой том.";
            issue.Severity = severity;
            issues.Add(issue);
        }

        private static void AnalyzeTemperature(IReadOnlyList<SystemSnapshot> history, int count, int required, List<DiagnosticIssue> issues)
        {
            int matches = CountTrue(history, delegate(SystemSnapshot s)
            {
                return s.CpuTemperature.HasValue && s.CpuTemperature.Value >= TempWarningC;
            });
            if (matches < required)
            {
                return;
            }
            double tempAvg = Average(history, delegate(SystemSnapshot s)
            {
                return s.CpuTemperature.HasValue ? s.CpuTemperature.Value : 0.0;
            });
            IssueSeverity severity = tempAvg >= 92.0 ? IssueSeverity.Critical : IssueSeverity.Warning;

            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "CPU_OVERHEAT";
            issue.Title = "Перегрев процессора";
            issue.Evidence = "Температура ЦП держится на уровне " + RussianFormat.Number(tempAvg) + " °C"
                + " (порог " + RussianFormat.Number(TempWarningC) + " °C, окно "
                + count.ToString("N0", RussianFormat.Culture) + " выборок).";
            issue.Recommendation = "Проверьте охлаждение: очистите от пыли, замените термопасту, улучшите продув корпуса; снизьте нагрузку до устранения причины.";
            issue.Severity = severity;
            issues.Add(issue);
        }

        private static void AnalyzeThrottling(IReadOnlyList<SystemSnapshot> history, int count, int required, List<DiagnosticIssue> issues)
        {
            int matches = CountTrue(history, delegate(SystemSnapshot s)
            {
                return s.CpuUsage >= ThrottleLoadThreshold
                    && s.BaseClockMhz > 0.0
                    && s.CpuClockMhz > 0.0
                    && s.CpuClockMhz < s.BaseClockMhz * 0.7;
            });
            if (matches < required)
            {
                return;
            }
            double clockAvg = Average(history, delegate(SystemSnapshot s) { return s.CpuClockMhz; });
            double baseAvg = Average(history, delegate(SystemSnapshot s) { return s.BaseClockMhz; });
            double loadAvg = Average(history, delegate(SystemSnapshot s) { return s.CpuUsage; });

            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "CPU_THROTTLING";
            issue.Title = "Процессор сбрасывает частоту под нагрузкой";
            issue.Evidence = "Под нагрузкой " + RussianFormat.Percent(loadAvg) + " частота ЦП в среднем "
                + RussianFormat.MegaHertz(clockAvg) + " при базовой " + RussianFormat.MegaHertz(baseAvg) + ".";
            issue.Recommendation = "Выберите схему электропитания «Высокая производительность», проверьте температуры и питание, обновите BIOS и драйверы чипсета.";
            issue.Severity = IssueSeverity.Warning;
            issues.Add(issue);
        }

        private static void AnalyzePageFile(IReadOnlyList<SystemSnapshot> history, int count, int required, List<DiagnosticIssue> issues)
        {
            int matches = CountTrue(history, delegate(SystemSnapshot s)
            {
                return s.PageFileUsage >= PageFileThreshold || s.CommitUsage >= CommitThreshold;
            });
            if (matches < required)
            {
                return;
            }
            double pageAvg = Average(history, delegate(SystemSnapshot s) { return s.PageFileUsage; });
            double commitAvg = Average(history, delegate(SystemSnapshot s) { return s.CommitUsage; });
            IssueSeverity severity = (pageAvg >= 95.0 || commitAvg >= 95.0) ? IssueSeverity.Critical : IssueSeverity.Warning;

            double topValue;
            string topName = TopProcessName(history, delegate(ProcessSample p) { return p.MemoryMB; }, out topValue);

            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "PAGEFILE_PRESSURE";
            issue.Title = "Высокая нагрузка на файл подкачки";
            issue.Evidence = "Файл подкачки занят на " + RussianFormat.Percent(pageAvg)
                + ", commit-загрузка " + RussianFormat.Percent(commitAvg)
                + " в среднем за окно " + count.ToString("N0", RussianFormat.Culture) + " выборок.";
            issue.Recommendation = "Увеличьте файл подкачки или объём ОЗУ; закройте приложения с наибольшим потреблением памяти.";
            issue.Severity = severity;
            issue.ProcessName = topName;
            issues.Add(issue);
        }

        private static void AnalyzeGpu(IReadOnlyList<SystemSnapshot> history, int count, int required, List<DiagnosticIssue> issues)
        {
            int matches = CountTrue(history, delegate(SystemSnapshot s) { return s.GpuUsage >= GpuThreshold; });
            if (matches < required)
            {
                return;
            }
            double gpuAvg = Average(history, delegate(SystemSnapshot s) { return s.GpuUsage; });

            double topValue;
            string topName = TopProcessName(history, delegate(ProcessSample p) { return p.GpuUsage; }, out topValue);

            string evidence = "Загрузка GPU стабильно высокая: среднее " + RussianFormat.Percent(gpuAvg)
                + " (порог " + RussianFormat.Percent(GpuThreshold) + ", окно "
                + count.ToString("N0", RussianFormat.Culture) + " выборок).";
            if (topName.Length > 0 && topValue >= 25.0)
            {
                evidence += " Основной потребитель: " + topName + " (" + RussianFormat.Percent(topValue) + " GPU в среднем).";
            }

            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "GPU_HIGH";
            issue.Title = "Высокая загрузка видеокарты";
            issue.Evidence = evidence;
            issue.Recommendation = "Закройте игры и приложения рендеринга, проверьте процессы, использующие GPU, обновите драйвер видеокарты.";
            issue.Severity = IssueSeverity.Warning;
            issue.ProcessName = topName;
            issues.Add(issue);
        }

        private static DiagnosticIssue BuildGood()
        {
            DiagnosticIssue issue = new DiagnosticIssue();
            issue.Code = "OK";
            issue.Title = "Критичных проблем не обнаружено";
            issue.Evidence = "Показатели системы в норме за период наблюдения.";
            issue.Recommendation = "Продолжайте наблюдение.";
            issue.Severity = IssueSeverity.Good;
            return issue;
        }

        private static int CountTrue(IReadOnlyList<SystemSnapshot> history, Predicate<SystemSnapshot> condition)
        {
            int matches = 0;
            for (int i = 0; i < history.Count; i++)
            {
                if (condition(history[i]))
                {
                    matches++;
                }
            }
            return matches;
        }

        private static double Average(IReadOnlyList<SystemSnapshot> history, Func<SystemSnapshot, double> selector)
        {
            if (history.Count == 0)
            {
                return 0.0;
            }
            double sum = 0.0;
            for (int i = 0; i < history.Count; i++)
            {
                sum += selector(history[i]);
            }
            return sum / history.Count;
        }

        private static string TopProcessName(IReadOnlyList<SystemSnapshot> history, Func<ProcessSample, double> selector, out double avgValue)
        {
            Dictionary<string, double> sums = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < history.Count; i++)
            {
                IList<ProcessSample> processes = history[i].Processes;
                if (processes == null)
                {
                    continue;
                }
                for (int j = 0; j < processes.Count; j++)
                {
                    ProcessSample p = processes[j];
                    if (p == null || p.Name == null || p.Name.Length == 0)
                    {
                        continue;
                    }
                    if (sums.ContainsKey(p.Name))
                    {
                        sums[p.Name] += selector(p);
                        counts[p.Name] = counts[p.Name] + 1;
                    }
                    else
                    {
                        sums[p.Name] = selector(p);
                        counts[p.Name] = 1;
                    }
                }
            }

            string bestName = "";
            double bestAvg = 0.0;
            foreach (KeyValuePair<string, double> pair in sums)
            {
                double avg = pair.Value / counts[pair.Key];
                if (avg > bestAvg)
                {
                    bestAvg = avg;
                    bestName = pair.Key;
                }
            }
            avgValue = bestAvg;
            return bestName;
        }
    }
}
