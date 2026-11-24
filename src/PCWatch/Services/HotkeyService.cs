using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PCWatch.Services
{
    public sealed class HotkeyService : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int Id = 0x50C7;
        private HwndSource _source;
        private IntPtr _handle;
        private bool _registered;
        public event Action Pressed;
        public string Error { get; private set; }

        public void Register(Window window, string hotkey)
        {
            WindowInteropHelper helper = new WindowInteropHelper(window);
            _handle = helper.Handle;
            _source = HwndSource.FromHwnd(_handle);
            if (_source == null || _handle == IntPtr.Zero) { Error = "Окно ещё не готово."; return; }
            uint modifiers, key;
            Parse(hotkey, out modifiers, out key);
            _registered = RegisterHotKey(_handle, Id, modifiers | 0x4000, key);
            if (!_registered) Error = "Сочетание " + hotkey + " уже занято другой программой (код " + Marshal.GetLastWin32Error() + ").";
            _source.AddHook(Hook);
        }

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && wParam.ToInt32() == Id)
            {
                handled = true;
                Action handler = Pressed;
                if (handler != null) handler();
            }
            return IntPtr.Zero;
        }

        private static void Parse(string hotkey, out uint modifiers, out uint key)
        {
            modifiers = 0x0002 | 0x0001;
            key = 0x7B;
            if (String.IsNullOrWhiteSpace(hotkey)) return;
            string[] parts = hotkey.Split('+');
            modifiers = 0;
            string keyName = parts[parts.Length - 1].Trim();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i].Trim().ToLowerInvariant();
                if (part == "ctrl" || part == "control") modifiers |= 0x0002;
                else if (part == "alt") modifiers |= 0x0001;
                else if (part == "shift") modifiers |= 0x0004;
                else if (part == "win") modifiers |= 0x0008;
            }
            try { key = (uint)KeyInterop.VirtualKeyFromKey((Key)Enum.Parse(typeof(Key), keyName, true)); }
            catch { modifiers = 0x0002 | 0x0001; key = 0x7B; }
        }

        public void Dispose()
        {
            if (_source != null) _source.RemoveHook(Hook);
            if (_registered && _handle != IntPtr.Zero) UnregisterHotKey(_handle, Id);
            _registered = false;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
