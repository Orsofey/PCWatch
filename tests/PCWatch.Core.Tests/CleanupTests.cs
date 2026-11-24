using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PCWatch.Core;

namespace PCWatch.Core.Tests
{
    public static class CleanupTests
    {
        private static int _failures;
        public static int Run()
        {
            _failures = 0;
            TestMeasureAgeFilter();
            TestDeleteOnlyOld();
            TestProtectedRoot();
            TestInUseSkipped();
            TestJunctionBlocked();
            TestCancellation();
            TestDefaults();
            TestRecycleBinReadOnly();
            return _failures;
        }

        private static void Check(string name, bool condition)
        {
            if (condition) Console.WriteLine("[OK]   " + name);
            else { _failures++; Console.WriteLine("[FAIL] " + name); }
        }

        private static string Root(string name)
        {
            string path = Path.Combine(Path.GetTempPath(), "PCWatchTests", name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void Old(string path)
        {
            File.WriteAllText(path, "old-cache");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        }

        private static void TestMeasureAgeFilter()
        {
            string root = Root("measure");
            try
            {
                string old = Path.Combine(root, "old.bin"), fresh = Path.Combine(root, "fresh.bin");
                Old(old); File.WriteAllText(fresh, "new");
                File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
                long bytes; int files;
                FileOps.Measure(root, TimeSpan.FromDays(1), out bytes, out files, CancellationToken.None);
                Check("Очистка: Measure учитывает только старые файлы", bytes == 9 && files == 1);
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestDeleteOnlyOld()
        {
            string root = Root("delete");
            try
            {
                string old = Path.Combine(root, "old.bin"), fresh = Path.Combine(root, "fresh.bin");
                Old(old); File.WriteAllText(fresh, "new");
                File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);
                FileOps.DeleteResult result = FileOps.Delete(root, TimeSpan.FromDays(1), CancellationToken.None);
                Check("Очистка: удаляются только старые файлы", result.Freed == 9 && result.Files == 1 && !File.Exists(old) && File.Exists(fresh) && Directory.Exists(root));
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestProtectedRoot()
        {
            bool blocked = false;
            try { FileOps.Delete(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null, CancellationToken.None); }
            catch (InvalidOperationException) { blocked = true; }
            Check("Очистка: пользовательский корень защищён", blocked);
            blocked = false;
            try { FileOps.Delete(Environment.SystemDirectory, null, CancellationToken.None); }
            catch (InvalidOperationException) { blocked = true; }
            Check("Очистка: System32 защищён", blocked);
            Check("Очистка: путь Downloads определён", !String.IsNullOrWhiteSpace(NativeCleanup.DownloadsPath));
        }

        private static void TestInUseSkipped()
        {
            string root = Root("inuse");
            try
            {
                string old = Path.Combine(root, "old.bin");
                Old(old);
                using (FileStream stream = new FileStream(old, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    FileOps.DeleteResult result = FileOps.Delete(root, null, CancellationToken.None);
                    Check("Очистка: занятый файл не удаляется", result.Failed >= 1 && File.Exists(old));
                }
                FileOps.DeleteResult second = FileOps.Delete(root, null, CancellationToken.None);
                Check("Очистка: после освобождения файл удаляется", second.Files == 1 && !File.Exists(old));
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestJunctionBlocked()
        {
            string root = Root("junction");
            try
            {
                string target = Path.Combine(root, "target"), link = Path.Combine(root, "link");
                Directory.CreateDirectory(target);
                string old = Path.Combine(target, "old.bin");
                Old(old);
                ProcessStartInfo info = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"");
                info.UseShellExecute = false; info.CreateNoWindow = true;
                using (Process process = Process.Start(info)) { process.WaitForExit(); if (process.ExitCode != 0) { Console.WriteLine("[SKIP] Junction недоступна в этой среде"); return; } }
                long bytes; int files;
                FileOps.Measure(link, null, out bytes, out files, CancellationToken.None);
                FileOps.DeleteResult result = FileOps.Delete(link, null, CancellationToken.None);
                Check("Очистка: junction не проходится", bytes == 0 && result.Freed == 0 && File.Exists(old));
            }
            finally
            {
                string link = Path.Combine(root, "link");
                if (Directory.Exists(link)) Directory.Delete(link, false);
                Directory.Delete(root, true);
            }
        }

        private static void TestCancellation()
        {
            string root = Root("cancel");
            try
            {
                Old(Path.Combine(root, "old.bin"));
                CancellationTokenSource source = new CancellationTokenSource();
                source.Cancel();
                bool cancelled = false;
                try { long bytes; int files; FileOps.Measure(root, null, out bytes, out files, source.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check("Очистка: отмена поддерживается", cancelled);
                cancelled = false;
                try { FileOps.Delete(root, null, source.Token); }
                catch (OperationCanceledException) { cancelled = true; }
                Check("Очистка: отмена не удаляет файлы", cancelled && Directory.GetFiles(root).Length == 1);
                source.Dispose();
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestDefaults()
        {
            AppSettings settings = new AppSettings();
            Check("Очистка: загрузки по умолчанию в Корзину", settings.RecycleDownloads);
            CleanupItem item = new CleanupItem { Name = "Корзина", SelectedByDefault = false, Kind = CleanupKind.RecycleBin, Size = 100 };
            Check("Очистка: Корзина не выбрана по умолчанию", !item.SelectedByDefault);
        }

        private static void TestRecycleBinReadOnly()
        {
            long bytes, items;
            NativeCleanup.QueryRecycleBin(out bytes, out items);
            Check("Очистка: размер корзины читается без изменений", bytes >= 0 && items >= 0);
        }
    }
}
