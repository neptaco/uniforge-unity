#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;

namespace UniForge.Tools.Mutations.InputSimulation
{
    /// <summary>
    /// Windows SendInput API を使用した入力シミュレーター
    /// </summary>
    public class WindowsInputSimulator : IInputSimulator
    {
        public string Name => "Windows (SendInput)";

        public bool IsAvailable => true;

        #region Native Methods

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // Input types
        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        // Keyboard flags
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        // Mouse flags
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        #endregion

        #region Key Mappings

        private static readonly Dictionary<string, ushort> VirtualKeyMap = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // Letters
            { "a", 0x41 }, { "b", 0x42 }, { "c", 0x43 }, { "d", 0x44 },
            { "e", 0x45 }, { "f", 0x46 }, { "g", 0x47 }, { "h", 0x48 },
            { "i", 0x49 }, { "j", 0x4A }, { "k", 0x4B }, { "l", 0x4C },
            { "m", 0x4D }, { "n", 0x4E }, { "o", 0x4F }, { "p", 0x50 },
            { "q", 0x51 }, { "r", 0x52 }, { "s", 0x53 }, { "t", 0x54 },
            { "u", 0x55 }, { "v", 0x56 }, { "w", 0x57 }, { "x", 0x58 },
            { "y", 0x59 }, { "z", 0x5A },

            // Numbers
            { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 },
            { "4", 0x34 }, { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 },
            { "8", 0x38 }, { "9", 0x39 },

            // Function keys
            { "f1", 0x70 }, { "f2", 0x71 }, { "f3", 0x72 }, { "f4", 0x73 },
            { "f5", 0x74 }, { "f6", 0x75 }, { "f7", 0x76 }, { "f8", 0x77 },
            { "f9", 0x78 }, { "f10", 0x79 }, { "f11", 0x7A }, { "f12", 0x7B },

            // Special keys
            { "backspace", 0x08 },
            { "tab", 0x09 },
            { "enter", 0x0D }, { "return", 0x0D },
            { "shift", 0x10 }, { "leftshift", 0x10 },
            { "ctrl", 0x11 }, { "control", 0x11 }, { "leftctrl", 0x11 },
            { "alt", 0x12 }, { "leftalt", 0x12 },
            { "pause", 0x13 },
            { "capslock", 0x14 },
            { "escape", 0x1B }, { "esc", 0x1B },
            { "space", 0x20 }, { "spacebar", 0x20 },
            { "pageup", 0x21 },
            { "pagedown", 0x22 },
            { "end", 0x23 },
            { "home", 0x24 },
            { "left", 0x25 }, { "leftarrow", 0x25 },
            { "up", 0x26 }, { "uparrow", 0x26 },
            { "right", 0x27 }, { "rightarrow", 0x27 },
            { "down", 0x28 }, { "downarrow", 0x28 },
            { "printscreen", 0x2C },
            { "insert", 0x2D },
            { "delete", 0x2E },

            // Modifier keys (right side)
            { "rightshift", 0xA1 },
            { "rightctrl", 0xA3 },
            { "rightalt", 0xA5 },

            // Windows keys
            { "lwin", 0x5B }, { "meta", 0x5B }, { "leftmeta", 0x5B }, { "windows", 0x5B }, { "command", 0x5B },
            { "rwin", 0x5C }, { "rightmeta", 0x5C },

            // Numpad
            { "numpad0", 0x60 }, { "numpad1", 0x61 }, { "numpad2", 0x62 },
            { "numpad3", 0x63 }, { "numpad4", 0x64 }, { "numpad5", 0x65 },
            { "numpad6", 0x66 }, { "numpad7", 0x67 }, { "numpad8", 0x68 },
            { "numpad9", 0x69 },
            { "numpadmultiply", 0x6A }, { "numpadplus", 0x6B },
            { "numpadminus", 0x6D }, { "numpadperiod", 0x6E },
            { "numpaddivide", 0x6F },
            { "numlock", 0x90 },
            { "scrolllock", 0x91 },

            // OEM keys
            { ";", 0xBA }, { "semicolon", 0xBA },
            { "=", 0xBB }, { "equals", 0xBB },
            { ",", 0xBC }, { "comma", 0xBC },
            { "-", 0xBD }, { "minus", 0xBD },
            { ".", 0xBE }, { "period", 0xBE },
            { "/", 0xBF }, { "slash", 0xBF },
            { "`", 0xC0 }, { "backquote", 0xC0 },
            { "[", 0xDB }, { "leftbracket", 0xDB },
            { "\\", 0xDC }, { "backslash", 0xDC },
            { "]", 0xDD }, { "rightbracket", 0xDD },
            { "'", 0xDE }, { "quote", 0xDE },
        };

