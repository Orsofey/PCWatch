using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using PCWatch.Core;

namespace PCWatch.Core.Tests
{
    public static class StartupTests
    {
        private static int _failures;
        public static int Run()
        {
            _failures = 0;
            TestRegistryRun();
            TestStartupFolder();
            TestProtectedService();
            TestScheduledTasks();
            TestNonStringValueBlocked();
            TestBackupCorruptionAbortsMutation();
            TestPreDisabledEnableRejected();
            return _failures;
        }

        private static void Check(string name, bool condition)
        {
            if (condition) Console.WriteLine("[OK]   " + name);
            else { _failures++; Console.WriteLine("[FAIL] " + name); }
        }

        private static string TestFolder(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "PCWatchTests", name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TestRegistryRun()
        {
            string backupFolder = TestFolder("startup-registry");
            string backupFile = Path.Combine(backupFolder, "startup-backup.json");
            string subKey = @"Software\PCWatchTests\Run-" + Guid.NewGuid().ToString("N");
            string valueName = "PCWatchTestApp";
            string command = "%PCWATCHTESTDIR%\\PCWatchTestApp.exe";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey))
                    key.SetValue(valueName, command, RegistryValueKind.ExpandString);
                Environment.SetEnvironmentVariable("PCWATCHTESTDIR", backupFolder, EnvironmentVariableTarget.Process);
                StartupRegistryRoot root = new StartupRegistryRoot { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, SubKey = subKey, RequiresAdmin = false };
                StartupService service = new StartupService(backupFile, null, null, false, new List<StartupRegistryRoot> { root });
                StartupItem item = Find(service.Scan(), subKey, valueName);
                Check("Автозапуск: запись Run найдена", item != null && item.Enabled && item.Kind == StartupItemKind.RegistryRun);
                if (item == null) return;
                service.Disable(item);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey)) Check("Автозапуск: значение Run удалено", key.GetValue(valueName) == null);
                StartupItem disabled = Find(service.Scan(), subKey, valueName);
                Check("Автозапуск: отключённая запись видна", disabled != null && !disabled.Enabled && disabled.Backup != null);
                if (disabled == null) return;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, true)) key.SetValue(valueName, "NewerApplication.exe", RegistryValueKind.String);
                bool overwriteBlocked = false;
                try { service.Enable(disabled); }
                catch (InvalidOperationException) { overwriteBlocked = true; }
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, true))
                {
                    Check("Автозапуск: новое значение не перезаписано", overwriteBlocked && Convert.ToString(key.GetValue(valueName)) == "NewerApplication.exe");
                    key.DeleteValue(valueName, false);
                }
                service.Enable(disabled);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey)) Check("Автозапуск: значение Run восстановлено", key != null && Convert.ToString(key.GetValue(valueName)) == Environment.ExpandEnvironmentVariables(command));
                Check("Автозапуск: резервная копия удалена", !File.Exists(backupFile) || File.ReadAllText(backupFile).Contains("[]"));
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
                if (Directory.Exists(backupFolder)) Directory.Delete(backupFolder, true);
            }
        }

        private static void TestStartupFolder()
        {
            string backupFolder = TestFolder("startup-folder");
            string backupFile = Path.Combine(backupFolder, "startup-backup.json");
            string startupFolder = TestFolder("startup-folder-source");
            string path = Path.Combine(startupFolder, "PCWatchTest.bat");
            File.WriteAllText(path, "@echo PCWatch test");
            StartupService service = new StartupService(backupFile, startupFolder, null, false, null);
            StartupItem item = Find(service.Scan(), startupFolder, "PCWatchTest.bat");
            Check("Автозапуск: файл Startup найден", item != null && item.Kind == StartupItemKind.StartupFolder);
            if (item == null) return;
            service.Disable(item);
            Check("Автозапуск: файл Startup перемещён в копию", !File.Exists(path) && File.Exists(item.Backup.BackupPath));
            StartupItem disabled = Find(service.Scan(), startupFolder, "PCWatchTest.bat");
            Check("Автозапуск: отключённый файл виден", disabled != null && !disabled.Enabled);
            if (disabled == null) return;
            service.Enable(disabled);
            Check("Автозапуск: файл Startup восстановлен", File.Exists(path) && !File.Exists(disabled.Backup.BackupPath));
            Check("Автозапуск: запись Startup удалена из копии", !File.Exists(backupFile) || File.ReadAllText(backupFile).Contains("[]"));
        }

        private static void TestNonStringValueBlocked()
        {
            string backupFolder = TestFolder("startup-nonstring");
            string subKey = @"Software\PCWatchTests\RunNonString-" + Guid.NewGuid().ToString("N");
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKey))
                    key.SetValue("BinaryApp", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
                StartupRegistryRoot root = new StartupRegistryRoot { Hive = RegistryHive.CurrentUser, View = RegistryView.Default, SubKey = subKey, RequiresAdmin = false };
                StartupService service = new StartupService(Path.Combine(backupFolder, "startup-backup.json"), null, null, false, new List<StartupRegistryRoot> { root });
                StartupItem item = Find(service.Scan(), subKey, "BinaryApp");
                Check("Автозапуск: нестроковое значение защищено", item != null && !item.CanToggle);
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(subKey, false);
                if (Directory.Exists(backupFolder)) Directory.Delete(backupFolder, true);
            }
        }

        private static void TestBackupCorruptionAbortsMutation()
        {
            string backupFolder = TestFolder("startup-corruption");
            string backupFile = Path.Combine(backupFolder, "startup-backup.json");
            string startupFolder = TestFolder("startup-corruption-source");
            string first = Path.Combine(startupFolder, "First.bat"), second = Path.Combine(startupFolder, "Second.bat");
            File.WriteAllText(first, "first");
            File.WriteAllText(second, "second");
            StartupService service = new StartupService(backupFile, startupFolder, null, false, null);
            StartupItem item = Find(service.Scan(), startupFolder, "First.bat");
            if (item == null) { Check("Автозапуск: подготовка повреждения копии", false); return; }
            service.Disable(item);
            File.Copy(backupFile, backupFile + ".previous", true);
            File.WriteAllText(backupFile, "not-json");
            bool blocked = false;
            try
            {
                StartupItem secondItem = Find(service.Scan(), startupFolder, "Second.bat");
                if (secondItem != null) service.Disable(secondItem);
            }
            catch (InvalidDataException) { blocked = true; }
            Check("Автозапуск: повреждение backup останавливает операцию", blocked && File.Exists(second));
            if (Directory.Exists(startupFolder)) Directory.Delete(startupFolder, true);
            if (Directory.Exists(backupFolder)) Directory.Delete(backupFolder, true);
        }

        private static void TestPreDisabledEnableRejected()
        {
            StartupItem item = new StartupItem();
            item.Id = "task:pre-disabled";
            item.Name = "Pre-disabled";
            item.Kind = StartupItemKind.ScheduledTask;
            item.Location = "\\Pre-disabled";
            item.Enabled = false;
            item.CanToggle = true;
            bool blocked = false;
            try { new StartupService(Path.Combine(TestFolder("startup-pre-disabled"), "startup-backup.json"), null, null, true, null).Enable(item); }
            catch (InvalidOperationException) { blocked = true; }
            Check("Автозапуск: pre-disabled без backup не включается", blocked);
        }

        private static void TestProtectedService()
        {
            StartupItem item = new StartupItem();
            item.Id = "service:WinDefend";
            item.Name = "Microsoft Defender Antivirus Service";
            item.Kind = StartupItemKind.Service;
            item.Location = "WinDefend";
            item.CanToggle = false;
            item.ProtectionReason = "Критическая служба Windows";
            bool blocked = false;
            try { new StartupService(Path.Combine(TestFolder("startup-protected"), "startup-backup.json"), null, null, false, null).Disable(item); }
            catch (InvalidOperationException) { blocked = true; }
            Check("Автозапуск: критическая служба защищена", blocked);
        }

        private static void TestScheduledTasks()
        {
            StartupService service = new StartupService(Path.Combine(TestFolder("startup-tasks"), "startup-backup.json"), null, null, true, null);
            IList<StartupItem> items = service.Scan();
            int taskCount = 0;
            foreach (StartupItem item in items) if (item.Kind == StartupItemKind.ScheduledTask) taskCount++;
            if (taskCount == 0) { Console.WriteLine("[SKIP] Boot/Logon-задачи в этой среде не найдены"); return; }
            bool valid = true;
            foreach (StartupItem item in items)
                if (item.Kind == StartupItemKind.ScheduledTask && (String.IsNullOrWhiteSpace(item.Id) || String.IsNullOrWhiteSpace(item.Location))) valid = false;
            Check("Автозапуск: Boot/Logon-задачи планировщика сканируются", taskCount > 0 && valid);
        }

        private static StartupItem Find(IList<StartupItem> items, string location, string name)
        {
            foreach (StartupItem item in items)
                if (item.Location.IndexOf(location, StringComparison.OrdinalIgnoreCase) >= 0
                    && (String.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) || item.Location.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
                    return item;
            return null;
        }
    }
}
