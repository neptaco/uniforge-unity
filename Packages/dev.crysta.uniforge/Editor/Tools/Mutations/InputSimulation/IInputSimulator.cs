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

        /// <summary>Unity Editor の Game View にフォーカスを当てる</summary>
        public static void FocusGameView()
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) return;

            var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
            if (gameView != null)
            {
                gameView.Focus();
            }
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

        /// <summary>
        /// アプリケーションをフォアグラウンドにして入力を受け付ける状態にする
        /// OS ネイティブシミュレーターで必要
        /// </summary>
        void FocusApplication();
    }
}
