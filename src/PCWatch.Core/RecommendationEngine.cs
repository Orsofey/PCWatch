using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PCWatch.Core
{
    public static class RecommendationEngine
    {
        private sealed class Rule
        {
            public string Impact;
            public string Group;
            public Rule(string impact, string group) { Impact = impact; Group = group; }
        }

        private sealed class Metrics
        {
            public int Samples;
            public double Cpu;
            public double Memory;
            public double Disk;
        }

        private static readonly Dictionary<string, Rule> Rules = CreateRules();
        private static readonly string[] ExcludedPrefixes = {
            "vmmem", "qemu-system", "vmware", "virtualbox", "vmcompute", "vmms", "wsl",
            "devenv", "code", "cursor", "rider", "idea", "pycharm", "msbuild", "dotnet", "rustc", "cargo",
            "java", "javaw", "node", "python", "obs", "onedrive", "dropbox", "googledrive", "googledrivesync",
            "icue", "lghub", "razer", "armoury", "msiafterburner", "rtss", "powertoys", "everything",
            "searchindexer", "searchprotocolhost", "searchfilterhost", "audiodg", "nahimic", "nvcontainer", "nvidia",
            "amd", "radeon", "igfx", "sqlservr", "postgres", "mysqld", "mongod", "redis", "docker", "com.docker",
            "security", "windefend", "svchost", "systemsettings", "sihost", "runtimebroker"
        };

        public static RecommendationResult Evaluate(IReadOnlyList<SystemSnapshot> history, int currentProcessId, IEnumerable<string> hidden)
        {
            RecommendationResult result = new RecommendationResult();
            if (history == null || history.Count < 2) return result;
            SystemSnapshot latest = history[history.Count - 1];
            HashSet<string> hiddenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (hidden != null) foreach (string name in hidden) hiddenNames.Add(KnownApplications.Normalize(name));
            bool creativeActive = HasCreativeApplicationActive(latest);
            bool gameActive = HasGameActive(latest);
            List<RecommendationItem> items = new List<RecommendationItem>();
            foreach (ProcessSample process in latest.Processes)
            {
                string name = KnownApplications.Normalize(process.Name);
                string reasonIgnored;
                if (hiddenNames.Contains(name) || !ProcessSafety.CanTerminate(process, currentProcessId, out reasonIgnored) || IsExcluded(name)) continue;
                if (process.Responding.HasValue && !process.Responding.Value && process.Windows.Count > 0)
                {
                    items.Add(new RecommendationItem { Process = process, Category = "Требует внимания", Reason = "Окно приложения не отвечает. Можно попробовать завершить и открыть программу заново.", Impact = "Несохранённые данные в этом приложении могут быть потеряны.", Score = 70, IsSafeCandidate = false });
                    continue;
                }
                if (process.IsForeground || process.Windows.Count > 0) continue;
                Metrics metrics = Average(history, process);
                if (metrics.Samples < 2) continue;
                Rule rule;
                if (Rules.TryGetValue(name, out rule))
                {
                    if (rule.Group == "adobe" && creativeActive) continue;
                    if (rule.Group == "games" && gameActive) continue;
                    if (metrics.Memory < 80 && metrics.Cpu < 0.5 && metrics.Disk < 262144) continue;
                    int score = Score(metrics) + 25;
                    RecommendationItem item = new RecommendationItem { Process = process, Category = "Можно закрыть", Reason = Describe(metrics, "Фоновый помощник без открытых окон"), Impact = rule.Impact, Score = score, IsSafeCandidate = true };
                    items.Add(item);
                    result.PotentialMemoryMB += process.MemoryMB;
                }
                else if (metrics.Memory >= 1000 || metrics.Cpu >= 10 || metrics.Disk >= 5242880)
                {
                    items.Add(new RecommendationItem { Process = process, Category = "Проверить вручную", Reason = Describe(metrics, "Заметное потребление ресурсов в фоне"), Impact = "Назначение процесса не подтверждено. Сначала проверьте его свойства и документацию: закрытие может прервать полезную фоновую задачу.", Score = Score(metrics), IsSafeCandidate = false });
                }
            }
            items.Sort(delegate(RecommendationItem a, RecommendationItem b) { return b.Score.CompareTo(a.Score); });
            if (items.Count > 20) items.RemoveRange(20, items.Count - 20);
            result.Items = items;
            return result;
        }

        private static Metrics Average(IReadOnlyList<SystemSnapshot> history, ProcessSample current)
        {
            Metrics result = new Metrics();
            int start = Math.Max(0, history.Count - 30);
            for (int i = start; i < history.Count; i++)
                foreach (ProcessSample process in history[i].Processes)
                {
                    if (process.Id != current.Id || !SameIdentity(process, current)) continue;
                    result.Samples++;
                    result.Cpu += process.CpuUsage;
                    result.Memory += process.MemoryMB;
                    result.Disk += process.DiskBytesPerSec;
                    break;
                }
            if (result.Samples > 0)
            {
                result.Cpu /= result.Samples;
                result.Memory /= result.Samples;
                result.Disk /= result.Samples;
            }
            return result;
        }

        private static bool SameIdentity(ProcessSample candidate, ProcessSample current)
        {
            if (!String.Equals(KnownApplications.Normalize(candidate.Name), KnownApplications.Normalize(current.Name), StringComparison.OrdinalIgnoreCase)) return false;
            if (!String.IsNullOrWhiteSpace(candidate.Path) && !String.IsNullOrWhiteSpace(current.Path)
                && !String.Equals(candidate.Path, current.Path, StringComparison.OrdinalIgnoreCase)) return false;
            return candidate.StartTime == DateTime.MinValue || current.StartTime == DateTime.MinValue || candidate.StartTime == current.StartTime;
        }

        private static string Describe(Metrics metrics, string prefix)
        {
            return prefix + ": " + RussianFormat.Number(metrics.Memory) + " МБ, ЦП " + RussianFormat.Percent(metrics.Cpu)
                + ", диск " + RussianFormat.BytesPerSec(metrics.Disk) + ". Среднее по " + metrics.Samples + " замерам.";
        }

        private static int Score(Metrics metrics)
        {
            return Math.Max(1, Math.Min(99, (int)(metrics.Cpu * 5 + metrics.Memory / 45 + metrics.Disk / 1048576 * 4)));
        }

        private static bool IsExcluded(string name)
        {
            foreach (string prefix in ExcludedPrefixes) if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool HasCreativeApplicationActive(SystemSnapshot snapshot)
        {
            string[] names = { "photoshop", "premiere", "afterfx", "after effects", "illustrator", "indesign", "lightroom", "audition", "animate", "dreamweaver" };
            foreach (ProcessSample process in snapshot.Processes)
            {
                string name = KnownApplications.Normalize(process.Name);
                foreach (string candidate in names)
                    if (name.StartsWith(candidate, StringComparison.Ordinal) && (process.Windows.Count > 0 || process.CpuUsage > 2)) return true;
            }
            return false;
        }

        private static bool HasGameActive(SystemSnapshot snapshot)
        {
            string[] names = { "valorant", "league", "cs2", "dota2", "fortnite", "minecraft", "roblox", "genshin", "r5apex", "eldenring", "overwatch", "call of duty" };
            foreach (ProcessSample process in snapshot.Processes)
            {
                if (process.GpuUsage > 25 && process.Windows.Count > 0) return true;
                string name = KnownApplications.Normalize(process.Name);
                foreach (string candidate in names) if (name.StartsWith(candidate, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static Dictionary<string, Rule> CreateRules()
        {
            Dictionary<string, Rule> rules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
            string updates = "Проверка обновлений продолжится позже через планировщик или при следующем запуске приложения.";
            rules["googleupdate"] = new Rule(updates, "");
            rules["microsoftedgeupdate"] = new Rule(updates, "");
            rules["edgeupdate"] = new Rule(updates, "");
            rules["braveupdate"] = new Rule(updates, "");
            rules["operaautoupdate"] = new Rule(updates, "");
            rules["adobearm"] = new Rule(updates, "");
            rules["discordupdate"] = new Rule(updates, "");
            rules["ccxprocess"] = new Rule("Фоновые панели Adobe могут перезапуститься. Установленные программы и файлы проектов не затрагиваются.", "adobe");
            rules["adobeipcbroker"] = new Rule("Adobe перезапустит IPC-helper при следующем запуске своих приложений.", "adobe");
            rules["creative cloud"] = new Rule("Закроется фоновый клиент Creative Cloud; установленные приложения и проекты останутся.", "adobe");
            rules["steamwebhelper"] = new Rule("Steam перезапустит helper. Не закрывайте его при использовании оверлея или активной игры.", "games");
            rules["epicwebhelper"] = new Rule("Вспомогательный процесс Epic перезапустится вместе с лаунчером.", "games");
            rules["epicgameslauncher"] = new Rule("Лаунчер закроется, игры не удалятся. Текущие загрузки могут быть приостановлены.", "games");
            rules["riotclientux"] = new Rule("Клиент Riot перезапустится при следующем входе. Не закрывайте во время игры.", "games");
            rules["goggalaxy"] = new Rule("GOG Galaxy закроется; установленные игры останутся. Загрузки могут быть приостановлены.", "games");
            rules["galaxyclientservice"] = new Rule("Фоновый клиент GOG может перезапуститься при следующем старте.", "games");
            rules["eadesktop"] = new Rule("EA app закроется; игры останутся. Загрузки и обновления могут быть приостановлены.", "games");
            rules["eacore"] = new Rule("Фоновый компонент EA перезапустится при следующем запуске клиента.", "games");
            rules["originwebhelperservice"] = new Rule("Origin/EA перезапустит вспомогательный процесс при следующем запуске.", "games");
            rules["ubisoftconnect"] = new Rule("Ubisoft Connect закроется; игры останутся. Загрузки могут быть приостановлены.", "games");
            rules["upc"] = new Rule("Ubisoft Connect перезапустит фоновый компонент при следующем запуске.", "games");
            return rules;
        }
    }
}
