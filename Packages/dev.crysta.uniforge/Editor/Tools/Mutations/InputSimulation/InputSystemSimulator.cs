#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace UniForge.Tools.Mutations.InputSimulation
{
    /// <summary>
    /// Unity Input System + OS ネイティブのハイブリッド入力シミュレーター。
    /// マウス位置: Mouse.WarpCursorPosition で物理カーソルを移動し、QueueStateEvent で Input System にも反映。
    /// マウスボタン: CGEvent (macOS) で OS レベルのイベントを送信し、Old Input Manager にも反映。
    /// キーボード: Input System の QueueStateEvent を使用。
    /// </summary>
    public class InputSystemSimulator : IInputSimulator
    {
        public string Name => "InputSystem";

        public bool IsAvailable => Mouse.current != null || Keyboard.current != null;

#if UNITY_EDITOR_OSX
        private MacOSInputSimulator _macOS;
        private MacOSInputSimulator MacOS => _macOS ??= new MacOSInputSimulator();
#elif UNITY_EDITOR_WIN
        // Windows では static メソッドを使用するためインスタンスは不要
#endif

        #region Keyboard

        public InputSimulationResult KeyDown(string key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return InputSimulationResult.Fail("No keyboard device available");
            if (string.IsNullOrEmpty(key))
                return InputSimulationResult.Fail("Parameter 'key' is required");

            var keyControl = GetKeyControl(keyboard, key);
            if (keyControl == null)
                return InputSimulationResult.Fail($"Invalid key name: {key}");

            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyControl.WriteValueIntoEvent(1f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            return InputSimulationResult.Ok("key_down", $"Key '{key}' pressed down", $"Simulated key down: {key}", Name);
        }

        public InputSimulationResult KeyUp(string key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return InputSimulationResult.Fail("No keyboard device available");
            if (string.IsNullOrEmpty(key))
                return InputSimulationResult.Fail("Parameter 'key' is required");

            var keyControl = GetKeyControl(keyboard, key);
            if (keyControl == null)
                return InputSimulationResult.Fail($"Invalid key name: {key}");

            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyControl.WriteValueIntoEvent(0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            return InputSimulationResult.Ok("key_up", $"Key '{key}' released", $"Simulated key up: {key}", Name);
        }

        public InputSimulationResult KeyPress(string key, int durationMs = 100)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return InputSimulationResult.Fail("No keyboard device available");
            if (string.IsNullOrEmpty(key))
                return InputSimulationResult.Fail("Parameter 'key' is required");

            var keyControl = GetKeyControl(keyboard, key);
            if (keyControl == null)
                return InputSimulationResult.Fail($"Invalid key name: {key}");

            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyControl.WriteValueIntoEvent(1f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            InputSimulatorUtils.ScheduleAfterMilliseconds(durationMs, () =>
            {
                if (keyboard != null && Keyboard.current == keyboard)
                {
                    using (StateEvent.From(keyboard, out var eventPtr))
                    {
                        keyControl.WriteValueIntoEvent(0f, eventPtr);
                        InputSystem.QueueEvent(eventPtr);
                    }
                }
            });

            return InputSimulationResult.Ok("key_press", $"Key '{key}' pressed for ~{durationMs}ms", $"Simulated key press: {key}", Name);
        }

        #endregion

        #region Mouse

        public InputSimulationResult MouseDown(int button, float? x = null, float? y = null)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return InputSimulationResult.Fail("No mouse device available");

            // 物理カーソルを移動 + Input System に位置反映 + CGEvent 移動イベント送信
            if (x.HasValue && y.HasValue)
                WarpAndQueuePosition(mouse, x.Value, y.Value);

            // CGEvent でボタンイベント送信（Old Input Manager 対応）
            PostNativeMouseButton(button, true);

            // Input System 側にもボタン状態を反映
            var buttonControl = GetMouseButtonControl(mouse, button);
            if (buttonControl != null)
            {
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    if (x.HasValue && y.HasValue)
                        mouse.position.WriteValueIntoEvent(new Vector2(x.Value, y.Value), eventPtr);
                    buttonControl.WriteValueIntoEvent(1f, eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
            }

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F1}, {y.Value:F1})" : "";
            return InputSimulationResult.Ok("mouse_down", $"Mouse {buttonName} button pressed{positionInfo}",
                $"Simulated mouse down: {buttonName} button{positionInfo}", Name);
        }

        public InputSimulationResult MouseUp(int button, float? x = null, float? y = null)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return InputSimulationResult.Fail("No mouse device available");

            if (x.HasValue && y.HasValue)
                WarpAndQueuePosition(mouse, x.Value, y.Value);

            PostNativeMouseButton(button, false);

            var buttonControl = GetMouseButtonControl(mouse, button);
            if (buttonControl != null)
            {
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    if (x.HasValue && y.HasValue)
                        mouse.position.WriteValueIntoEvent(new Vector2(x.Value, y.Value), eventPtr);
                    buttonControl.WriteValueIntoEvent(0f, eventPtr);
                    InputSystem.QueueEvent(eventPtr);
                }
            }

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F1}, {y.Value:F1})" : "";
            return InputSimulationResult.Ok("mouse_up", $"Mouse {buttonName} button released{positionInfo}",
                $"Simulated mouse up: {buttonName} button{positionInfo}", Name);
        }

        public InputSimulationResult MouseClick(int button, float? x = null, float? y = null)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return InputSimulationResult.Fail("No mouse device available");

            if (x.HasValue && y.HasValue)
                WarpAndQueuePosition(mouse, x.Value, y.Value);

            // Button down (CGEvent + QueueStateEvent)
            PostNativeMouseButton(button, true);

            var buttonControl = GetMouseButtonControl(mouse, button);
            if (buttonControl != null)
            {
                using (StateEvent.From(mouse, out var downPtr))
                {
                    if (x.HasValue && y.HasValue)
                        mouse.position.WriteValueIntoEvent(new Vector2(x.Value, y.Value), downPtr);
                    buttonControl.WriteValueIntoEvent(1f, downPtr);
                    InputSystem.QueueEvent(downPtr);
                }
            }

            // Button up は次フレームで
            EditorApplication.delayCall += () =>
            {
                PostNativeMouseButton(button, false);

                if (mouse != null && Mouse.current == mouse && buttonControl != null)
                {
                    using (StateEvent.From(mouse, out var upPtr))
                    {
                        if (x.HasValue && y.HasValue)
                            mouse.position.WriteValueIntoEvent(new Vector2(x.Value, y.Value), upPtr);
                        buttonControl.WriteValueIntoEvent(0f, upPtr);
                        InputSystem.QueueEvent(upPtr);
                    }
                }
            };

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F1}, {y.Value:F1})" : "";
            return InputSimulationResult.Ok("mouse_click", $"Mouse {buttonName} click{positionInfo}",
                $"Simulated mouse click: {buttonName} button{positionInfo}", Name);
        }

        public InputSimulationResult MouseMove(float x, float y)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return InputSimulationResult.Fail("No mouse device available");

            WarpAndQueuePosition(mouse, x, y);

            return InputSimulationResult.Ok("mouse_move", $"Mouse moved to ({x:F1}, {y:F1})",
                $"Simulated mouse move to ({x:F1}, {y:F1})", Name);
        }

        /// <summary>
        /// ドラッグ中のマウス移動（WarpCursorPosition + QueueStateEvent + NSEvent LeftMouseDragged）
        /// </summary>
        public void MouseDragMove(int button, float x, float y)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            WarpAndQueuePosition(mouse, x, y);

            // ドラッグ中の移動イベントを送信（Old Input Manager 対応）
            // WarpAndQueuePosition 内の SendNativeMouseMoveEvent は MouseMoved を送信するが、
            // ドラッグ中は MouseDragged イベントが必要。
