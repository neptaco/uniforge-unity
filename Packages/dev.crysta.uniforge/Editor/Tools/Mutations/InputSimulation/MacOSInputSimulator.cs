#if UNITY_EDITOR_OSX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;

namespace UniForge.Tools.Mutations.InputSimulation
{
    /// <summary>
    /// macOS CGEvent API を使用した入力シミュレーター
    /// </summary>
    public class MacOSInputSimulator : IInputSimulator
    {
        public string Name => "macOS (CGEvent)";

        public bool IsAvailable => true;

        #region Objective-C Runtime

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
        private static extern IntPtr objc_getClass(string className);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
        private static extern IntPtr sel_registerName(string selectorName);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_ulong(IntPtr receiver, IntPtr selector, ulong arg1);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern double objc_msgSend_double(IntPtr receiver, IntPtr selector);

        // Returns CGPoint (NSPoint) - needs special stret handling on x86_64 but on ARM64 it's returned in registers
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern CGPoint objc_msgSend_CGPoint(IntPtr receiver, IntPtr selector);

        // [NSEvent mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:]
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_mouseEvent(
            IntPtr receiver, IntPtr selector,
            long type,                // NSEventType
            CGPoint location,         // NSPoint (location in window)
            ulong modifierFlags,      // NSEventModifierFlags
            double timestamp,         // NSTimeInterval
            long windowNumber,        // NSInteger
            IntPtr context,           // NSGraphicsContext (nil)
            long eventNumber,         // NSInteger
            long clickCount,          // NSInteger
            float pressure            // float
        );

        // NSApplicationActivationOptions
        private const ulong NSApplicationActivateIgnoringOtherApps = 1 << 1;

        // NSEventType
        private const long NSEventTypeLeftMouseDown = 1;
        private const long NSEventTypeLeftMouseUp = 2;
        private const long NSEventTypeRightMouseDown = 3;
        private const long NSEventTypeRightMouseUp = 4;
        private const long NSEventTypeMouseMoved = 5;
        private const long NSEventTypeLeftMouseDragged = 6;
        private const long NSEventTypeRightMouseDragged = 7;
        private const long NSEventTypeOtherMouseDown = 25;
        private const long NSEventTypeOtherMouseUp = 26;

        #endregion

        #region Native Methods

        // CGEventSourceRef CGEventSourceCreate(CGEventSourceStateID stateID)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventSourceCreate(int stateID);

        // CGEventRef CGEventCreateKeyboardEvent(CGEventSourceRef source, CGKeyCode virtualKey, bool keyDown)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

        // CGEventRef CGEventCreateMouseEvent(CGEventSourceRef source, CGEventType mouseType, CGPoint mouseCursorPosition, CGMouseButton mouseButton)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, int mouseType, CGPoint mouseCursorPosition, int mouseButton);

        // CGEventRef CGEventCreateScrollWheelEvent(CGEventSourceRef source, CGScrollEventUnit units, uint32_t wheelCount, int32_t wheel1, ...)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventCreateScrollWheelEvent(IntPtr source, int units, uint wheelCount, int wheel1);

        // void CGEventPost(CGEventTapLocation tap, CGEventRef event)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventPost(int tap, IntPtr evt);

        // CGEventRef CGEventCreate(CGEventSourceRef source) - for reading cursor position
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern IntPtr CGEventCreate(IntPtr source);

        // CGPoint CGEventGetLocation(CGEventRef event)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern CGPoint CGEventGetLocation(IntPtr evt);

        // void CGAssociateMouseAndMouseCursorPosition(boolean_t connected)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);

        // void CGEventSourceSetLocalEventsSuppressionInterval(CGEventSourceRef source, CFTimeInterval seconds)
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        private static extern void CGEventSourceSetLocalEventsSuppressionInterval(IntPtr source, double seconds);

        // void CFRelease(CFTypeRef cf)
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);

        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint
        {
            public double x;
            public double y;

            public CGPoint(double x, double y)
            {
                this.x = x;
                this.y = y;
            }
        }

        // Constants
        private const int kCGEventSourceStateHIDSystemState = 1;
        private const int kCGHIDEventTap = 0;

