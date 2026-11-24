using System;
using System.Runtime.InteropServices;

namespace PCWatch.Services
{
    public static class WindowActivationService
    {
        public static bool Activate(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return false;
            try
            {
                if (IsIconic(handle)) ShowWindowAsync(handle, 9);
                else ShowWindowAsync(handle, 5);
                BringWindowToTop(handle);
                bool activated = SetForegroundWindow(handle);
                if (!activated)
                {
                    FlashWindow(handle);
                    return false;
                }
                return true;
            }
            catch
            {
                FlashWindow(handle);
                return false;
            }
        }

        private static void FlashWindow(IntPtr handle)
        {
            try
            {
                FlashWindowInfo info = new FlashWindowInfo();
                info.Size = (uint)Marshal.SizeOf(typeof(FlashWindowInfo));
                info.Handle = handle;
                info.Flags = 2;
                info.Count = 3;
                info.Timeout = 0;
                FlashWindowEx(ref info);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr handle, int command);
        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FlashWindowInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashWindowInfo
        {
            public uint Size;
            public IntPtr Handle;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }
    }
}
