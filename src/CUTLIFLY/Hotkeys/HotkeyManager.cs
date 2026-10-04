using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Cutlifly.Core;

namespace Cutlifly.Hotkeys
{
    /// <summary>
    /// Atajos globales mediante un hook de teclado de bajo nivel (WH_KEYBOARD_LL).
    /// A diferencia de RegisterHotKey, permite usar combinaciones reservadas por Windows
    /// como Win+Shift+S o Win+Shift+R, suprimiendo la acción original del sistema.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        private const int LLKHF_INJECTED = 0x10;
        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        private const ushort VK_DUMMY = 0xE8; // tecla sin asignar: evita que se abra el menú Inicio al soltar Win

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public IntPtr extra; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public int type; public KEYBDINPUT ki; public long pad; }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT { public ushort wVk, wScan; public int dwFlags, time; public IntPtr dwExtraInfo; }

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, LowLevelProc proc, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly LowLevelProc _proc;
        private readonly Dispatcher _dispatcher;
        private readonly Dictionary<Hotkey, Action> _bindings = new Dictionary<Hotkey, Action>();
        private readonly HashSet<int> _swallowUp = new HashSet<int>();
        private IntPtr _hook;
        private Action<Hotkey> _captureNext;

        public HotkeyManager(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _proc = HookProc; // mantener referencia viva
        }

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            using var module = Process.GetCurrentProcess().MainModule;
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
            if (_hook == IntPtr.Zero)
                Logger.Error("Hotkeys", $"No se pudo instalar el hook de teclado (error {Marshal.GetLastWin32Error()})");
        }

        public void Clear() => _bindings.Clear();

        public void Bind(Hotkey hk, Action action)
        {
            if (hk.IsValid) _bindings[hk] = action;
        }

        /// <summary>La próxima combinación pulsada se entrega a <paramref name="callback"/> (para el editor de atajos).</summary>
        public void CaptureNext(Action<Hotkey> callback) => _captureNext = callback;
        public void CancelCapture() => _captureNext = null;

        private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        private static HotkeyModifiers CurrentModifiers()
        {
            var m = HotkeyModifiers.None;
            if (Down(VK_CONTROL)) m |= HotkeyModifiers.Ctrl;
            if (Down(VK_SHIFT)) m |= HotkeyModifiers.Shift;
            if (Down(VK_MENU)) m |= HotkeyModifiers.Alt;
            if (Down(VK_LWIN) || Down(VK_RWIN)) m |= HotkeyModifiers.Win;
            return m;
        }

        private static bool IsModifierKey(int vk) =>
            vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN or VK_RWIN or (>= 0xA0 and <= 0xA5);

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                bool injected = (data.flags & LLKHF_INJECTED) != 0;

                if (!injected && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && !IsModifierKey(data.vkCode))
                {
                    var hk = new Hotkey(CurrentModifiers(), data.vkCode);
                    if (_captureNext != null && hk.Modifiers != HotkeyModifiers.None)
                    {
                        var cb = _captureNext;
                        _captureNext = null;
                        Swallow(hk);
                        _dispatcher.BeginInvoke(new Action(() => cb(hk)));
                        return (IntPtr)1;
                    }
                    if (_captureNext == null && _bindings.TryGetValue(hk, out var action))
                    {
                        Swallow(hk);
                        _dispatcher.BeginInvoke(action);
                        return (IntPtr)1;
                    }
                }
                else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _swallowUp.Remove(data.vkCode))
                {
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void Swallow(Hotkey hk)
        {
            _swallowUp.Add(hk.Key);
            if (hk.Modifiers.HasFlag(HotkeyModifiers.Win) || hk.Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                // Pulsación ficticia para que soltar Win/Alt no abra el menú Inicio ni la barra de menús.
                var inputs = new[]
                {
                    new INPUT { type = 1, ki = new KEYBDINPUT { wVk = VK_DUMMY } },
                    new INPUT { type = 1, ki = new KEYBDINPUT { wVk = VK_DUMMY, dwFlags = 2 } }
                };
                SendInput(2, inputs, Marshal.SizeOf<INPUT>());
            }
        }

        /// <summary>
        /// Comprueba si otra aplicación tiene registrada la combinación (solo es posible sin la tecla Win;
        /// Windows no permite consultar sus atajos internos con Win).
        /// </summary>
        public static bool IsTakenByAnotherApp(Hotkey hk)
        {
            if (hk.Modifiers.HasFlag(HotkeyModifiers.Win)) return false;
            uint mods = 0x4000; // MOD_NOREPEAT
            if (hk.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= 1;
            if (hk.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) mods |= 2;
            if (hk.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= 4;
            const int id = 0xC17F;
            if (!RegisterHotKey(IntPtr.Zero, id, mods, (uint)hk.Key)) return true;
            UnregisterHotKey(IntPtr.Zero, id);
            return false;
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
