using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Win32;
using System.Management.Automation;

namespace PCWatch.Core
{
    public sealed class StartupService
    {
        private readonly string _backupFile;
        private readonly string _userStartupFolder;
        private readonly string _commonStartupFolder;
        private readonly bool _includeServicesAndTasks;
        private readonly IList<StartupRegistryRoot> _registryRoots;

        public StartupService() : this(null, null, null, true, null) { }
        public StartupService(string backupFile, string userStartupFolder, string commonStartupFolder, bool includeServicesAndTasks, IList<StartupRegistryRoot> registryRoots)
        {
            _backupFile = backupFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCWatch", "startup-backup.json");
            _userStartupFolder = userStartupFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\Startup");
            _commonStartupFolder = commonStartupFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs\Startup");
            _includeServicesAndTasks = includeServicesAndTasks;
            _registryRoots = registryRoots;
        }

        public IList<StartupItem> Scan()
        {
            List<StartupItem> result = new List<StartupItem>();
            ReadRegistry(result);
            ReadStartupFolder(result, _userStartupFolder, false);
            ReadStartupFolder(result, _commonStartupFolder, true);
            if (_includeServicesAndTasks)
            {
                ReadServices(result);
                ReadScheduledTasks(result);
            }
            MergeBackups(result);
            result.Sort(delegate(StartupItem a, StartupItem b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name); });
            return result;
        }

