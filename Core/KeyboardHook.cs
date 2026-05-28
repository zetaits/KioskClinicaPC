using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KioskClinicaPC
{
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int VK_TAB = 0x09;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private const uint LLKHF_ALTDOWN = 0x20;

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        // Permite Win+PrtSc temporalmente para capturas durante demos.
        public static bool AllowWindowsKey { get; set; } = false;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            _hookID = SetHook(_proc);
        }

        public void Stop()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var kbdStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                int vkCode = (int)kbdStruct.vkCode;
                uint flags = kbdStruct.flags;

                bool altDown = (flags & LLKHF_ALTDOWN) != 0;

                // 1. Bloquear Tecla Windows (Izquierda y Derecha)
                if (!AllowWindowsKey && (vkCode == VK_LWIN || vkCode == VK_RWIN))
                {
                    return (IntPtr)1;
                }

                // 2. Bloquear Alt + Tab
                if (altDown && vkCode == VK_TAB)
                {
                    return (IntPtr)1;
                }

                // 3. Bloquear Alt + F4
                if (altDown && vkCode == 0x73) // VK_F4
                {
                    return (IntPtr)1;
                }

                // 4. Bloquear Ctrl + Esc o Alt + Esc
                if (vkCode == 0x1B) // VK_ESCAPE
                {
                    bool ctrlDown = (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Control) != 0;
                    if (altDown || ctrlDown) return (IntPtr)1;
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
