using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PCWatch.Core
{
    // Read-only scan adapted from the category model of krezio/roomy (MIT).
    public static class CleanupScanner
    {
        private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

        public static IList<CleanupCategory> Scan(bool isAdmin, CancellationToken token, Action<string> status)
        {
            List<CleanupCategory> result = new List<CleanupCategory>();
            Report(status, "Корзина");
            Add(result, RecycleBin(token));
            Report(status, "Временные файлы");
            Add(result, Temp(token));
            Report(status, "Кэши браузеров");
            Add(result, Browsers(token));
            Report(status, "Кэши приложений");
            Add(result, Apps(token));
            Report(status, "Кэши GPU");
            Add(result, Shaders(token));
            Report(status, "Отчёты о сбоях");
            Add(result, Crashes(token));
            Report(status, "Остатки Windows Update");
            Add(result, WindowsLeftovers(isAdmin, token));
            Report(status, "Старые установщики");
            Add(result, Installers(token));
            Report(status, "Крупные загрузки");
            Add(result, BigDownloads(token));
            Report(status, "Windows.old");
            Add(result, WindowsOld(token));
            Report(status, "Файл гибернации");
            Add(result, Hibernation());
            return result;
        }

        private static void Report(Action<string> status, string text)
        {
            if (status != null) status("Проверка: " + text);
        }

        private static void Add(List<CleanupCategory> result, CleanupCategory category)
        {
            if (category != null) result.Add(category);
        }

        private static CleanupCategory Category(string id, string title, CleanupSafety safety, string note)
        {
            CleanupCategory category = new CleanupCategory();
            category.Id = id;
            category.Title = title;
            category.Safety = safety;
            category.Note = note ?? "";
            return category;
        }

        private static void AddItem(CleanupCategory category, string name, TimeSpan? minAge, IEnumerable<string> paths, CancellationToken token, bool selected, CleanupKind kind)
        {
            List<string> existing = new List<string>();
            foreach (string path in paths)
            {
                if (String.IsNullOrWhiteSpace(path) || !FileOps.Exists(path)) continue;
                bool duplicate = false;
                foreach (string existingPath in existing) if (String.Equals(existingPath, path, StringComparison.OrdinalIgnoreCase)) { duplicate = true; break; }
                if (!duplicate) existing.Add(path);
            }
            if (existing.Count == 0) return;
            long size = 0; int files = 0;
            foreach (string path in existing)
            {
                token.ThrowIfCancellationRequested();
                long bytes; int count;
                FileOps.Measure(path, minAge, out bytes, out count, token);
                size += bytes; files += count;
            }
            if (size <= 0 && kind == CleanupKind.Files) return;
            CleanupItem item = new CleanupItem();
            item.Name = name;
            item.Paths = existing;
            item.Size = size;
            item.FileCount = files;
            item.MinAge = minAge;
            item.SelectedByDefault = selected;
            item.Kind = kind;
            item.Detail = files > 0 ? files.ToString("N0", RussianFormat.Culture) + " файлов" : "";
            category.Items.Add(item);
        }

        private static void AddFileItem(CleanupCategory category, FileInfo file, string detail, bool selected, bool isDownload)
        {
            CleanupItem item = new CleanupItem();
            item.Name = file.Name;
            item.Paths = new List<string> { file.FullName };
            item.Size = file.Length;
            item.FileCount = 1;
            item.Detail = detail;
            item.IsDownload = isDownload;
            item.SelectedByDefault = selected;
            item.Kind = CleanupKind.Files;
            category.Items.Add(item);
        }

        private static CleanupCategory RecycleBin(CancellationToken token)
        {
            CleanupCategory category = Category("recycle", "Корзина", CleanupSafety.Review, "Содержимое корзины будет удалено безвозвратно. Отметьте вручную, если оно больше не нужно.");
            long bytes; long items;
            NativeCleanup.QueryRecycleBin(out bytes, out items);
            if (bytes <= 0) return null;
            CleanupItem item = new CleanupItem();
            item.Name = "Корзина";
            item.Paths = new List<string> { "Корзина Windows" };
            item.Size = bytes;
            item.FileCount = (int)Math.Min(Int32.MaxValue, items);
            item.Detail = items.ToString("N0", RussianFormat.Culture) + " объектов";
            item.Kind = CleanupKind.RecycleBin;
            item.SelectedByDefault = false;
            category.Items.Add(item);
            return category;
        }

        private static CleanupCategory Temp(CancellationToken token)
        {
            CleanupCategory category = Category("temp", "Временные файлы", CleanupSafety.Safe, "Удаляются файлы пользовательского Temp старше суток. Занятые файлы пропускаются.");
            string userTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
            AddItem(category, "Temp пользователя (старше 1 дня)", OneDay, new[] { userTemp }, token, true, CleanupKind.Files);
            return category.Items.Count == 0 ? null : category;
        }

        private static IEnumerable<string> ChromiumCaches(string userData)
        {
            if (!Directory.Exists(userData)) yield break;
            yield return Path.Combine(userData, "ShaderCache");
            yield return Path.Combine(userData, "GrShaderCache");
            yield return Path.Combine(userData, "GraphiteDawnCache");
            string[] profiles;
            try { profiles = Directory.GetDirectories(userData); }
            catch { profiles = new string[0]; }
            foreach (string profile in profiles)
            {
                string name = Path.GetFileName(profile);
                if (name != "Default" && name != "Guest Profile" && !name.StartsWith("Profile ", StringComparison.Ordinal)) continue;
                yield return Path.Combine(profile, "Cache");
                yield return Path.Combine(profile, "Code Cache");
                yield return Path.Combine(profile, "GPUCache");
            }
        }

        private static CleanupCategory Browsers(CancellationToken token)
        {
            CleanupCategory category = Category("browser", "Кэши браузеров", CleanupSafety.Safe, "Кэш страниц, кода и GPU. Пароли, история и вкладки не затрагиваются.");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            AddItem(category, "Google Chrome", null, ChromiumCaches(Path.Combine(local, @"Google\Chrome\User Data")), token, true, CleanupKind.Files);
            AddItem(category, "Microsoft Edge", null, ChromiumCaches(Path.Combine(local, @"Microsoft\Edge\User Data")), token, true, CleanupKind.Files);
            AddItem(category, "Brave", null, ChromiumCaches(Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data")), token, true, CleanupKind.Files);
            AddItem(category, "Vivaldi", null, ChromiumCaches(Path.Combine(local, @"Vivaldi\User Data")), token, true, CleanupKind.Files);
            AddItem(category, "Chromium", null, ChromiumCaches(Path.Combine(local, @"Chromium\User Data")), token, true, CleanupKind.Files);
            AddItem(category, "Opera", null, new[] { Path.Combine(local, @"Opera Software\Opera Stable\Cache"), Path.Combine(local, @"Opera Software\Opera GX Stable\Cache") }, token, true, CleanupKind.Files);
            List<string> firefox = new List<string>();
            string profiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox\Profiles");
            try
            {
                if (Directory.Exists(profiles))
                    foreach (string profile in Directory.GetDirectories(profiles)) firefox.Add(Path.Combine(profile, "cache2"));
            }
            catch { }
            AddItem(category, "Firefox", null, firefox, token, true, CleanupKind.Files);
            category.Note += RunningNote(new[] { "chrome", "msedge", "brave", "vivaldi", "opera", "firefox" });
            return category.Items.Count == 0 ? null : category;
        }

        private static IEnumerable<string> AppCacheBits(string root)
        {
            return new[] { Path.Combine(root, "Cache"), Path.Combine(root, "Code Cache"), Path.Combine(root, "GPUCache") };
        }

        private static IEnumerable<string> ChromiumCachesUnder(string root)
        {
            if (!Directory.Exists(root)) yield break;
            string[] directories;
            try { directories = FileOps.SafeDirectories(root).ToArray(); }
            catch { directories = new string[0]; }
            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory);
                if (name == "Cache" || name == "Code Cache" || name == "GPUCache" || name == "ShaderCache" || name == "GrShaderCache" || name == "GraphiteDawnCache")
                    yield return directory;
            }
        }

        private static CleanupCategory Apps(CancellationToken token)
        {
            CleanupCategory category = Category("apps", "Кэши приложений", CleanupSafety.Safe, "Только временные кэши. Профили входа, настройки, загрузки и игровые файлы не затрагиваются.");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AddItem(category, "Discord", null, CombineCaches(new[] { "discord", "discordptb", "discordcanary" }, roaming), token, true, CleanupKind.Files);
            AddItem(category, "Slack", null, AppCacheBits(Path.Combine(roaming, "Slack")), token, true, CleanupKind.Files);
            AddItem(category, "Microsoft Teams", null, AppCacheBits(Path.Combine(roaming, "Microsoft", "Teams")), token, true, CleanupKind.Files);
            AddItem(category, "Spotify", null, new[] { Path.Combine(local, @"Spotify\Browser"), Path.Combine(local, @"Spotify\Data") }, token, true, CleanupKind.Files);
            AddItem(category, "Visual Studio Code", null, new[] { Path.Combine(roaming, @"Code\Cache"), Path.Combine(roaming, @"Code\CachedData"), Path.Combine(roaming, @"Code\CachedExtensionVSIXs") }, token, true, CleanupKind.Files);
            AddItem(category, "Epic Games Launcher", null, EpicWebCaches(local), token, true, CleanupKind.Files);
            AddItem(category, "Steam browser cache", null, new[] { Path.Combine(local, @"Steam\htmlcache") }, token, true, CleanupKind.Files);
            AddItem(category, "Battle.net", null, ChromiumCachesUnder(Path.Combine(local, "Battle.net")), token, true, CleanupKind.Files);
            AddItem(category, "EA app", null, ChromiumCachesUnder(Path.Combine(local, "Electronic Arts", "EA Desktop")), token, true, CleanupKind.Files);
            AddItem(category, "Ubisoft Connect", null, ChromiumCachesUnder(Path.Combine(local, "Ubisoft Game Launcher")), token, true, CleanupKind.Files);
            AddItem(category, "Riot Client", null, ChromiumCachesUnder(Path.Combine(local, "Riot Games", "Riot Client")), token, true, CleanupKind.Files);
            AddItem(category, "GOG Galaxy", null, ChromiumCachesUnder(Path.Combine(local, "GOG.com", "Galaxy")), token, true, CleanupKind.Files);
            AddItem(category, "npm", null, new[] { Path.Combine(local, @"npm-cache\_cacache") }, token, true, CleanupKind.Files);
            AddItem(category, "pip", null, new[] { Path.Combine(local, @"pip\Cache") }, token, true, CleanupKind.Files);
            AddItem(category, "Yarn", null, new[] { Path.Combine(local, @"Yarn\Cache") }, token, true, CleanupKind.Files);
            AddItem(category, "NuGet", null, new[] { Path.Combine(local, @"NuGet\v3-cache") }, token, true, CleanupKind.Files);
            category.Note += RunningNote(new[] { "Discord", "slack", "ms-teams", "Teams", "Spotify", "Code", "EpicGamesLauncher", "steam", "Battle.net", "EADesktop", "upc", "RiotClientServices", "GalaxyClient" });
            return category.Items.Count == 0 ? null : category;
        }

        private static IEnumerable<string> CombineCaches(string[] names, string root)
        {
            foreach (string name in names)
                foreach (string path in AppCacheBits(Path.Combine(root, name))) yield return path;
        }

        private static IEnumerable<string> EpicWebCaches(string local)
        {
            string saved = Path.Combine(local, "EpicGamesLauncher", "Saved");
            if (!Directory.Exists(saved)) yield break;
            string[] directories;
            try { directories = Directory.GetDirectories(saved); }
            catch { directories = new string[0]; }
            foreach (string directory in directories)
                if (Path.GetFileName(directory).StartsWith("webcache", StringComparison.OrdinalIgnoreCase)) yield return directory;
        }

        private static CleanupCategory Shaders(CancellationToken token)
        {
            CleanupCategory category = Category("shaders", "Кэши шейдеров GPU", CleanupSafety.Review, "Безопасно, но при следующем запуске игр возможны короткие подтормаживания из-за повторной компиляции.");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            AddItem(category, "DirectX", null, new[] { Path.Combine(local, "D3DSCache") }, token, false, CleanupKind.Files);
            AddItem(category, "NVIDIA", null, new[] { Path.Combine(local, @"NVIDIA\DXCache"), Path.Combine(local, @"NVIDIA\GLCache"), Path.Combine(local, @"NVIDIA Corporation\NV_Cache") }, token, false, CleanupKind.Files);
            AddItem(category, "AMD", null, new[] { Path.Combine(local, @"AMD\DxCache"), Path.Combine(local, @"AMD\DxcCache"), Path.Combine(local, @"AMD\GLCache"), Path.Combine(local, @"AMD\VkCache") }, token, false, CleanupKind.Files);
            AddItem(category, "Intel", null, new[] { Path.Combine(local, @"Intel\ShaderCache") }, token, false, CleanupKind.Files);
            AddItem(category, "Steam", null, new[] { Path.Combine(pf, @"Steam\steamapps\shadercache") }, token, false, CleanupKind.Files);
            return category.Items.Count == 0 ? null : category;
        }

        private static CleanupCategory Crashes(CancellationToken token)
        {
            CleanupCategory category = Category("crash", "Дампы и отчёты о сбоях", CleanupSafety.Safe, "Диагностические дампы и WER-отчёты. Если вы разбираете недавний сбой, сначала сохраните нужный дамп.");
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            AddItem(category, "Дампы приложений", null, new[] { Path.Combine(local, "CrashDumps") }, token, true, CleanupKind.Files);
            AddItem(category, "Отчёты Windows Error Reporting", null, new[] { Path.Combine(local, @"Microsoft\Windows\WER"), Path.Combine(programData, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(programData, @"Microsoft\Windows\WER\ReportQueue") }, token, true, CleanupKind.Files);
            return category.Items.Count == 0 ? null : category;
        }

        private static CleanupCategory WindowsLeftovers(bool isAdmin, CancellationToken token)
        {
            CleanupCategory category = Category("windows", "Остатки Windows Update", CleanupSafety.Review, "Требуется администратор. Занятые файлы пропускаются; службы Windows не останавливаются.");
            if (!isAdmin)
            {
                category.Blocked = true;
                category.Note = "Нужен запуск PCWatch от администратора. Без него категория только показана и не изменяется.";
                return category;
            }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            AddItem(category, "Загруженные обновления", null, new[] { Path.Combine(windows, @"SoftwareDistribution\Download") }, token, false, CleanupKind.Files);
            AddItem(category, "Delivery Optimization", null, new[] { Path.Combine(windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") }, token, false, CleanupKind.Files);
            AddItem(category, "Windows Temp (старше 1 дня)", OneDay, new[] { Path.Combine(windows, "Temp") }, token, false, CleanupKind.Files);
            AddItem(category, "Системные дампы", null, new[] { Path.Combine(windows, "Minidump"), Path.Combine(windows, "MEMORY.DMP") }, token, false, CleanupKind.Files);
            return category;
        }

        private static CleanupCategory Installers(CancellationToken token)
        {
            CleanupCategory category = Category("installers", "Старые установщики", CleanupSafety.Review, "Только файлы установщиков старше 7 дней, лежащие непосредственно в Downloads. По умолчанию не выбраны и отправляются в корзину.");
            string downloads = NativeCleanup.DownloadsPath;
            if (!Directory.Exists(downloads)) return null;
            HashSet<string> extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle" };
            DateTime cutoff = DateTime.Now.AddDays(-7);
            try
            {
                foreach (string path in Directory.EnumerateFiles(downloads, "*", SearchOption.TopDirectoryOnly))
                {
                    token.ThrowIfCancellationRequested();
                    FileInfo file = new FileInfo(path);
                    if (!extensions.Contains(file.Extension)) continue;
                    DateTime touched = file.CreationTime > file.LastWriteTime ? file.CreationTime : file.LastWriteTime;
                    if (touched > cutoff) continue;
                    AddFileItem(category, file, "Изменён " + touched.ToString("dd.MM.yyyy", RussianFormat.Culture), false, true);
                }
            }
            catch { }
            category.Items = SortItems(category.Items);
            return category.Items.Count == 0 ? null : category;
        }

        private static CleanupCategory BigDownloads(CancellationToken token)
        {
            CleanupCategory category = Category("downloads", "Крупные старые загрузки", CleanupSafety.Review, "Файлы больше 250 МБ, не изменявшиеся 30 дней. По умолчанию не выбраны и отправляются в корзину.");
            string downloads = NativeCleanup.DownloadsPath;
            if (!Directory.Exists(downloads)) return null;
            HashSet<string> installers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle" };
            DateTime cutoff = DateTime.Now.AddDays(-30);
            try
            {
                foreach (string path in FileOps.SafeFiles(downloads))
                {
                    token.ThrowIfCancellationRequested();
                    FileInfo file = new FileInfo(path);
                    if (file.Length < 250L * 1024 * 1024 || installers.Contains(file.Extension)) continue;
                    DateTime touched = file.CreationTime > file.LastWriteTime ? file.CreationTime : file.LastWriteTime;
                    if (touched > cutoff) continue;
                    AddFileItem(category, file, "Не изменялся с " + touched.ToString("dd.MM.yyyy", RussianFormat.Culture), false, true);
                }
            }
            catch { }
            category.Items = SortItems(category.Items);
            return category.Items.Count == 0 ? null : category;
        }

        private static IList<CleanupItem> SortItems(IList<CleanupItem> items)
        {
            List<CleanupItem> sorted = new List<CleanupItem>(items);
            sorted.Sort(delegate(CleanupItem a, CleanupItem b) { return b.Size.CompareTo(a.Size); });
            return sorted;
        }

        private static CleanupCategory WindowsOld(CancellationToken token)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\..", "Windows.old");
            path = Path.GetFullPath(path);
            if (!Directory.Exists(path)) return null;
            CleanupCategory category = Category("winold", "Windows.old", CleanupSafety.Manual, "Удаление выполняет системная Очистка диска Windows. PCWatch не удаляет эту папку самостоятельно.");
            CleanupItem item = new CleanupItem();
            item.Name = "Windows.old";
            item.Paths = new List<string> { path };
            long bytes; int files;
            FileOps.Measure(path, null, out bytes, out files, token);
            item.Size = bytes;
            item.FileCount = files;
            item.Kind = CleanupKind.WindowsOld;
            item.Detail = "Открыть Очистку диска Windows";
            category.Items.Add(item);
            return category;
        }

        private static CleanupCategory Hibernation()
        {
            long size = NativeCleanup.HibernationSize();
            if (size <= 0) return null;
            CleanupCategory category = Category("hibernation", "Файл гибернации", CleanupSafety.Manual, "Отключается через powercfg. Вместе с гибернацией отключается быстрый запуск; включить можно здесь же.");
            CleanupItem item = new CleanupItem();
            item.Name = "hiberfil.sys";
            item.Paths = new List<string> { NativeCleanup.HibernationPath };
            item.Size = size;
            item.FileCount = 1;
            item.Kind = CleanupKind.Hibernation;
            item.Detail = "Управление гибернацией Windows";
            category.Items.Add(item);
            return category;
        }

        private static string RunningNote(string[] processNames)
        {
            List<string> running = new List<string>();
            foreach (string name in processNames)
            {
                try
                {
                    Process[] processes = Process.GetProcessesByName(name);
                    if (processes.Length > 0) running.Add(name);
                    foreach (Process process in processes) process.Dispose();
                }
                catch { }
            }
            return running.Count == 0 ? "" : " Сейчас запущены: " + String.Join(", ", running.ToArray()) + ". Закройте их для более полной очистки; занятые файлы будут пропущены.";
        }
    }
}
