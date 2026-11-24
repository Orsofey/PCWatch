using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PCWatch.Core
{
    public static class NativeCleanup
    {
        private const uint FoDelete = 3;
        private const ushort FofSilent = 0x0004;
        private const ushort FofAllowUndo = 0x0040;
        private const ushort FofNoErrorUi = 0x0400;
        private const ushort FofWantNukeWarning = 0x4000;
        private const uint SherbNoConfirmation = 0x1;
        private const uint SherbNoProgressUi = 0x2;
        private const uint SherbNoSound = 0x4;

        public static void QueryRecycleBin(out long bytes, out long items)
        {
            bytes = 0; items = 0;
            try
            {
                ShQueryRbInfo info = new ShQueryRbInfo();
                info.cbSize = (uint)Marshal.SizeOf(typeof(ShQueryRbInfo));
                if (SHQueryRecycleBin(null, ref info) == 0)
                {
                    bytes = info.i64Size;
                    items = info.i64NumItems;
                }
            }
            catch { }
        }

        public static bool EmptyRecycleBin()
        {
            try { return SHEmptyRecycleBin(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound) == 0; }
            catch { return false; }
        }

        public static bool SendToRecycleBin(string path)
        {
            try
            {
                ShFileOpStruct operation = new ShFileOpStruct();
                operation.wFunc = FoDelete;
                operation.pFrom = path + "\0";
                operation.fFlags = FofAllowUndo | FofNoErrorUi | FofSilent | FofWantNukeWarning;
                return SHFileOperation(ref operation) == 0 && !operation.fAnyOperationsAborted;
            }
            catch { return false; }
        }

        public static string DownloadsPath
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
                    {
                        string value = key == null ? null : Convert.ToString(key.GetValue("Downloads"));
                        if (!String.IsNullOrWhiteSpace(value)) return Environment.ExpandEnvironmentVariables(value);
                        value = key == null ? null : Convert.ToString(key.GetValue("{374DE290-123F-4565-9164-39C4925E467B}"));
                        if (!String.IsNullOrWhiteSpace(value)) return Environment.ExpandEnvironmentVariables(value);
                    }
                }
                catch { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        public static string HibernationPath
        {
            get { return Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "hiberfil.sys"); }
        }

        public static long HibernationSize()
        {
            try
            {
                FileInfo file = new FileInfo(HibernationPath);
                return file.Exists ? file.Length : 0;
            }
            catch { return 0; }
        }

        public static bool SetHibernation(bool enabled)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("powercfg.exe", enabled ? "/hibernate on" : "/hibernate off");
                info.UseShellExecute = true;
                info.Verb = "runas";
                info.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(info))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShQueryRbInfo
        {
            public uint cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShFileOpStruct
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string rootPath, ref ShQueryRbInfo info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref ShFileOpStruct operation);
    }
}