#if UNITY_EDITOR_OSX
            // NSEvent LeftMouseDragged を送信
            MacOS.SendNSMouseEvent(button, true, isDrag: true);
#elif UNITY_EDITOR_WIN
            // Windows では SendInput(MOUSEEVENTF_MOVE) がドラッグ中も WM_MOUSEMOVE として届く。
            // ボタン押下中の MOUSEMOVE は OS が自動的にドラッグとして扱う。
            // WarpAndQueuePosition 内で既に SendNativeMouseMoveEvent を呼んでいるため追加不要。
#endif
        }

        public InputSimulationResult MouseScroll(float delta)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return InputSimulationResult.Fail("No mouse device available");

            using (StateEvent.From(mouse, out var eventPtr))
            {
                mouse.scroll.WriteValueIntoEvent(new Vector2(0, delta), eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            return InputSimulationResult.Ok("mouse_scroll", $"Mouse scrolled by {delta}",
                $"Simulated mouse scroll: {delta}", Name);
        }

        /// <summary>
        /// 物理カーソルを移動し、QueueStateEvent で Input System に位置を反映し、
        /// NSEvent で OS レベルの移動イベントを送信（Old Input Manager 対応）。
        /// </summary>
        private void WarpAndQueuePosition(Mouse mouse, float x, float y)
        {
            // WarpCursorPosition で物理カーソルを移動（Unity が内部的に正しい OS 座標に変換）
            mouse.WarpCursorPosition(new Vector2(x, y));

            // macOS: CGWarpMouseCursorPosition 後の 0.25 秒イベント抑制を解除
#if UNITY_EDITOR_OSX
            MacOSInputSimulator.ReenableMouseEventSuppression();
#endif

            // Input System に位置を反映
            using (StateEvent.From(mouse, out var eventPtr))
            {
                mouse.position.WriteValueIntoEvent(new Vector2(x, y), eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            // OS ネイティブの移動イベントを送信（Old Input Manager 対応）
            // WarpCursorPosition はカーソル移動のみでイベントを生成しない。
            // macOS: NSEvent 経由（CGWarp 抑制の影響を受けない）
            // Windows: SendInput(MOUSEEVENTF_MOVE) で WM_MOUSEMOVE を発行
#if UNITY_EDITOR_OSX
            MacOS.SendNSMouseMoveEvent();
#elif UNITY_EDITOR_WIN
            WindowsInputSimulator.SendNativeMouseMoveEvent();
#endif
        }

        /// <summary>
        /// OS ネイティブのマウスボタンイベントを NSEvent 経由で送信（Old Input Manager 対応）。
        /// </summary>
        private void PostNativeMouseButton(int button, bool isDown)
        {
            // OS ネイティブのマウスボタンイベントを送信（Old Input Manager 対応）
#if UNITY_EDITOR_OSX
            // NSEvent 経由で送信（CGWarpMouseCursorPosition の抑制の影響を受けない）
            MacOS.SendNSMouseEvent(button, isDown);
#elif UNITY_EDITOR_WIN
            // SendInput API で WM_LBUTTONDOWN/UP 等を発行
            WindowsInputSimulator.SendNativeMouseButtonEvent(button, isDown);
#endif
        }

        #endregion

        public void FocusApplication()
        {
#if UNITY_EDITOR_OSX
            MacOS.FocusApplication();
#elif UNITY_EDITOR_WIN
            WindowsInputSimulator.FocusUnityApplication();
#endif
        }

        #region Helpers

        private static string GetButtonName(int button) => InputSimulatorUtils.GetButtonName(button);

        private ButtonControl GetMouseButtonControl(Mouse mouse, int button)
        {
            return button switch
            {
                0 => mouse.leftButton,
                1 => mouse.rightButton,
                2 => mouse.middleButton,
                _ => null
            };
        }

        private KeyControl GetKeyControl(Keyboard keyboard, string keyName)
        {
            var normalizedName = keyName.ToLowerInvariant();

            var keyMappings = new Dictionary<string, Func<Keyboard, KeyControl>>
            {
                // Letters
                { "a", k => k.aKey }, { "b", k => k.bKey }, { "c", k => k.cKey }, { "d", k => k.dKey },
                { "e", k => k.eKey }, { "f", k => k.fKey }, { "g", k => k.gKey }, { "h", k => k.hKey },
                { "i", k => k.iKey }, { "j", k => k.jKey }, { "k", k => k.kKey }, { "l", k => k.lKey },
                { "m", k => k.mKey }, { "n", k => k.nKey }, { "o", k => k.oKey }, { "p", k => k.pKey },
                { "q", k => k.qKey }, { "r", k => k.rKey }, { "s", k => k.sKey }, { "t", k => k.tKey },
                { "u", k => k.uKey }, { "v", k => k.vKey }, { "w", k => k.wKey }, { "x", k => k.xKey },
                { "y", k => k.yKey }, { "z", k => k.zKey },

                // Numbers
                { "0", k => k.digit0Key }, { "1", k => k.digit1Key }, { "2", k => k.digit2Key },
                { "3", k => k.digit3Key }, { "4", k => k.digit4Key }, { "5", k => k.digit5Key },
                { "6", k => k.digit6Key }, { "7", k => k.digit7Key }, { "8", k => k.digit8Key },
                { "9", k => k.digit9Key },

                // Function keys
                { "f1", k => k.f1Key }, { "f2", k => k.f2Key }, { "f3", k => k.f3Key }, { "f4", k => k.f4Key },
                { "f5", k => k.f5Key }, { "f6", k => k.f6Key }, { "f7", k => k.f7Key }, { "f8", k => k.f8Key },
                { "f9", k => k.f9Key }, { "f10", k => k.f10Key }, { "f11", k => k.f11Key }, { "f12", k => k.f12Key },

                // Modifiers
                { "leftshift", k => k.leftShiftKey }, { "rightshift", k => k.rightShiftKey },
                { "shift", k => k.leftShiftKey },
                { "leftctrl", k => k.leftCtrlKey }, { "rightctrl", k => k.rightCtrlKey },
                { "ctrl", k => k.leftCtrlKey }, { "control", k => k.leftCtrlKey },
                { "leftalt", k => k.leftAltKey }, { "rightalt", k => k.rightAltKey },
                { "alt", k => k.leftAltKey },
                { "leftmeta", k => k.leftMetaKey }, { "rightmeta", k => k.rightMetaKey },
                { "meta", k => k.leftMetaKey }, { "command", k => k.leftMetaKey }, { "windows", k => k.leftMetaKey },

                // Special keys
                { "space", k => k.spaceKey }, { "spacebar", k => k.spaceKey },
                { "enter", k => k.enterKey }, { "return", k => k.enterKey },
                { "escape", k => k.escapeKey }, { "esc", k => k.escapeKey },
                { "tab", k => k.tabKey },
                { "backspace", k => k.backspaceKey },
                { "delete", k => k.deleteKey },
                { "insert", k => k.insertKey },
                { "home", k => k.homeKey },
                { "end", k => k.endKey },
                { "pageup", k => k.pageUpKey },
                { "pagedown", k => k.pageDownKey },

                // Arrow keys
                { "up", k => k.upArrowKey }, { "uparrow", k => k.upArrowKey },
                { "down", k => k.downArrowKey }, { "downarrow", k => k.downArrowKey },
                { "left", k => k.leftArrowKey }, { "leftarrow", k => k.leftArrowKey },
                { "right", k => k.rightArrowKey }, { "rightarrow", k => k.rightArrowKey },

                // Punctuation
                { "minus", k => k.minusKey }, { "-", k => k.minusKey },
                { "equals", k => k.equalsKey }, { "=", k => k.equalsKey },
                { "leftbracket", k => k.leftBracketKey }, { "[", k => k.leftBracketKey },
                { "rightbracket", k => k.rightBracketKey }, { "]", k => k.rightBracketKey },
                { "backslash", k => k.backslashKey }, { "\\", k => k.backslashKey },
                { "semicolon", k => k.semicolonKey }, { ";", k => k.semicolonKey },
                { "quote", k => k.quoteKey }, { "'", k => k.quoteKey },
                { "comma", k => k.commaKey }, { ",", k => k.commaKey },
                { "period", k => k.periodKey }, { ".", k => k.periodKey },
                { "slash", k => k.slashKey }, { "/", k => k.slashKey },
                { "backquote", k => k.backquoteKey }, { "`", k => k.backquoteKey },

                // Numpad
                { "numpad0", k => k.numpad0Key }, { "numpad1", k => k.numpad1Key },
                { "numpad2", k => k.numpad2Key }, { "numpad3", k => k.numpad3Key },
                { "numpad4", k => k.numpad4Key }, { "numpad5", k => k.numpad5Key },
                { "numpad6", k => k.numpad6Key }, { "numpad7", k => k.numpad7Key },
                { "numpad8", k => k.numpad8Key }, { "numpad9", k => k.numpad9Key },
                { "numpadenter", k => k.numpadEnterKey },
                { "numpadplus", k => k.numpadPlusKey },
                { "numpadminus", k => k.numpadMinusKey },
                { "numpadmultiply", k => k.numpadMultiplyKey },
                { "numpaddivide", k => k.numpadDivideKey },
                { "numpadperiod", k => k.numpadPeriodKey },

                // Lock keys
                { "capslock", k => k.capsLockKey },
                { "numlock", k => k.numLockKey },
                { "scrolllock", k => k.scrollLockKey },

                // Other
                { "printscreen", k => k.printScreenKey },
                { "pause", k => k.pauseKey },
            };

            if (keyMappings.TryGetValue(normalizedName, out var getter))
                return getter(keyboard);

            return null;
        }

        #endregion
    }
}
#endif