        // Mouse event types
        private const int kCGEventLeftMouseDown = 1;
        private const int kCGEventLeftMouseUp = 2;
        private const int kCGEventRightMouseDown = 3;
        private const int kCGEventRightMouseUp = 4;
        private const int kCGEventMouseMoved = 5;
        private const int kCGEventLeftMouseDragged = 6;
        private const int kCGEventRightMouseDragged = 7;
        private const int kCGEventOtherMouseDown = 25;
        private const int kCGEventOtherMouseUp = 26;
        private const int kCGEventScrollWheel = 22;

        // Mouse buttons
        private const int kCGMouseButtonLeft = 0;
        private const int kCGMouseButtonRight = 1;
        private const int kCGMouseButtonCenter = 2;

        // Scroll event units
        private const int kCGScrollEventUnitLine = 1;

        #endregion

        #region Key Mappings

        private static readonly Dictionary<string, ushort> KeyCodeMap = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            // Letters
            { "a", 0x00 }, { "s", 0x01 }, { "d", 0x02 }, { "f", 0x03 },
            { "h", 0x04 }, { "g", 0x05 }, { "z", 0x06 }, { "x", 0x07 },
            { "c", 0x08 }, { "v", 0x09 }, { "b", 0x0B }, { "q", 0x0C },
            { "w", 0x0D }, { "e", 0x0E }, { "r", 0x0F }, { "y", 0x10 },
            { "t", 0x11 }, { "1", 0x12 }, { "2", 0x13 }, { "3", 0x14 },
            { "4", 0x15 }, { "6", 0x16 }, { "5", 0x17 }, { "=", 0x18 },
            { "9", 0x19 }, { "7", 0x1A }, { "-", 0x1B }, { "8", 0x1C },
            { "0", 0x1D }, { "]", 0x1E }, { "o", 0x1F }, { "u", 0x20 },
            { "[", 0x21 }, { "i", 0x22 }, { "p", 0x23 }, { "l", 0x25 },
            { "j", 0x26 }, { "'", 0x27 }, { "k", 0x28 }, { ";", 0x29 },
            { "\\", 0x2A }, { ",", 0x2B }, { "/", 0x2C }, { "n", 0x2D },
            { "m", 0x2E }, { ".", 0x2F }, { "`", 0x32 },

            // Special keys
            { "return", 0x24 }, { "enter", 0x24 },
            { "tab", 0x30 },
            { "space", 0x31 }, { "spacebar", 0x31 },
            { "delete", 0x33 }, { "backspace", 0x33 },
            { "escape", 0x35 }, { "esc", 0x35 },
            { "command", 0x37 }, { "meta", 0x37 }, { "leftmeta", 0x37 },
            { "shift", 0x38 }, { "leftshift", 0x38 },
            { "capslock", 0x39 },
            { "option", 0x3A }, { "alt", 0x3A }, { "leftalt", 0x3A },
            { "control", 0x3B }, { "ctrl", 0x3B }, { "leftctrl", 0x3B },
            { "rightshift", 0x3C },
            { "rightoption", 0x3D }, { "rightalt", 0x3D },
            { "rightcontrol", 0x3E }, { "rightctrl", 0x3E },
            { "fn", 0x3F },

            // Function keys
            { "f1", 0x7A }, { "f2", 0x78 }, { "f3", 0x63 }, { "f4", 0x76 },
            { "f5", 0x60 }, { "f6", 0x61 }, { "f7", 0x62 }, { "f8", 0x64 },
            { "f9", 0x65 }, { "f10", 0x6D }, { "f11", 0x67 }, { "f12", 0x6F },

            // Arrow keys
            { "left", 0x7B }, { "leftarrow", 0x7B },
            { "right", 0x7C }, { "rightarrow", 0x7C },
            { "down", 0x7D }, { "downarrow", 0x7D },
            { "up", 0x7E }, { "uparrow", 0x7E },

            // Navigation keys
            { "home", 0x73 },
            { "end", 0x77 },
            { "pageup", 0x74 },
            { "pagedown", 0x79 },
            { "forwarddelete", 0x75 },

