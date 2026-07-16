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
    /// Unity Input System のイベントキューだけを使用する入力シミュレーター。
    /// Editor の前面化や物理カーソル操作を行わないため、バックグラウンドで利用できる。
    /// </summary>
    public class InputSystemSimulator : IInputSimulator
    {
        public string Name => "InputSystem";

        public bool IsAvailable => true;

        private Keyboard _syntheticKeyboard;
        private Mouse _syntheticMouse;

        #region Pending State (同一フレーム内の状態合成)

        /// <summary>
        /// 同一フレーム内で既にキューした入力状態のシャドウコピー。
        /// StateEvent.From はデバイスの「まだ更新されていない現在状態」をスナップショット
        /// するため、同一フレームで 2 回イベントをキューすると 2 回目が 1 回目を打ち消して
        /// しまう（例: Shift+A が A になる）。キュー済みの変更をここに記録し、
        /// 次のイベント構築時に上書き適用することで打ち消しを防ぐ。
        /// </summary>
        private sealed class PendingDeviceState
        {
            public readonly Dictionary<InputControl<float>, float> FloatValues = new Dictionary<InputControl<float>, float>();
            public Vector2? Position;
        }

        private static readonly Dictionary<InputDevice, PendingDeviceState> PendingStates = new Dictionary<InputDevice, PendingDeviceState>();
        private static bool _afterUpdateHooked;

        private static PendingDeviceState GetOrCreatePendingState(InputDevice device)
        {
            if (!PendingStates.TryGetValue(device, out var state))
            {
                state = new PendingDeviceState();
                PendingStates[device] = state;
            }

            // Input System の更新後（キューしたイベントがデバイス状態に反映された後）に
            // シャドウ状態を破棄する
            if (!_afterUpdateHooked)
            {
                InputSystem.onAfterUpdate += ClearPendingStates;
                _afterUpdateHooked = true;
            }

            return state;
        }

        private static void ClearPendingStates()
        {
            PendingStates.Clear();
            if (_afterUpdateHooked)
            {
                InputSystem.onAfterUpdate -= ClearPendingStates;
                _afterUpdateHooked = false;
            }
        }

        /// <summary>
        /// 同一フレーム内でキュー済みの変更をイベントに上書き適用する
        /// </summary>
        private static void ApplyPendingState(InputDevice device, InputEventPtr eventPtr)
        {
            if (!PendingStates.TryGetValue(device, out var state)) return;

            foreach (var kv in state.FloatValues)
            {
                kv.Key.WriteValueIntoEvent(kv.Value, eventPtr);
            }

            if (state.Position.HasValue && device is Mouse mouseDevice)
            {
                mouseDevice.position.WriteValueIntoEvent(state.Position.Value, eventPtr);
            }
        }

        /// <summary>
        /// シャドウ状態を反映した StateEvent を構築してキューする。
        /// control / position の変更はシャドウ状態にも記録される。
        /// </summary>
        private static void QueueStateWithPending(InputDevice device, InputControl<float> control, float value, Vector2? position)
        {
            var pending = GetOrCreatePendingState(device);
            if (position.HasValue) pending.Position = position.Value;
            if (control != null) pending.FloatValues[control] = value;

            using (StateEvent.From(device, out var eventPtr))
            {
                ApplyPendingState(device, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        #endregion

        #region Keyboard

        public InputSimulationResult KeyDown(string key)
        {
            var keyboard = GetOrCreateKeyboard();
            if (string.IsNullOrEmpty(key))
                return InputSimulationResult.Fail("Parameter 'key' is required");

            var keyControl = GetKeyControl(keyboard, key);
            if (keyControl == null)
                return InputSimulationResult.Fail($"Invalid key name: {key}");

            QueueStateWithPending(keyboard, keyControl, 1f, null);

            return InputSimulationResult.Ok("key_down", $"Key '{key}' pressed down", $"Simulated key down: {key}", Name);
        }

        public InputSimulationResult KeyUp(string key)
        {
            var keyboard = GetOrCreateKeyboard();
            if (string.IsNullOrEmpty(key))
                return InputSimulationResult.Fail("Parameter 'key' is required");

            var keyControl = GetKeyControl(keyboard, key);
            if (keyControl == null)
                return InputSimulationResult.Fail($"Invalid key name: {key}");

            QueueStateWithPending(keyboard, keyControl, 0f, null);

            return InputSimulationResult.Ok("key_up", $"Key '{key}' released", $"Simulated key up: {key}", Name);
        }

        public InputSimulationResult KeyPress(string key, int durationMs = 100)
        {
            var keyboard = GetOrCreateKeyboard();
            if (string.IsNullOrEmpty(key))
                return InputSimulationResult.Fail("Parameter 'key' is required");

            var keyControl = GetKeyControl(keyboard, key);
            if (keyControl == null)
                return InputSimulationResult.Fail($"Invalid key name: {key}");

            QueueStateWithPending(keyboard, keyControl, 1f, null);

            // durationMs 経過後にキー解放をスケジュール
            // （ドメインリロード・エディタ終了時もフラッシュされ、stuck key を防ぐ）
            PendingInputReleaseRegistry.Register(() =>
            {
                if (keyboard != null && Keyboard.current == keyboard)
                {
                    QueueStateWithPending(keyboard, keyControl, 0f, null);
                }
            }, durationMs / 1000.0);

            return InputSimulationResult.Ok("key_press", $"Key '{key}' pressed for ~{durationMs}ms", $"Simulated key press: {key}", Name);
        }

        #endregion

        #region Mouse

        public InputSimulationResult MouseDown(int button, float? x = null, float? y = null)
        {
            var mouse = GetOrCreateMouse();

            if (x.HasValue && y.HasValue)
                QueuePosition(mouse, x.Value, y.Value);

            var buttonControl = GetMouseButtonControl(mouse, button);
            if (buttonControl != null)
            {
                var position = x.HasValue && y.HasValue ? new Vector2(x.Value, y.Value) : (Vector2?)null;
                QueueStateWithPending(mouse, buttonControl, 1f, position);
            }

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F1}, {y.Value:F1})" : "";
            return InputSimulationResult.Ok("mouse_down", $"Mouse {buttonName} button pressed{positionInfo}",
                $"Simulated mouse down: {buttonName} button{positionInfo}", Name);
        }

        public InputSimulationResult MouseUp(int button, float? x = null, float? y = null)
        {
            var mouse = GetOrCreateMouse();

            if (x.HasValue && y.HasValue)
                QueuePosition(mouse, x.Value, y.Value);

            var buttonControl = GetMouseButtonControl(mouse, button);
            if (buttonControl != null)
            {
                var position = x.HasValue && y.HasValue ? new Vector2(x.Value, y.Value) : (Vector2?)null;
                QueueStateWithPending(mouse, buttonControl, 0f, position);
            }

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F1}, {y.Value:F1})" : "";
            return InputSimulationResult.Ok("mouse_up", $"Mouse {buttonName} button released{positionInfo}",
                $"Simulated mouse up: {buttonName} button{positionInfo}", Name);
        }

        public InputSimulationResult MouseClick(int button, float? x = null, float? y = null)
        {
            var mouse = GetOrCreateMouse();

            if (x.HasValue && y.HasValue)
                QueuePosition(mouse, x.Value, y.Value);

            var clickPosition = x.HasValue && y.HasValue ? new Vector2(x.Value, y.Value) : (Vector2?)null;

            var buttonControl = GetMouseButtonControl(mouse, button);
            if (buttonControl != null)
            {
                QueueStateWithPending(mouse, buttonControl, 1f, clickPosition);
            }

            // Button up は次の update tick で
            // （ドメインリロード・エディタ終了時もフラッシュされ、stuck button を防ぐ）
            PendingInputReleaseRegistry.Register(() =>
            {
                if (mouse != null && Mouse.current == mouse && buttonControl != null)
                {
                    QueueStateWithPending(mouse, buttonControl, 0f, clickPosition);
                }
            }, 0.0);

            var buttonName = GetButtonName(button);
            var positionInfo = x.HasValue && y.HasValue ? $" at ({x.Value:F1}, {y.Value:F1})" : "";
            return InputSimulationResult.Ok("mouse_click", $"Mouse {buttonName} click{positionInfo}",
                $"Simulated mouse click: {buttonName} button{positionInfo}", Name);
        }

        public InputSimulationResult MouseMove(float x, float y)
        {
            var mouse = GetOrCreateMouse();

            QueuePosition(mouse, x, y);

            return InputSimulationResult.Ok("mouse_move", $"Mouse moved to ({x:F1}, {y:F1})",
                $"Simulated mouse move to ({x:F1}, {y:F1})", Name);
        }

        /// <summary>ドラッグ中のマウス位置を Input System に反映する</summary>
        public void MouseDragMove(int button, float x, float y)
        {
            var mouse = GetOrCreateMouse();

            QueuePosition(mouse, x, y);
        }

        public InputSimulationResult MouseScroll(float delta)
        {
            var mouse = GetOrCreateMouse();

            using (StateEvent.From(mouse, out var eventPtr))
            {
                // 同一フレームでキュー済みのボタン状態・位置を保持したままスクロールを反映
                ApplyPendingState(mouse, eventPtr);
                mouse.scroll.WriteValueIntoEvent(new Vector2(0, delta), eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            return InputSimulationResult.Ok("mouse_scroll", $"Mouse scrolled by {delta}",
                $"Simulated mouse scroll: {delta}", Name);
        }

        /// <summary>Input System に仮想マウス位置を反映する</summary>
        private static void QueuePosition(Mouse mouse, float x, float y)
        {
            // 同一フレームでキュー済みのボタン状態を保持したまま、
            // Input System 内の仮想位置だけを更新する。
            QueueStateWithPending(mouse, null, 0f, new Vector2(x, y));
        }

        #endregion

        #region Helpers

        private Keyboard GetOrCreateKeyboard()
        {
            if (Keyboard.current != null)
                return Keyboard.current;

            if (_syntheticKeyboard == null || !_syntheticKeyboard.added)
                _syntheticKeyboard = InputSystem.AddDevice<Keyboard>();

            return _syntheticKeyboard;
        }

        private Mouse GetOrCreateMouse()
        {
            if (Mouse.current != null)
                return Mouse.current;

            if (_syntheticMouse == null || !_syntheticMouse.added)
                _syntheticMouse = InputSystem.AddDevice<Mouse>();

            return _syntheticMouse;
        }

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
