using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace PCWatch.Core
{
    public static class KnownApplications
    {
        public static string ResolveExecutablePath(string processName)
        {
            string name = Normalize(processName);
            if (name.Length == 0) return null;
            string registered = FromAppPaths(name + ".exe");
            if (registered != null) return registered;
            foreach (string candidate in Candidates(name))
                if (!String.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
            string dynamic = ResolveDynamic(name);
            if (dynamic != null && File.Exists(dynamic)) return dynamic;
            return FromPath(name + ".exe");
        }

        public static string Normalize(string processName)
        {
            string name = processName == null ? "" : processName.Trim().ToLowerInvariant();
            int hash = name.IndexOf('#');
            if (hash > 0) name = name.Substring(0, hash);
            if (name.EndsWith(".exe", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 4);
            return name;
        }

        private static IEnumerable<string> Candidates(string name)
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string system = Environment.SystemDirectory;
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            switch (name)
            {
                case "chrome": return Paths(pf + @"\Google\Chrome\Application\chrome.exe", pfx + @"\Google\Chrome\Application\chrome.exe", local + @"\Google\Chrome\Application\chrome.exe");
                case "msedge": return Paths(pfx + @"\Microsoft\Edge\Application\msedge.exe", pf + @"\Microsoft\Edge\Application\msedge.exe");
                case "firefox": return Paths(pf + @"\Mozilla Firefox\firefox.exe", pfx + @"\Mozilla Firefox\firefox.exe");
                case "opera": return Paths(local + @"\Programs\Opera\opera.exe", pf + @"\Opera\opera.exe", pfx + @"\Opera\opera.exe");
                case "brave": return Paths(pf + @"\BraveSoftware\Brave-Browser\Application\brave.exe", pfx + @"\BraveSoftware\Brave-Browser\Application\brave.exe", local + @"\BraveSoftware\Brave-Browser\Application\brave.exe");
                case "browser": return Paths(local + @"\Yandex\YandexBrowser\Application\browser.exe", pfx + @"\Yandex\YandexBrowser\Application\browser.exe");
                case "code": return Paths(local + @"\Programs\Microsoft VS Code\Code.exe", pf + @"\Microsoft VS Code\Code.exe", pfx + @"\Microsoft VS Code\Code.exe");
                case "cursor": return Paths(local + @"\Programs\cursor\Cursor.exe", pf + @"\Cursor\Cursor.exe");
                case "notepad": return Paths(windows + @"\notepad.exe", system + @"\notepad.exe");
                case "notepad++": return Paths(pf + @"\Notepad++\notepad++.exe", pfx + @"\Notepad++\notepad++.exe");
                case "explorer": return Paths(windows + @"\explorer.exe");
                case "taskmgr": return Paths(system + @"\taskmgr.exe");
                case "cmd": return Paths(system + @"\cmd.exe");
                case "powershell": return Paths(system + @"\WindowsPowerShell\v1.0\powershell.exe");
                case "pwsh": return Paths(pf + @"\PowerShell\7\pwsh.exe", pfx + @"\PowerShell\7\pwsh.exe");
                case "windowsterminal":
                case "wt": return Paths(local + @"\Microsoft\WindowsApps\wt.exe");
                case "systemsettings": return Paths(system + @"\SystemSettings.exe");
                case "shellexperiencehost": return Paths(system + @"\ShellExperienceHost.exe");
                case "startmenuexperiencehost": return Paths(system + @"\StartMenuExperienceHost.exe");
                case "searchui": return Paths(system + @"\SearchUI.exe");
                case "textinputhost": return Paths(system + @"\TextInputHost.exe");
                case "applicationframehost": return Paths(system + @"\ApplicationFrameHost.exe");
                case "runtimebroker": return Paths(system + @"\RuntimeBroker.exe");
                case "winword": return Office(pf, pfx, "WINWORD.EXE");
                case "excel": return Office(pf, pfx, "EXCEL.EXE");
                case "powerpnt": return Office(pf, pfx, "POWERPNT.EXE");
                case "outlook": return Office(pf, pfx, "OUTLOOK.EXE");
                case "telegram": return Paths(roaming + @"\Telegram Desktop\Telegram.exe", local + @"\Telegram Desktop\Telegram.exe");
                case "whatsapp": return Paths(local + @"\WhatsApp\WhatsApp.exe", pf + @"\WhatsApp\WhatsApp.exe");
                case "spotify": return Paths(roaming + @"\Spotify\Spotify.exe", local + @"\Spotify\Spotify.exe");
                case "steam": return Paths(pfx + @"\Steam\steam.exe", pf + @"\Steam\steam.exe");
                case "steamwebhelper": return Paths(pfx + @"\Steam\bin\cef\cef.win7x64\steamwebhelper.exe", pfx + @"\Steam\bin\cef\cef.win7\steamwebhelper.exe");
                case "epicgameslauncher": return Paths(pfx + @"\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", pfx + @"\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe");
                case "epicwebhelper": return Paths(pfx + @"\Epic Games\Launcher\Engine\Binaries\Win64\EpicWebHelper.exe", pfx + @"\Epic Games\Launcher\Engine\Binaries\Win32\EpicWebHelper.exe");
                case "vlc": return Paths(pf + @"\VideoLAN\VLC\vlc.exe", pfx + @"\VideoLAN\VLC\vlc.exe");
                case "obs64": return Paths(pf + @"\obs-studio\bin\64bit\obs64.exe", pfx + @"\obs-studio\bin\64bit\obs64.exe");
                case "node": return Paths(pf + @"\nodejs\node.exe", pfx + @"\nodejs\node.exe");
                case "rustc":
                case "cargo": return Paths(user + @"\.cargo\bin\" + name + ".exe");
                case "docker desktop": return Paths(pf + @"\Docker\Docker\Docker Desktop.exe");
                case "com.docker.backend": return Paths(pf + @"\Docker\Docker\resources\com.docker.backend.exe");
                case "googleupdate": return Paths(pfx + @"\Google\Update\GoogleUpdate.exe", pf + @"\Google\Update\GoogleUpdate.exe");
                case "microsoftedgeupdate": return Paths(pfx + @"\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe", pf + @"\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe");
                case "ccxprocess": return Paths(pf + @"\Adobe\Adobe Creative Cloud Experience\CCXProcess.exe", pf + @"\Adobe\Adobe Creative Cloud\CCXProcess\CCXProcess.exe", pfx + @"\Adobe\Adobe Creative Cloud\CCXProcess\CCXProcess.exe");
                case "creative cloud": return Paths(pf + @"\Adobe\Adobe Creative Cloud\ACC\Creative Cloud.exe", pfx + @"\Adobe\Adobe Creative Cloud\ACC\Creative Cloud.exe");
                case "adobeipcbroker": return Paths(pf + @"\Common Files\Adobe\Adobe Desktop Common\IPCBox\AdobeIPCBroker.exe", pfx + @"\Common Files\Adobe\Adobe Desktop Common\IPCBox\AdobeIPCBroker.exe");
                case "adobe desktop service": return Paths(pf + @"\Common Files\Adobe\Adobe Desktop Common\ADS\Adobe Desktop Service.exe", pfx + @"\Common Files\Adobe\Adobe Desktop Common\ADS\Adobe Desktop Service.exe");
                case "vsservicing": return Paths(pfx + @"\Microsoft Visual Studio\Installer\resources\app\ServiceHub\Services\Microsoft.VisualStudio.Setup.Service\VSServicing.exe", pf + @"\Microsoft Visual Studio\Installer\resources\app\ServiceHub\Services\Microsoft.VisualStudio.Setup.Service\VSServicing.exe");
                case "nvdisplay.container": return Paths(pf + @"\NVIDIA Corporation\Display.NvContainer\NVDisplay.Container.exe", pfx + @"\NVIDIA Corporation\Display.NvContainer\NVDisplay.Container.exe");
                case "svchost": return Paths(system + @"\svchost.exe");
                case "spoolsv": return Paths(system + @"\spoolsv.exe");
                case "services": return Paths(system + @"\services.exe");
                case "lsass": return Paths(system + @"\lsass.exe");
                case "csrss": return Paths(system + @"\csrss.exe");
                case "smss": return Paths(system + @"\smss.exe");
                case "wininit": return Paths(system + @"\wininit.exe");
                case "winlogon": return Paths(system + @"\winlogon.exe");
                case "dwm": return Paths(system + @"\dwm.exe");
                case "sihost": return Paths(system + @"\sihost.exe");
                case "taskhostw": return Paths(system + @"\taskhostw.exe");
                case "conhost": return Paths(system + @"\conhost.exe");
                case "ctfmon": return Paths(system + @"\ctfmon.exe");
                case "dllhost": return Paths(system + @"\dllhost.exe");
                case "wudfhost": return Paths(system + @"\WUDFHost.exe");
                case "dashost": return Paths(system + @"\dasHost.exe");
                case "fontdrvhost": return Paths(system + @"\fontdrvhost.exe");
                case "searchindexer": return Paths(system + @"\SearchIndexer.exe");
                case "searchprotocolhost": return Paths(system + @"\SearchProtocolHost.exe");
                case "searchfilterhost": return Paths(system + @"\SearchFilterHost.exe");
                case "wmiprvse": return Paths(system + @"\wbem\WmiPrvSE.exe");
                case "msmpeng": return Paths(pf + @"\Windows Defender\MsMpEng.exe", system + @"\MsMpEng.exe");
                case "securityhealthservice": return Paths(system + @"\SecurityHealthService.exe");
                case "system":
                case "registry":
                case "memory compression": return Paths(windows + @"\System32\ntoskrnl.exe");
                default: return new string[0];
            }
        }

        private static string ResolveDynamic(string name)
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (name == "discord") return NewestFile(Path.Combine(local, "Discord"), "app-*", "Discord.exe");
                if (name == "discordupdate") return Path.Combine(local, "Discord", "Update.exe");
                if (name == "devenv")
                {
                    string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string[] editions = { "Enterprise", "Professional", "Community", "Preview" };
                    foreach (string year in new[] { "2022", "2019" })
                        foreach (string edition in editions)
                        {
                            string path = Path.Combine(pf, "Microsoft Visual Studio", year, edition, @"Common7\IDE\devenv.exe");
                            if (File.Exists(path)) return path;
                        }
                }
                if (name == "python" || name == "pythonw")
                {
                    string root = Path.Combine(local, "Programs", "Python");
                    if (Directory.Exists(root))
                    {
                        string[] folders = Directory.GetDirectories(root, "Python*");
                        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
                        for (int i = folders.Length - 1; i >= 0; i--)
                        {
                            string path = Path.Combine(folders[i], name + ".exe");
                            if (File.Exists(path)) return path;
                        }
                    }
                }
                if (name == "1cv8" || name == "1cv8c")
                {
                    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "1cv8");
                    if (Directory.Exists(root))
                    {
                        string[] files = Directory.GetFiles(root, name + ".exe", SearchOption.AllDirectories);
                        if (files.Length > 0) return files[0];
                    }
                }
            }
            catch { }
            return null;
        }

        private static string NewestFile(string root, string folderPattern, string fileName)
        {
            if (!Directory.Exists(root)) return null;
            DirectoryInfo[] folders = new DirectoryInfo(root).GetDirectories(folderPattern);
            Array.Sort(folders, delegate(DirectoryInfo a, DirectoryInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
            foreach (DirectoryInfo folder in folders)
            {
                string path = Path.Combine(folder.FullName, fileName);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static string FromAppPaths(string fileName)
        {
            string[] roots = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" };
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (string root in roots)
                    try
                    {
                        using (RegistryKey key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(root + fileName))
                            if (key != null)
                            {
                                string path = Convert.ToString(key.GetValue(null));
                                if (File.Exists(path)) return path;
                                string folder = Convert.ToString(key.GetValue("Path"));
                                string combined = Path.Combine(folder ?? "", fileName);
                                if (File.Exists(combined)) return combined;
                            }
                    }
                    catch { }
            return null;
        }

        private static string FromPath(string fileName)
        {
            try
            {
                foreach (string folder in Environment.GetEnvironmentVariable("PATH").Split(Path.PathSeparator))
                {
                    string path = Path.Combine(folder.Trim(), fileName);
                    if (File.Exists(path)) return path;
                }
            }
            catch { }
            return null;
        }

        private static string[] Office(string pf, string pfx, string file)
        {
            return Paths(pf + @"\Microsoft Office\root\Office16\" + file, pfx + @"\Microsoft Office\root\Office16\" + file, pf + @"\Microsoft Office\Office16\" + file, pfx + @"\Microsoft Office\Office16\" + file);
        }

        private static string[] Paths(params string[] paths) { return paths; }
    }
}