            // Numpad
            { "numpad0", 0x52 }, { "numpad1", 0x53 }, { "numpad2", 0x54 },
            { "numpad3", 0x55 }, { "numpad4", 0x56 }, { "numpad5", 0x57 },
            { "numpad6", 0x58 }, { "numpad7", 0x59 }, { "numpad8", 0x5B },
            { "numpad9", 0x5C },
            { "numpadmultiply", 0x43 }, { "numpadplus", 0x45 },
            { "numpadminus", 0x4E }, { "numpadperiod", 0x41 },
            { "numpaddivide", 0x4B }, { "numpadenter", 0x4C },
            { "numpadequals", 0x51 }, { "numpadclear", 0x47 },
        };

        #endregion

        public InputSimulationResult KeyDown(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return InputSimulationResult.Fail("Parameter 'key' is required");
            }

            if (!TryGetKeyCode(key, out var keyCode))
            {
                return InputSimulationResult.Fail($"Invalid key name: {key}");
            }

            PostKeyEvent(keyCode, true);

            return InputSimulationResult.Ok(
                "key_down",
                $"Key '{key}' pressed down (keycode: 0x{keyCode:X2})",
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

            if (!TryGetKeyCode(key, out var keyCode))
            {
                return InputSimulationResult.Fail($"Invalid key name: {key}");
            }

            PostKeyEvent(keyCode, false);

            return InputSimulationResult.Ok(
                "key_up",
                $"Key '{key}' released (keycode: 0x{keyCode:X2})",
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

            if (!TryGetKeyCode(key, out var keyCode))
            {
                return InputSimulationResult.Fail($"Invalid key name: {key}");
            }

            // Key down
            PostKeyEvent(keyCode, true);

            // Schedule key up
            EditorApplication.delayCall += () =>
            {
                PostKeyEvent(keyCode, false);
            };

            return InputSimulationResult.Ok(
                "key_press",
                $"Key '{key}' pressed for ~{durationMs}ms (keycode: 0x{keyCode:X2})",
                $"Simulated key press: {key}",
                Name
            );
        }

        public InputSimulationResult MouseDown(int button, float? x = null, float? y = null)
        {
            var position = GetMousePosition(x, y);
            var eventType = GetMouseDownEventType(button);
            var cgButton = GetCGMouseButton(button);

            PostMouseEvent(eventType, position, cgButton);

            var buttonName = GetButtonName(button);
            var positionInfo = $" at ({position.x:F0}, {position.y:F0})";

            return InputSimulationResult.Ok(
                "mouse_down",
                $"Mouse {buttonName} button pressed{positionInfo}",
                $"Simulated mouse down: {buttonName} button{positionInfo}",
                Name
            );
        }

        public InputSimulationResult MouseUp(int button, float? x = null, float? y = null)
        {
            var position = GetMousePosition(x, y);
            var eventType = GetMouseUpEventType(button);
            var cgButton = GetCGMouseButton(button);

            PostMouseEvent(eventType, position, cgButton);

            var buttonName = GetButtonName(button);
            var positionInfo = $" at ({position.x:F0}, {position.y:F0})";

            return InputSimulationResult.Ok(
                "mouse_up",
                $"Mouse {buttonName} button released{positionInfo}",
                $"Simulated mouse up: {buttonName} button{positionInfo}",
                Name
            );
        }

        public InputSimulationResult MouseClick(int button, float? x = null, float? y = null)
        {
            var position = GetMousePosition(x, y);
            var cgButton = GetCGMouseButton(button);

            // Mouse down
            PostMouseEvent(GetMouseDownEventType(button), position, cgButton);

            // Schedule mouse up
            EditorApplication.delayCall += () =>
            {
                PostMouseEvent(GetMouseUpEventType(button), position, cgButton);
            };

            var buttonName = GetButtonName(button);
            var positionInfo = $" at ({position.x:F0}, {position.y:F0})";

            return InputSimulationResult.Ok(
                "mouse_click",
                $"Mouse {buttonName} click{positionInfo}",
                $"Simulated mouse click: {buttonName} button{positionInfo}",
                Name
            );
        }

        public InputSimulationResult MouseMove(float x, float y)
        {
            var position = new CGPoint(x, y);
            PostMouseEvent(kCGEventMouseMoved, position, kCGMouseButtonLeft);

            return InputSimulationResult.Ok(
                "mouse_move",
                $"Mouse moved to ({x:F0}, {y:F0})",
                $"Simulated mouse move to ({x:F0}, {y:F0})",
                Name
            );
        }

        public InputSimulationResult MouseScroll(float delta)
        {
            var source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);
            if (source == IntPtr.Zero)
            {
                return InputSimulationResult.Fail("Failed to create event source");
            }

            try
            {
                // Convert delta to scroll units (positive = up, negative = down)
                int scrollAmount = (int)(delta / 10f);
                if (scrollAmount == 0) scrollAmount = delta > 0 ? 1 : -1;

                var evt = CGEventCreateScrollWheelEvent(source, kCGScrollEventUnitLine, 1, scrollAmount);
                if (evt == IntPtr.Zero)
                {
                    return InputSimulationResult.Fail("Failed to create scroll event");
                }

                try
                {
                    CGEventPost(kCGHIDEventTap, evt);
                }
                finally
                {
                    CFRelease(evt);
                }
            }
            finally
            {
                CFRelease(source);
            }

            return InputSimulationResult.Ok(
                "mouse_scroll",
                $"Mouse scrolled by {delta}",
                $"Simulated mouse scroll: {delta}",
                Name
            );
        }

        public void FocusApplication()
        {
            try
            {
                // 1. Unity アプリをフォアグラウンドに
                ActivateCurrentApplication();

                // 2. Game View にフォーカス
                FocusGameViewWindow();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[MacOSInputSimulator] Failed to focus Unity: {ex.Message}");
            }
        }

        #region Public Helpers for Hybrid Use

        /// <summary>
        /// CGWarpMouseCursorPosition 後のイベント抑制を解除する。
        /// Apple ドキュメント: CGWarpMouseCursorPosition は 0.25 秒間マウスイベントを抑制する。
        /// CGAssociateMouseAndMouseCursorPosition(true) で再度関連付けを有効にし、抑制を解除する。
        /// </summary>
        public static void ReenableMouseEventSuppression()
        {
            CGAssociateMouseAndMouseCursorPosition(1);
        }

        /// <summary>
        /// 現在の OS カーソル位置を取得（グローバル desktop 座標、左上原点、ポイント）
        /// </summary>
        public static UnityEngine.Vector2 GetCurrentCursorPosition()
        {
            var evt = CGEventCreate(IntPtr.Zero);
            if (evt == IntPtr.Zero) return UnityEngine.Vector2.zero;
            try
            {
                var pt = CGEventGetLocation(evt);
                return new UnityEngine.Vector2((float)pt.x, (float)pt.y);
            }
            finally
            {
                CFRelease(evt);
            }
        }

        /// <summary>
        /// 指定した OS 座標で CGEvent マウスボタンイベントを送信
        /// </summary>
        public void PostMouseButtonEvent(int button, bool isDown, float osX, float osY)
        {
            var cgPoint = new CGPoint(osX, osY);
            var eventType = isDown ? GetMouseDownEventType(button) : GetMouseUpEventType(button);
            var cgButton = GetCGMouseButton(button);
            PostMouseEvent(eventType, cgPoint, cgButton);
        }

        /// <summary>
        /// 現在のカーソル位置で CGEvent マウスボタンイベントを送信
        /// </summary>
        public void PostMouseButtonAtCurrentPosition(int button, bool isDown)
        {
            var cursorPos = GetCurrentCursorPosition();
            var cgPoint = new CGPoint(cursorPos.x, cursorPos.y);
            var eventType = isDown ? GetMouseDownEventType(button) : GetMouseUpEventType(button);
            var cgButton = GetCGMouseButton(button);
            PostMouseEvent(eventType, cgPoint, cgButton);
        }

        /// <summary>
        /// 指定した OS 座標で CGEvent マウスドラッグイベントを送信
        /// </summary>
        public void PostMouseDragEvent(int button, float osX, float osY)
        {
            var cgPoint = new CGPoint(osX, osY);
            int eventType = button == 1 ? kCGEventRightMouseDragged : kCGEventLeftMouseDragged;
            var cgButton = GetCGMouseButton(button);
            PostMouseEvent(eventType, cgPoint, cgButton);
        }

        /// <summary>
        /// 指定した OS 座標で CGEvent マウス移動イベントを送信
        /// </summary>
        public void PostMouseMoveEvent(float osX, float osY)
        {
            var cgPoint = new CGPoint(osX, osY);
            PostMouseEvent(kCGEventMouseMoved, cgPoint, kCGMouseButtonLeft);
        }

        /// <summary>
        /// NSEvent を生成して NSApplication に直接送信する。
        /// CGEvent と異なり、CGWarpMouseCursorPosition の抑制の影響を受けない。
        /// 座標は Game View の NSWindow のウィンドウ座標 (左下原点, ポイント)。
        /// </summary>
        public void SendNSMouseEvent(int button, bool isDown, bool isDrag = false)
        {
            try
            {
                // NSApplication.sharedApplication
                var nsAppClass = objc_getClass("NSApplication");
                var sharedAppSel = sel_registerName("sharedApplication");
                var app = objc_msgSend(nsAppClass, sharedAppSel);
                if (app == IntPtr.Zero) return;

                // keyWindow
                var keyWindowSel = sel_registerName("keyWindow");
                var keyWindow = objc_msgSend(app, keyWindowSel);
                if (keyWindow == IntPtr.Zero)
                {
                    UnityEngine.Debug.LogWarning("[MacOS] NSEvent: no key window");
                    return;
                }

                // windowNumber
                var windowNumberSel = sel_registerName("windowNumber");
                var windowNum = objc_msgSend_long(keyWindow, windowNumberSel);

                // mouseLocationOutsideOfEventStream (in window coordinates, bottom-left origin)
                var mouseLocSel = sel_registerName("mouseLocationOutsideOfEventStream");
                var mouseLoc = objc_msgSend_CGPoint(keyWindow, mouseLocSel);

                // NSEventType
                long eventType;
                if (isDrag)
                {
                    eventType = button == 1 ? NSEventTypeRightMouseDragged : NSEventTypeLeftMouseDragged;
                }
                else
                {
                    eventType = button switch
                    {
                        0 => isDown ? NSEventTypeLeftMouseDown : NSEventTypeLeftMouseUp,
                        1 => isDown ? NSEventTypeRightMouseDown : NSEventTypeRightMouseUp,
                        _ => isDown ? NSEventTypeOtherMouseDown : NSEventTypeOtherMouseUp
                    };
                }

                // NSProcessInfo.processInfo.systemUptime
                var nsProcessInfoClass = objc_getClass("NSProcessInfo");
                var processInfoSel = sel_registerName("processInfo");
                var processInfo = objc_msgSend(nsProcessInfoClass, processInfoSel);
                var systemUptimeSel = sel_registerName("systemUptime");
                var timestamp = objc_msgSend_double(processInfo, systemUptimeSel);

                // [NSEvent mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:]
                var nsEventClass = objc_getClass("NSEvent");
                var mouseEventSel = sel_registerName("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:");
                var nsEvent = objc_msgSend_mouseEvent(
                    nsEventClass, mouseEventSel,
                    eventType,
                    mouseLoc,        // current mouse location in window coords
                    0,               // no modifier flags
                    timestamp,
                    windowNum,
                    IntPtr.Zero,     // no graphics context
                    0,               // event number
                    isDown ? 1 : 0,  // click count
                    isDown ? 1.0f : 0.0f // pressure
                );

                if (nsEvent == IntPtr.Zero)
                {
                    UnityEngine.Debug.LogWarning("[MacOS] NSEvent: failed to create event");
                    return;
                }

                // [NSApplication sendEvent:]
                var sendEventSel = sel_registerName("sendEvent:");
                objc_msgSend_void_IntPtr(app, sendEventSel, nsEvent);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[MacOS] NSEvent failed: {ex.Message}");
            }
        }

        /// <summary>
        /// NSEvent マウス移動を NSApplication に直接送信する。
        /// </summary>
        public void SendNSMouseMoveEvent()
        {
            try
            {
                var nsAppClass = objc_getClass("NSApplication");
                var sharedAppSel = sel_registerName("sharedApplication");
                var app = objc_msgSend(nsAppClass, sharedAppSel);
                if (app == IntPtr.Zero) return;

                var keyWindowSel = sel_registerName("keyWindow");
                var keyWindow = objc_msgSend(app, keyWindowSel);
                if (keyWindow == IntPtr.Zero) return;

                var windowNumberSel = sel_registerName("windowNumber");
                var windowNum = objc_msgSend_long(keyWindow, windowNumberSel);

                var mouseLocSel = sel_registerName("mouseLocationOutsideOfEventStream");
                var mouseLoc = objc_msgSend_CGPoint(keyWindow, mouseLocSel);

                var nsProcessInfoClass = objc_getClass("NSProcessInfo");
                var processInfoSel = sel_registerName("processInfo");
                var processInfo = objc_msgSend(nsProcessInfoClass, processInfoSel);
                var systemUptimeSel = sel_registerName("systemUptime");
                var timestamp = objc_msgSend_double(processInfo, systemUptimeSel);

                var nsEventClass = objc_getClass("NSEvent");
                var mouseEventSel = sel_registerName("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:");
                var nsEvent = objc_msgSend_mouseEvent(
                    nsEventClass, mouseEventSel,
                    NSEventTypeMouseMoved,
                    mouseLoc,
                    0,
                    timestamp,
                    windowNum,
                    IntPtr.Zero,
                    0,
                    0,
                    0.0f
                );

                if (nsEvent == IntPtr.Zero) return;

                var sendEventSel = sel_registerName("sendEvent:");
                objc_msgSend_void_IntPtr(app, sendEventSel, nsEvent);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[MacOS] NSEvent move failed: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 現在のアプリケーション（Unity Editor）をアクティブにする
        /// </summary>
        private void ActivateCurrentApplication()
        {
            var nsRunningApplicationClass = objc_getClass("NSRunningApplication");
            if (nsRunningApplicationClass == IntPtr.Zero) return;

            var currentAppSel = sel_registerName("currentApplication");
            var currentApp = objc_msgSend(nsRunningApplicationClass, currentAppSel);
            if (currentApp == IntPtr.Zero) return;

            var activateSel = sel_registerName("activateWithOptions:");
            objc_msgSend_ulong(currentApp, activateSel, NSApplicationActivateIgnoringOtherApps);
        }

        private static void FocusGameViewWindow() => InputSimulatorUtils.FocusGameView();

        private bool TryGetKeyCode(string key, out ushort keyCode)
        {
            // Try direct lookup
            if (KeyCodeMap.TryGetValue(key, out keyCode))
            {
                return true;
            }

            // Try single character
            if (key.Length == 1 && KeyCodeMap.TryGetValue(key.ToLowerInvariant(), out keyCode))
            {
                return true;
            }

            keyCode = 0;
            return false;
        }

        private void PostKeyEvent(ushort keyCode, bool keyDown)
        {
            var source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);
            if (source == IntPtr.Zero) return;

            try
            {
                var evt = CGEventCreateKeyboardEvent(source, keyCode, keyDown);
                if (evt == IntPtr.Zero) return;

                try
                {
                    CGEventPost(kCGHIDEventTap, evt);
                }
                finally
                {
                    CFRelease(evt);
                }
            }
            finally
            {
                CFRelease(source);
            }
        }

        private void PostMouseEvent(int eventType, CGPoint position, int button)
        {
            var source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);
            if (source == IntPtr.Zero) return;

            // CGWarpMouseCursorPosition 後のイベント抑制を無効化
            CGEventSourceSetLocalEventsSuppressionInterval(source, 0.0);

            try
            {
                var evt = CGEventCreateMouseEvent(source, eventType, position, button);
                if (evt == IntPtr.Zero) return;

                try
                {
                    CGEventPost(kCGHIDEventTap, evt);
                }
                finally
                {
                    CFRelease(evt);
                }
            }
            finally
            {
                CFRelease(source);
            }
        }

        private CGPoint GetMousePosition(float? x, float? y)
        {
            // TODO: Get current mouse position if not specified
            return new CGPoint(x ?? 0, y ?? 0);
        }

        private int GetMouseDownEventType(int button)
        {
            return button switch
            {
                0 => kCGEventLeftMouseDown,
                1 => kCGEventRightMouseDown,
                _ => kCGEventOtherMouseDown
            };
        }

        private int GetMouseUpEventType(int button)
        {
            return button switch
            {
                0 => kCGEventLeftMouseUp,
                1 => kCGEventRightMouseUp,
                _ => kCGEventOtherMouseUp
            };
        }

        private int GetCGMouseButton(int button)
        {
            return button switch
            {
                0 => kCGMouseButtonLeft,
                1 => kCGMouseButtonRight,
                _ => kCGMouseButtonCenter
            };
        }

        private static string GetButtonName(int button) => InputSimulatorUtils.GetButtonName(button);

        #endregion
    }
}
#endif
