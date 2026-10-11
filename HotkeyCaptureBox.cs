using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenClip
{
    internal sealed class HotkeyCaptureBox : TextBox
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int VkSnapshot = 0x2C;
        private readonly HookProc hookProc;
        private IntPtr hook;

        public uint Modifiers { get; private set; }
        public int KeyCode { get; private set; }

        public HotkeyCaptureBox(AppSettings initial)
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            ImeMode = ImeMode.Disable;
            hookProc = OnKeyboardHook;
            SetHotkey(initial.Modifiers, initial.KeyCode);
        }

        public void SetHotkey(uint modifiers, int keyCode)
        {
            Modifiers = modifiers;
            KeyCode = keyCode;
            Text = AppSettings.HotkeyTextFor(modifiers, keyCode);
            SelectionStart = Text.Length;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (hook == IntPtr.Zero)
                hook = SetWindowsHookEx(WhKeyboardLl, hookProc, GetModuleHandle(null), 0);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            RemoveHook();
            base.OnLostFocus(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (RecordKey(e.KeyCode, ModifierFlags(e.Modifiers)))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            // PrintScreen can arrive as only a key-up message on some keyboards.
            if (e.KeyCode == Keys.PrintScreen)
            {
                SetHotkey(ModifierFlags(e.Modifiers), (int)Keys.PrintScreen);
                e.Handled = true;
            }
            base.OnKeyUp(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key != Keys.Escape && key != Keys.Tab &&
                RecordKey(key, ModifierFlags(keyData & Keys.Modifiers))) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool RecordKey(Keys key, uint modifiers)
        {
            if (key == Keys.None || key == Keys.Escape || key == Keys.Tab ||
                key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin) return false;
            SetHotkey(modifiers, (int)key);
            return true;
        }

        private static uint ModifierFlags(Keys modifiers)
        {
            uint flags = 0;
            if ((modifiers & Keys.Control) != 0) flags |= 2;
            if ((modifiers & Keys.Alt) != 0) flags |= 1;
            if ((modifiers & Keys.Shift) != 0) flags |= 4;
            if ((GetAsyncKeyState(0x5B) & 0x8000) != 0 ||
                (GetAsyncKeyState(0x5C) & 0x8000) != 0) flags |= 8;
            return flags;
        }

        private IntPtr OnKeyboardHook(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && Focused)
            {
                KeyboardData keyboard = (KeyboardData)Marshal.PtrToStructure(data, typeof(KeyboardData));
                if (keyboard.VirtualKey == VkSnapshot)
                {
                    int kind = message.ToInt32();
                    if (kind == WmKeyDown || kind == WmSysKeyDown)
                        SetHotkey(ModifierFlags(Control.ModifierKeys), (int)Keys.PrintScreen);
                    if (kind == WmKeyDown || kind == WmSysKeyDown ||
                        kind == WmKeyUp || kind == WmSysKeyUp) return (IntPtr)1;
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }

        private void RemoveHook()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) RemoveHook();
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardData
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string module);
    }
}
