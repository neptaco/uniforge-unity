using System;
using UnityEditor;

namespace UniForge.Tools.Mutations.InputSimulation
{
    /// <summary>
    /// 入力シミュレーター共通のヘルパーメソッド
    /// </summary>
    public static class InputSimulatorUtils
    {
        /// <summary>マウスボタン番号を表示名に変換</summary>
        public static string GetButtonName(int button)
        {
            return button switch
            {
                0 => "left",
                1 => "right",
                2 => "middle",
                _ => $"button{button}"
            };
        }

        /// <summary>
        /// ゲーム時間に依存せず、指定時間後に Editor update 上で処理を実行する。
        /// timeScale=0 や Editor がバックグラウンドの状態でも duration_ms を尊重する。
        /// </summary>
        internal static void ScheduleAfterMilliseconds(int durationMs, Action callback)
        {
            if (callback == null)
                return;

            var deadline = EditorApplication.timeSinceStartup + Math.Max(0, durationMs) / 1000.0;
            EditorApplication.CallbackFunction updateCallback = null;
            updateCallback = () =>
            {
                if (EditorApplication.timeSinceStartup < deadline)
                    return;

                EditorApplication.update -= updateCallback;
                callback();
            };

            EditorApplication.update += updateCallback;
        }
    }

    /// <summary>
    /// 入力シミュレーションの結果
    /// </summary>
    public class InputSimulationResult
    {
        public bool Success { get; set; }
        public string Action { get; set; }
        public string Details { get; set; }
        public string Message { get; set; }
        public string SimulatorType { get; set; }
        public string Error { get; set; }

        public static InputSimulationResult Ok(string action, string details, string message, string simulatorType)
        {
            return new InputSimulationResult
            {
                Success = true,
                Action = action,
                Details = details,
                Message = message,
                SimulatorType = simulatorType
            };
        }

        public static InputSimulationResult Fail(string error)
        {
            return new InputSimulationResult
            {
                Success = false,
                Error = error
            };
        }
    }

    /// <summary>
    /// 入力シミュレーターのインターフェース
    /// </summary>
    public interface IInputSimulator
    {
        /// <summary>シミュレーターの名前</summary>
        string Name { get; }

        /// <summary>このシミュレーターが現在の環境で利用可能か</summary>
        bool IsAvailable { get; }

        /// <summary>キーを押下</summary>
        InputSimulationResult KeyDown(string key);

        /// <summary>キーを解放</summary>
        InputSimulationResult KeyUp(string key);

        /// <summary>キーを押して離す</summary>
        InputSimulationResult KeyPress(string key, int durationMs = 100);

        /// <summary>マウスボタンを押下</summary>
        InputSimulationResult MouseDown(int button, float? x = null, float? y = null);

        /// <summary>マウスボタンを解放</summary>
        InputSimulationResult MouseUp(int button, float? x = null, float? y = null);

        /// <summary>マウスクリック</summary>
        InputSimulationResult MouseClick(int button, float? x = null, float? y = null);

        /// <summary>マウス移動</summary>
        InputSimulationResult MouseMove(float x, float y);

        /// <summary>マウススクロール</summary>
        InputSimulationResult MouseScroll(float delta);

    }
}