        private IList<StartupRegistryRoot> DefaultRegistryRoots()
        {
            return new List<StartupRegistryRoot>
            {
                new StartupRegistryRoot { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RequiresAdmin = false },
                new StartupRegistryRoot { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, SubKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce", RequiresAdmin = false },
                new StartupRegistryRoot { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RequiresAdmin = true },
                new StartupRegistryRoot { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry64, SubKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce", RequiresAdmin = true },
                new StartupRegistryRoot { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32, SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RequiresAdmin = true },
                new StartupRegistryRoot { Hive = RegistryHive.LocalMachine, View = RegistryView.Registry32, SubKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce", RequiresAdmin = true }
            };
        }

        private void ReadRegistry(List<StartupItem> result)
        {
            IList<StartupRegistryRoot> roots = _registryRoots ?? DefaultRegistryRoots();
            foreach (StartupRegistryRoot root in roots)
            {
                try
                {
                    using (RegistryKey key = RegistryKey.OpenBaseKey(root.Hive, root.View).OpenSubKey(root.SubKey, false))
                    {
                        if (key == null) continue;
                        foreach (string valueName in key.GetValueNames())
                        {
                            RegistryValueKind valueKind = key.GetValueKind(valueName);
                            string command = Convert.ToString(key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
                            if (String.IsNullOrWhiteSpace(command)) continue;
                            string icon = ResolveExecutable(command);
                            StartupItem item = NewItem("registry:" + root.Hive + ":" + root.View + ":" + root.SubKey + ":" + valueName,
                                valueName, StartupItemKind.RegistryRun, root.SubKey, command, root.RequiresAdmin, icon);
                            item.Enabled = true;
                            item.Backup = CreateBackup(item);
                            item.Backup.Hive = root.Hive;
                            item.Backup.View = root.View;
                            item.Backup.SubKey = root.SubKey;
                            item.Backup.ValueName = valueName;
                            item.Backup.ValueKind = valueKind;
                            if (valueKind != RegistryValueKind.String && valueKind != RegistryValueKind.ExpandString)
                            {
                                item.CanToggle = false;
                                item.ProtectionReason = "Тип значения не поддерживается для безопасного восстановления";
                            }
                            ApplyProtection(item, valueName);
                            result.Add(item);
                        }
                    }
                }
                catch { }
            }
        }

        private void ReadStartupFolder(List<StartupItem> result, string folder, bool requiresAdmin)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (string path in Directory.GetFiles(folder))
                {
                    string command = path;
                    string icon = path;
                    if (String.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        string target = ResolveShortcut(path);
                        if (!String.IsNullOrWhiteSpace(target)) { command = target; icon = target; }
                    }
                    StartupItem item = NewItem("file:" + path, Path.GetFileNameWithoutExtension(path), StartupItemKind.StartupFolder, path, command, requiresAdmin, icon);
                    item.Enabled = true;
                    item.Backup = CreateBackup(item);
                    result.Add(item);
                }
            }
            catch { }
        }

        private void ReadServices(List<StartupItem> result)
        {
            try
            {
                using (System.Management.ManagementObjectSearcher searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Name, DisplayName, State, StartMode, PathName, ServiceType, DelayedAutoStart FROM Win32_Service WHERE StartMode='Auto'"))
                using (System.Management.ManagementObjectCollection services = searcher.Get())
                    foreach (System.Management.ManagementObject service in services)
                    {
                        string type = Convert.ToString(service["ServiceType"]);
                        if (type.IndexOf("Driver", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        string name = Convert.ToString(service["Name"]);
                        string display = Convert.ToString(service["DisplayName"]);
                        string path = Convert.ToString(service["PathName"]);
                        string mode = service["DelayedAutoStart"] != null && Convert.ToBoolean(service["DelayedAutoStart"]) ? "delayed-auto" : "auto";
                        string icon = ResolveExecutable(path);
                        StartupItem item = NewItem("service:" + name, String.IsNullOrWhiteSpace(display) ? name : display, StartupItemKind.Service, name, path, true, icon);
                        item.OriginalStartMode = mode;
                        item.ProcessState = Convert.ToString(service["State"]);
                        item.Enabled = !String.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase);
                        item.Backup = CreateBackup(item);
                        item.Backup.OriginalStartMode = mode;
                        ApplyServiceProtection(item, name, icon);
                        result.Add(item);
                    }
            }
            catch { }
        }

        private void ReadScheduledTasks(List<StartupItem> result)
        {
            ProcessOutput output = RunProcess("schtasks.exe", "/Query /XML", Encoding.UTF8);
            if (output.ExitCode != 0 || String.IsNullOrWhiteSpace(output.Text)) return;
            try
            {
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(output.Text, "<Task\\b[\\s\\S]*?</Task>"))
                {
                    XmlDocument document = new XmlDocument();
                    document.LoadXml(match.Value);
                    XmlNamespaceManager namespaces = new XmlNamespaceManager(document.NameTable);
                    namespaces.AddNamespace("n", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                    XmlNode task = document.DocumentElement;
                    if (task == null) continue;
                    string uri = Text(task.SelectSingleNode("n:RegistrationInfo/n:URI", namespaces));
                    if (uri.Length == 0) continue;
                    bool startup = task.SelectSingleNode("n:Triggers/n:BootTrigger", namespaces) != null || task.SelectSingleNode("n:Triggers/n:LogonTrigger", namespaces) != null;
                    if (!startup) continue;
                    string enabledText = Text(task.SelectSingleNode("n:Settings/n:Enabled", namespaces));
                    bool enabled = !String.Equals(enabledText, "false", StringComparison.OrdinalIgnoreCase);
                    string command = "";
                    XmlNode action = task.SelectSingleNode("n:Actions/n:Exec/n:Command", namespaces);
                    if (action != null) command = action.InnerText;
                    string arguments = Text(task.SelectSingleNode("n:Actions/n:Exec/n:Arguments", namespaces));
                    if (arguments.Length > 0) command += " " + arguments;
                    string icon = ResolveExecutable(command);
                    bool requiresAdmin = uri.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);
                    StartupItem item = NewItem("task:" + uri, Path.GetFileName(uri.TrimStart('\\')), StartupItemKind.ScheduledTask, uri, command, requiresAdmin, icon);
                    item.Enabled = enabled;
                    item.Backup = CreateBackup(item);
                    item.Backup.RequiresAdmin = requiresAdmin;
                    bool hasOtherTriggers = false;
                    XmlNodeList triggers = task.SelectNodes("n:Triggers/*", namespaces);
                    if (triggers != null)
                        foreach (XmlNode trigger in triggers)
                            if (trigger.LocalName != "BootTrigger" && trigger.LocalName != "LogonTrigger") { hasOtherTriggers = true; break; }
                    if (hasOtherTriggers) { item.CanToggle = false; item.ProtectionReason = "У задачи есть дополнительные триггеры"; }
                    if (!enabled)
                    {
                        StartupBackup persisted = FindBackup(item.Id);
                        if (persisted != null) { item.Backup = persisted; item.CanToggle = true; item.ProtectionReason = ""; }
                        else { item.CanToggle = false; item.ProtectionReason = "Отключено до PCWatch"; }
                    }
                    if (requiresAdmin) { item.CanToggle = false; item.ProtectionReason = "Системная задача Microsoft"; }
                    result.Add(item);
                }
            }
            catch { }
        }

        private static string Text(XmlNode node)
        {
            return node == null ? "" : node.InnerText;
        }

        private static StartupItem NewItem(string id, string name, StartupItemKind kind, string location, string command, bool requiresAdmin, string icon)
        {
            StartupItem item = new StartupItem();
            item.Id = id;
            item.Name = name;
            item.Kind = kind;
            item.Location = location;
            item.Command = command;
            item.RequiresAdmin = requiresAdmin;
            item.IconPath = icon ?? "";
            FillMetadata(item);
            return item;
        }

        private static void ApplyProtection(StartupItem item, string valueName)
        {
            if (String.Equals(valueName, "SecurityHealth", StringComparison.OrdinalIgnoreCase)) { item.CanToggle = false; item.ProtectionReason = "Защита Windows"; }
        }

        private static void ApplyServiceProtection(StartupItem item, string serviceName, string icon)
        {
            string[] protectedNames = { "WinDefend", "WdNisSvc", "MDCoreSvc", "Sense", "EventLog", "RpcSs", "DcomLaunch", "PlugPlay", "Power", "TrustedInstaller", "Schedule", "Winmgmt", "MpsSvc", "BFE", "msiserver", "StateRepository", "SystemEventsBroker", "TimeBrokerSvc", "UserManager", "ProfSvc", "Themes", "AudioSrv", "AudioEndpointBuilder", "lsass", "wininit" };
            foreach (string name in protectedNames)
                if (String.Equals(serviceName, name, StringComparison.OrdinalIgnoreCase)) { item.CanToggle = false; item.ProtectionReason = "Критическая служба Windows"; return; }
            if (item.Publisher.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) { item.CanToggle = false; item.ProtectionReason = "Служба Microsoft"; }
        }

        private static void FillMetadata(StartupItem item)
        {
            if (String.IsNullOrWhiteSpace(item.IconPath) || !File.Exists(item.IconPath)) return;
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(item.IconPath);
                item.Publisher = info.CompanyName ?? "";
                try { item.SignatureStatus = GetSignatureStatus(item.IconPath); } catch { }
            }
            catch { }
        }

        private static string GetSignatureStatus(string path)
        {
            try
            {
                using (PowerShell shell = PowerShell.Create())
                {
                    shell.AddCommand("Get-AuthenticodeSignature").AddParameter("LiteralPath", path);
                    System.Collections.ObjectModel.Collection<PSObject> result = shell.Invoke();
                    if (result.Count > 0 && result[0].Properties["Status"] != null)
                        return Convert.ToString(result[0].Properties["Status"].Value);
                }
            }
            catch { }
            return "";
        }

        private static StartupBackup CreateBackup(StartupItem item)
        {
            StartupBackup backup = new StartupBackup();
            backup.Id = item.Id;
            backup.Name = item.Name;
            backup.Kind = item.Kind;
            backup.Location = item.Location;
            backup.Command = item.Command;
            backup.RequiresAdmin = item.RequiresAdmin;
            backup.IconPath = item.IconPath;
            backup.Publisher = item.Publisher;
            backup.SignatureStatus = item.SignatureStatus;
            backup.OriginalStartMode = item.OriginalStartMode;
            backup.CreatedUtc = DateTime.UtcNow;
            return backup;
        }

        private void MergeBackups(List<StartupItem> result)
        {
            foreach (StartupBackup backup in LoadBackups())
            {
                bool exists = false;
                foreach (StartupItem existing in result) if (String.Equals(existing.Id, backup.Id, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (exists) continue;
                StartupItem item = NewItem(backup.Id, backup.Name, backup.Kind, backup.Location, backup.Command, backup.RequiresAdmin, backup.IconPath);
                item.Enabled = false;
                item.Backup = backup;
                result.Add(item);
            }
        }

        public void Disable(StartupItem item)
        {
            ValidateToggle(item);
            if (item.RequiresAdmin) RequireAdmin();
            if (item.Backup == null) item.Backup = CreateBackup(item);
            if (item.Kind == StartupItemKind.StartupFolder && String.IsNullOrWhiteSpace(item.Backup.BackupPath)
                && File.Exists(item.Location))
            {
                string folder = Path.Combine(Path.GetDirectoryName(_backupFile) ?? "", "Files");
                Directory.CreateDirectory(folder);
                item.Backup.BackupPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".startup");
            }
            SaveBackup(item.Backup);
            switch (item.Kind)
            {
                case StartupItemKind.RegistryRun:
                    using (RegistryKey key = RegistryKey.OpenBaseKey(item.Backup.Hive, item.Backup.View).OpenSubKey(item.Backup.SubKey, true))
                    {
                        if (key == null) throw new InvalidOperationException("Раздел реестра не найден");
                        key.DeleteValue(item.Backup.ValueName, false);
                    }
                    break;
                case StartupItemKind.StartupFolder:
                    File.Move(item.Location, item.Backup.BackupPath);
                    break;
                case StartupItemKind.Service:
                    RunChecked("sc.exe", "config \"" + item.Location + "\" start= disabled", "Не удалось отключить службу");
                    break;
                case StartupItemKind.ScheduledTask:
                    RunChecked("schtasks.exe", "/Change /TN \"" + item.Location + "\" /DISABLE", "Не удалось отключить задачу");
                    break;
            }
        }

        public void Enable(StartupItem item)
        {
            ValidateToggle(item);
            if (item.RequiresAdmin) RequireAdmin();
            StartupBackup backup = FindBackup(item.Id);
            if (backup == null) throw new InvalidOperationException("Нельзя восстановить элемент, который был отключён ранее не PCWatch");
            switch (item.Kind)
            {
                case StartupItemKind.RegistryRun:
                    using (RegistryKey key = RegistryKey.OpenBaseKey(backup.Hive, backup.View).OpenSubKey(backup.SubKey, true))
                    {
                        if (key == null) throw new InvalidOperationException("Раздел реестра не найден");
                        if (key.GetValue(backup.ValueName) != null) throw new InvalidOperationException("Значение уже создано заново. Восстановление отменено, чтобы не перезаписать новую команду.");
                        key.SetValue(backup.ValueName, backup.Command, backup.ValueKind);
                    }
                    break;
                case StartupItemKind.StartupFolder:
                    if (!File.Exists(backup.BackupPath)) throw new FileNotFoundException("Файл резервной копии не найден", backup.BackupPath);
                    if (File.Exists(backup.Location)) throw new InvalidOperationException("В исходном месте уже есть файл. Переместите его вручную перед восстановлением.");
                    File.Move(backup.BackupPath, backup.Location);
                    break;
                case StartupItemKind.Service:
                    if (!String.Equals(GetServiceStartMode(item.Location), "Disabled", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Служба уже включена или её режим изменён. Восстановление отменено.");
                    string mode = String.IsNullOrWhiteSpace(backup.OriginalStartMode) ? "auto" : backup.OriginalStartMode;
                    RunChecked("sc.exe", "config \"" + item.Location + "\" start= " + mode, "Не удалось включить службу");
                    break;
                case StartupItemKind.ScheduledTask:
                    if (IsTaskEnabled(item.Location)) throw new InvalidOperationException("Задача уже включена или изменена. Восстановление отменено.");
                    RunChecked("schtasks.exe", "/Change /TN \"" + item.Location + "\" /ENABLE", "Не удалось включить задачу");
                    break;
            }
            DeleteBackup(backup.Id);
        }

        private StartupBackup FindBackup(string id)
        {
            foreach (StartupBackup backup in LoadBackups())
                if (String.Equals(backup.Id, id, StringComparison.OrdinalIgnoreCase)) return backup;
            return null;
        }

        private static string GetServiceStartMode(string serviceName)
        {
            try
            {
                using (System.Management.ManagementObjectSearcher searcher = new System.Management.ManagementObjectSearcher("SELECT StartMode FROM Win32_Service WHERE Name='" + serviceName.Replace("'", "''") + "'"))
                using (System.Management.ManagementObjectCollection services = searcher.Get())
                    foreach (System.Management.ManagementObject service in services) return Convert.ToString(service["StartMode"]);
            }
            catch { }
            return "";
        }

        private static bool IsTaskEnabled(string taskName)
        {
            ProcessOutput output = RunProcess("schtasks.exe", "/Query /TN \"" + taskName.Replace("\"", "") + "\" /XML", Encoding.UTF8);
            if (output.ExitCode != 0) throw new InvalidOperationException("Не удалось проверить состояние задачи: " + output.Text);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(output.Text, "<Task\\b[\\s\\S]*?</Task>"))
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(match.Value);
                XmlNamespaceManager namespaces = new XmlNamespaceManager(document.NameTable);
                namespaces.AddNamespace("n", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                XmlNode enabled = document.DocumentElement.SelectSingleNode("n:Settings/n:Enabled", namespaces);
                if (enabled != null) return String.Equals(enabled.InnerText, "true", StringComparison.OrdinalIgnoreCase);
            }
            throw new InvalidOperationException("Состояние Enabled в XML задачи не найдено");
        }

        private static void ValidateToggle(StartupItem item)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (!item.CanToggle) throw new InvalidOperationException(String.IsNullOrWhiteSpace(item.ProtectionReason) ? "Элемент защищён от изменений" : item.ProtectionReason);
        }

        private static void RequireAdmin()
        {
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("Для этого элемента нужен запуск PCWatch от администратора");
        }

        private static void RunChecked(string file, string arguments, string error)
        {
            ProcessOutput output = RunProcess(file, arguments, null);
            if (output.ExitCode != 0) throw new InvalidOperationException(error + ": " + output.Text);
        }

        private static ProcessOutput RunProcess(string file, string arguments, Encoding encoding)
        {
            ProcessStartInfo info = new ProcessStartInfo(file, arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            if (encoding != null) { info.StandardOutputEncoding = encoding; info.StandardErrorEncoding = encoding; }
            using (Process process = Process.Start(info))
            {
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Task.WaitAll(stdout, stderr);
                return new ProcessOutput { ExitCode = process.ExitCode, Text = (stdout.Result + stderr.Result).Trim() };
            }
        }

        private List<StartupBackup> LoadBackups()
        {
            if (!File.Exists(_backupFile)) return new List<StartupBackup>();
            try { return ReadBackupFile(_backupFile); }
            catch (Exception error)
            {
                throw new InvalidDataException("Резервная копия автозапуска повреждена. Изменения отменены. Для ручного восстановления используйте startup-backup.previous.json.", error);
            }
        }

        private static List<StartupBackup> ReadBackupFile(string path)
        {
            using (FileStream stream = File.OpenRead(path))
                return (List<StartupBackup>)new DataContractJsonSerializer(typeof(List<StartupBackup>)).ReadObject(stream) ?? new List<StartupBackup>();
        }

        private void SaveBackup(StartupBackup backup)
        {
            List<StartupBackup> backups = LoadBackups();
            bool found = false;
            for (int i = 0; i < backups.Count; i++) if (String.Equals(backups[i].Id, backup.Id, StringComparison.OrdinalIgnoreCase)) { backups[i] = backup; found = true; break; }
            if (!found) backups.Add(backup);
            WriteBackupsAtomically(backups);
        }

        private void DeleteBackup(string id)
        {
            List<StartupBackup> backups = LoadBackups();
            backups.RemoveAll(delegate(StartupBackup backup) { return String.Equals(backup.Id, id, StringComparison.OrdinalIgnoreCase); });
            WriteBackupsAtomically(backups);
        }

        private void WriteBackupsAtomically(List<StartupBackup> backups)
        {
            string folder = Path.GetDirectoryName(_backupFile);
            if (!String.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
            string temporary = _backupFile + ".tmp";
            string previous = _backupFile + ".previous";
            using (FileStream stream = File.Create(temporary))
            {
                new DataContractJsonSerializer(typeof(List<StartupBackup>)).WriteObject(stream, backups);
                stream.Flush(true);
            }
            if (File.Exists(_backupFile)) File.Replace(temporary, _backupFile, previous);
            else File.Move(temporary, _backupFile);
        }

        private static string ResolveShortcut(string path)
        {
            object shell = null, shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                shell = Activator.CreateInstance(type);
                shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                return Convert.ToString(shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
            }
            catch { return null; }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static string ResolveExecutable(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return "";
            string value = command.Trim();
            if (value.StartsWith("\"") && value.IndexOf('"', 1) > 1)
            {
                string path = value.Substring(1, value.IndexOf('"', 1) - 1);
                if (File.Exists(path)) return path;
            }
            int exe = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0)
            {
                string path = value.Substring(0, exe + 4).Trim().Trim('"');
                if (File.Exists(path)) return path;
            }
            return "";
        }

        private sealed class ProcessOutput
        {
            public int ExitCode;
            public string Text;
        }
    }
}
