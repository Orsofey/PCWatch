using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PCWatch.Core
{
    // Guarded measurement/deletion adapted from krezio/roomy (MIT).
    public static class FileOps
    {
        public struct DeleteResult
        {
            public long Freed;
            public int Files;
            public int Failed;
            public DeleteResult(long freed, int files, int failed) { Freed = freed; Files = files; Failed = failed; }
        }

        private static IEnumerable<string> EnumerateDirectoriesSafe(string root)
        {
            if (SafeFs.TrustedPath(root) == null) yield break;
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] children;
                try { children = Directory.GetDirectories(directory); }
                catch { continue; }
                foreach (string child in children)
                {
                    bool reparse = false;
                    try { reparse = (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0; }
                    catch { reparse = true; }
                    if (reparse) continue;
                    pending.Push(child);
                    yield return child;
                }
            }
        }

        public static IEnumerable<string> SafeFiles(string root)
        {
            if (SafeFs.TrustedPath(root) == null) yield break;
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] files;
                try { files = Directory.GetFiles(directory); }
                catch { continue; }
                foreach (string file in files) yield return file;
                string[] children;
                try { children = Directory.GetDirectories(directory); }
                catch { continue; }
                foreach (string child in children)
                {
                    bool reparse = false;
                    try { reparse = (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0; }
                    catch { reparse = true; }
                    if (!reparse) pending.Push(child);
                }
            }
        }

        public static IEnumerable<string> SafeDirectories(string root)
        {
            return EnumerateDirectoriesSafe(root);
        }

        private static DateTime Cutoff(TimeSpan? minAge) { return minAge.HasValue ? DateTime.UtcNow - minAge.Value : DateTime.MaxValue; }
        public static bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }

        public static void Measure(string path, TimeSpan? minAge, out long bytes, out int files, CancellationToken token)
        {
            bytes = 0; files = 0;
            DateTime cutoff = Cutoff(minAge);
            try
            {
                string root = SafeFs.TrustedPath(path);
                if (root == null) return;
                if (File.Exists(root))
                {
                    FileInfo info = new FileInfo(root);
                    if (info.LastWriteTimeUtc <= cutoff) { bytes = info.Length; files = 1; }
                    return;
                }
                if (!Directory.Exists(root)) return;
                foreach (string file in SafeFiles(root))
                {
                    token.ThrowIfCancellationRequested();
                    FileInfo info = new FileInfo(file);
                    if (info.LastWriteTimeUtc <= cutoff) { bytes += info.Length; files++; }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        public static DeleteResult Delete(string path, TimeSpan? minAge, CancellationToken token)
        {
            DateTime cutoff = Cutoff(minAge);
            Guard(path);
            string root = SafeFs.TrustedPath(path);
            if (root == null) return default(DeleteResult);
            Guard(root);
            if (File.Exists(root))
            {
                FileInfo info = new FileInfo(root);
                if (info.LastWriteTimeUtc > cutoff) return default(DeleteResult);
                long length = info.Length;
                return SafeFs.Delete(root, root) ? new DeleteResult(length, 1, 0) : new DeleteResult(0, 0, 1);
            }
            if (!Directory.Exists(root)) return default(DeleteResult);
            long freed = 0; int deleted = 0, failed = 0;
            List<string> files;
            try { files = new List<string>(SafeFiles(root)); }
            catch (OperationCanceledException) { throw; }
            catch { return default(DeleteResult); }
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                FileInfo info = new FileInfo(file);
                if (info.LastWriteTimeUtc > cutoff) continue;
                if (SafeFs.Delete(file, root)) { freed += info.Length; deleted++; }
                else failed++;
            }
            try
            {
                token.ThrowIfCancellationRequested();
                List<string> directories = new List<string>(SafeDirectories(root));
                directories.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
                foreach (string directory in directories)
                {
                    token.ThrowIfCancellationRequested();
                    SafeFs.Delete(directory, root);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            return new DeleteResult(freed, deleted, failed);
        }

        private static readonly string[] ProtectedRoots = BuildProtectedRoots();
        private static string[] BuildProtectedRoots()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.SystemDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                local, roaming, user, Path.GetDirectoryName(user) ?? "",
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                NativeCleanup.DownloadsPath
            };
        }

        private static string Norm(string path)
        {
            try { return Path.GetFullPath(path).TrimEnd('\\'); }
            catch { return path.TrimEnd('\\'); }
        }

        public static bool RecycleFile(string path, out long bytes)
        {
            bytes = 0;
            Guard(path);
            string root = SafeFs.TrustedPath(path);
            if (root == null || !File.Exists(root)) return false;
            Guard(root);
            FileInfo info = new FileInfo(root);
            long length = info.Length;
            if (NativeCleanup.SendToRecycleBin(root) && !File.Exists(root))
            {
                bytes = length;
                return true;
            }
            return false;
        }

        public static void Guard(string path)
        {
            string normalized = Norm(path);
            if (normalized.Length <= 3) throw new InvalidOperationException("Refusing to clean a drive root: " + path);
            foreach (string protectedRoot in ProtectedRoots)
                if (String.Equals(Norm(protectedRoot), normalized, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing to clean a protected location: " + path);
        }
    }
}