        // Extended keys that require KEYEVENTF_EXTENDEDKEY flag
        private static readonly HashSet<ushort> ExtendedKeys = new HashSet<ushort>
        {
            0x21, 0x22, 0x23, 0x24, // PageUp, PageDown, End, Home
            0x25, 0x26, 0x27, 0x28, // Arrow keys
            0x2D, 0x2E,             // Insert, Delete
            0x5B, 0x5C,             // Windows keys
            0x6F,                   // Numpad divide
            0x90,                   // Num lock
        };

        #endregion

        public InputSimulationResult KeyDown(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return InputSimulationResult.Fail("Parameter 'key' is required");
            }

            if (!TryGetVirtualKey(key, out var vk))
            {
                return InputSimulationResult.Fail($"Invalid key name: {key}");
            }

            SendKeyInput(vk, false);

            return InputSimulationResult.Ok(
                "key_down",
                $"Key '{key}' pressed down (VK: 0x{vk:X2})",
                $"Simulated key down: {key}",
                Name
            );
        }

        public InputSimulationResult KeyUp(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return InputSimulationResult.Fail("Parameter 'key' is required");
            }

            if (!TryGetVirtualKey(key, out var vk))
            {
                return InputSimulationResult.Fail($"Invalid key name: {key}");
            }

            SendKeyInput(vk, true);

            return InputSimulationResult.Ok(
                "key_up",
                $"Key '{key}' released (VK: 0x{vk:X2})",
                $"Simulated key up: {key}",
                Name
            );
        }

        public InputSimulationResult KeyPress(string key, int durationMs = 100)
        {
            if (string.IsNullOrEmpty(key))
            {
                return InputSimulationResult.Fail("Parameter 'key' is required");
            }

            if (!TryGetVirtualKey(key, out var vk))
            {
                return InputSimulationResult.Fail($"Invalid key name: {key}");
            }

            // Key down
            SendKeyInput(vk, false);

            // Schedule key up
            InputSimulatorUtils.ScheduleAfterMilliseconds(durationMs, () =>
            {
                SendKeyInput(vk, true);
            });

            return InputSimulationResult.Ok(
                "key_press",
                $"Key '{key}' pressed for ~{durationMs}ms (VK: 0x{vk:X2})",
                $"Simulated key press: {key}",
                Name
            );
        }

        public InputSimulationResult MouseDown(int button, float? x = null, float? y = null)
        {
            if (x.HasValue && y.HasValue)
            {
                SetCursorPos((int)x.Value, (int)y.Value);
            }

            var flags = GetMouseDownFlags(button);
            SendMouseInput(0, 0, 0, flags);

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F0}, {y.Value:F0})" : "";

            return InputSimulationResult.Ok(
                "mouse_down",
                $"Mouse {buttonName} button pressed{positionInfo}",
                $"Simulated mouse down: {buttonName} button{positionInfo}",
                Name
            );
        }

        public InputSimulationResult MouseUp(int button, float? x = null, float? y = null)
        {
            if (x.HasValue && y.HasValue)
            {
                SetCursorPos((int)x.Value, (int)y.Value);
            }

            var flags = GetMouseUpFlags(button);
            SendMouseInput(0, 0, 0, flags);

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F0}, {y.Value:F0})" : "";

            return InputSimulationResult.Ok(
                "mouse_up",
                $"Mouse {buttonName} button released{positionInfo}",
                $"Simulated mouse up: {buttonName} button{positionInfo}",
                Name
            );
        }

        public InputSimulationResult MouseClick(int button, float? x = null, float? y = null)
        {
            if (x.HasValue && y.HasValue)
            {
                SetCursorPos((int)x.Value, (int)y.Value);
            }

            // Mouse down
            SendMouseInput(0, 0, 0, GetMouseDownFlags(button));

            // Schedule mouse up
            EditorApplication.delayCall += () =>
            {
                SendMouseInput(0, 0, 0, GetMouseUpFlags(button));
            };

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F0}, {y.Value:F0})" : "";

            return InputSimulationResult.Ok(
                "mouse_click",
                $"Mouse {buttonName} click{positionInfo}",
                $"Simulated mouse click: {buttonName} button{positionInfo}",
                Name
            );
        }

        public InputSimulationResult MouseMove(float x, float y)
        {
            SetCursorPos((int)x, (int)y);

            return InputSimulationResult.Ok(
                "mouse_move",
                $"Mouse moved to ({x:F0}, {y:F0})",
                $"Simulated mouse move to ({x:F0}, {y:F0})",
                Name
            );
        }

        public InputSimulationResult MouseScroll(float delta)
        {
            // WHEEL_DELTA is 120
            int wheelDelta = (int)(delta);
            SendMouseInput(0, 0, (uint)wheelDelta, MOUSEEVENTF_WHEEL);

            return InputSimulationResult.Ok(
                "mouse_scroll",
                $"Mouse scrolled by {delta}",
                $"Simulated mouse scroll: {delta}",
                Name
            );
        }

        public void FocusApplication() => InputSimulatorUtils.FocusGameView();

        #region Static Helpers for Hybrid Use (InputSystemSimulator)

        /// <summary>
        /// SendInput API でマウスボタンイベントを送信（Old Input Manager 対応）。
        /// WarpCursorPosition 後に呼び出すことで、現在のカーソル位置でボタンイベントが発行される。
        /// </summary>
        public static void SendNativeMouseButtonEvent(int button, bool isDown)
        {
            uint flags = (button, isDown) switch
            {
                (0, true) => MOUSEEVENTF_LEFTDOWN,
                (0, false) => MOUSEEVENTF_LEFTUP,
                (1, true) => MOUSEEVENTF_RIGHTDOWN,
                (1, false) => MOUSEEVENTF_RIGHTUP,
                (2, true) => MOUSEEVENTF_MIDDLEDOWN,
                (2, false) => MOUSEEVENTF_MIDDLEUP,
                _ => isDown ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP
            };
            SendInputEvent(0, 0, 0, flags);
        }

        /// <summary>
        /// SendInput API でマウス移動イベントを送信（Old Input Manager 対応）。
        /// SetCursorPos / WarpCursorPosition は WM_MOUSEMOVE を生成しないため、
        /// SendInput(MOUSEEVENTF_MOVE) で明示的に移動イベントを発行する。
        /// </summary>
        public static void SendNativeMouseMoveEvent()
        {
            // MOUSEEVENTF_MOVE with dx=0, dy=0 (relative) generates WM_MOUSEMOVE at current cursor position
            SendInputEvent(0, 0, 0, MOUSEEVENTF_MOVE);
        }

        /// <summary>
        /// Unity Editor (Game View) をフォアグラウンドにフォーカスする
        /// </summary>
        public static void FocusUnityApplication() => InputSimulatorUtils.FocusGameView();

        private static void SendInputEvent(int dx, int dy, uint data, uint flags)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = data,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        #endregion

        #region Helper Methods

        private bool TryGetVirtualKey(string key, out ushort vk)
        {
            // Try direct lookup
            if (VirtualKeyMap.TryGetValue(key, out vk))
            {
                return true;
            }

            // Try single character
            if (key.Length == 1)
            {
                char c = char.ToUpper(key[0]);
                if (c >= 'A' && c <= 'Z')
                {
                    vk = (ushort)c;
                    return true;
                }
                if (c >= '0' && c <= '9')
                {
                    vk = (ushort)c;
                    return true;
                }
            }

            vk = 0;
            return false;
        }

        private void SendKeyInput(ushort vk, bool keyUp)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = 0,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : KEYEVENTF_KEYDOWN,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            // Add extended key flag if needed
            if (ExtendedKeys.Contains(vk))
            {
                input.u.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
            }

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        private void SendMouseInput(int dx, int dy, uint data, uint flags)
            => SendInputEvent(dx, dy, data, flags);

        private uint GetMouseDownFlags(int button)
        {
            return button switch
            {
                0 => MOUSEEVENTF_LEFTDOWN,
                1 => MOUSEEVENTF_RIGHTDOWN,
                2 => MOUSEEVENTF_MIDDLEDOWN,
                _ => MOUSEEVENTF_LEFTDOWN
            };
        }

        private uint GetMouseUpFlags(int button)
        {
            return button switch
            {
                0 => MOUSEEVENTF_LEFTUP,
                1 => MOUSEEVENTF_RIGHTUP,
                2 => MOUSEEVENTF_MIDDLEUP,
                _ => MOUSEEVENTF_LEFTUP
            };
        }

        private static string GetButtonName(int button) => InputSimulatorUtils.GetButtonName(button);

        #endregion
    }
}
#endif
