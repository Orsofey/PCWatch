using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PCWatch.Core
{
    // Handle-based safety adapted from krezio/roomy (MIT), see THIRD-PARTY-NOTICES.txt.
    internal static class SafeFs
    {
        private const uint DeleteAccess = 0x00010000;
        private const uint ReadAttributes = 0x80;
        private const uint WriteAttributes = 0x100;
        private const uint ShareAll = 0x1 | 0x2 | 0x4;
        private const uint OpenExisting = 3;
        private const uint BackupSemantics = 0x02000000;
        private const uint OpenReparsePoint = 0x00200000;
        private const int FileDispositionInfo = 4;
        private const int FileDispositionInfoEx = 21;
        private const uint DeleteFlag = 0x1;
        private const uint PosixSemantics = 0x2;
        private const uint IgnoreReadOnly = 0x10;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref uint info, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref byte info, uint size);

        private static SafeFileHandle Open(string path, bool forDelete)
        {
            return CreateFileW(@"\\?\" + path, ReadAttributes | (forDelete ? DeleteAccess | WriteAttributes : 0), ShareAll,
                IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        }

        private static string FinalPath(SafeFileHandle handle)
        {
            StringBuilder builder = new StringBuilder(1024);
            uint length = GetFinalPathNameByHandleW(handle, builder, (uint)builder.Capacity, 0);
            if (length == 0) return null;
            if (length > builder.Capacity)
            {
                builder = new StringBuilder((int)length + 1);
                length = GetFinalPathNameByHandleW(handle, builder, (uint)builder.Capacity, 0);
                if (length == 0 || length > builder.Capacity) return null;
            }
            string path = builder.ToString();
            if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + path.Substring(8);
            return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path;
        }

        private static string LongPath(string path)
        {
            string full = Path.GetFullPath(path);
            StringBuilder builder = new StringBuilder(1024);
            uint length = GetLongPathNameW(@"\\?\" + full, builder, (uint)builder.Capacity);
            if (length == 0 || length > builder.Capacity) return null;
            string result = builder.ToString();
            return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result.Substring(4) : result;
        }

        private static string Trim(string path) { return path.Length > 3 ? path.TrimEnd('\\') : path; }

        public static string TrustedPath(string path)
        {
            try
            {
                string expected = LongPath(path);
                if (expected == null) return null;
                using (SafeFileHandle handle = Open(expected, false))
                {
                    if (handle.IsInvalid) return null;
                    string real = FinalPath(handle);
                    if (real == null || !String.Equals(Trim(real), Trim(expected), StringComparison.OrdinalIgnoreCase)) return null;
                    return (File.GetAttributes(expected) & FileAttributes.ReparsePoint) != 0 ? null : Trim(real);
                }
            }
            catch { return null; }
        }

        public static bool IsInside(string real, string root)
        {
            return String.Equals(real, root, StringComparison.OrdinalIgnoreCase)
                || real.StartsWith(root.EndsWith("\\") ? root : root + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Delete(string path, string trustedRoot)
        {
            try
            {
                using (SafeFileHandle handle = Open(Path.GetFullPath(path), true))
                {
                    if (handle.IsInvalid) return false;
                    string real = FinalPath(handle);
                    if (real == null || !IsInside(Trim(real), trustedRoot)) return false;
                    uint flags = DeleteFlag | PosixSemantics | IgnoreReadOnly;
                    if (SetFileInformationByHandle(handle, FileDispositionInfoEx, ref flags, sizeof(uint))) return true;
                    byte delete = 1;
                    return SetFileInformationByHandle(handle, FileDispositionInfo, ref delete, 1);
                }
            }
            catch { return false; }
        }
    }
}
