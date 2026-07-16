using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UniForge.Services;
using UniForge.Tools.Queries;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// 複数の入力シミュレーションステップを順次実行するツール。
    /// 各ステップは simulate-input と同じアクションパラメータに加え、
    /// wait_for_log, wait_for_object, capture 等の E2E テスト用アクションを持つ。
    /// </summary>
    [Tool("auto-play",
        Description = "Execute a sequence of background-safe play mode steps: Input System keyboard/mouse events, EventSystem UI actions, waits, assertions, and captures. It never activates the Unity Editor or moves the physical cursor. Keyboard and coordinate mouse actions require the Input System package; legacy Input Manager injection is intentionally unsupported. Steps can be inline or loaded from a scenario file.",
        Title = "Auto Play",
        Category = ToolCategory.Input,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public class AutoPlayHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            public class Step
            {
                [ToolParameter("Action type: key_down, key_up, key_press, mouse_click, tap_ui, input_text, wait, wait_for_log, wait_for_object, wait_for_ui_state, capture, etc. All actions run without activating Unity or moving the physical cursor.", Required = true)]
                public string action;

                [ToolParameter("Unique ID for this step. Can be referenced by 'since' in later wait_for_log steps.")]
                public string id;

                [ToolParameter("Key name for keyboard input")]
                public string key;

                [ToolParameter("Wait time after this step in milliseconds (game continues running)")]
                public int? wait_ms;

                [ToolParameter("Duration in milliseconds for key_press/drag/long_press")]
                public int? duration_ms;

                [ToolParameter("X position for mouse input")]
                public float? x;

                [ToolParameter("Y position for mouse input")]
                public float? y;

                [ToolParameter("Mouse button (0=left, 1=right, 2=middle)")]
                public int? button;

                [ToolParameter("Position [x,y] for tap/long_press")]
                public float[] position;

                [ToolParameter("Start position [x,y] for drag")]
                public float[] from;

                [ToolParameter("End position [x,y] for drag")]
                public float[] to;

                [ToolParameter("Coordinate system: 'screen' or 'world'")]
                public string coordinate;

                [ToolParameter("Scroll delta for mouse_scroll")]
                public float? scroll_delta;

                [ToolParameter("Wait time in milliseconds for wait action")]
                public int? ms;

                [ToolParameter("Regex pattern for wait_for_log")]
                public string pattern;

                [ToolParameter("Timeout in milliseconds for wait_for_log / wait_for_object (default: 10000)")]
                public int? timeout_ms;

                [ToolParameter("Log type filter for wait_for_log: 'all', 'errors', 'warnings', 'info'")]
                public string filter;

                [ToolParameter("Poll interval in milliseconds for wait actions (default: 250)")]
                public int? poll_interval_ms;

                [ToolParameter("GameObject name for wait_for_object")]
                public string name;

                [ToolParameter("GameObject hierarchy path for wait_for_object")]
                public string path;

                [ToolParameter("Expected state for wait_for_object: 'exists' or 'destroyed' (default: 'exists')")]
                public string state;

                [ToolParameter("Condition for wait_for_ui_state: 'interactable', 'active', 'text_equals', 'text_contains', 'toggle_on', 'slider_value'")]
                public string condition;

                [ToolParameter("Expected value for wait_for_ui_state (bool for interactable/active/toggle_on, string for text_equals/text_contains, float for slider_value)")]
                public string value;

                [ToolParameter("Minimum value for slider_value condition")]
                public float? min;

                [ToolParameter("Maximum value for slider_value condition")]
                public float? max;

                [ToolParameter("Text to input for input_text action")]
                public string text;

                [ToolParameter("If true, append text instead of replacing for input_text action")]
                public bool? append;

                [ToolParameter("If true, invoke the input field's submit/end-edit event after input_text")]
                public bool? submit;

                [ToolParameter("Instance ID of the target GameObject (alternative to path/name for tap_ui, input_text, wait_for_ui_state)")]
                public int? instance_id;

                [ToolParameter("Filename for capture action (without extension)")]
                public string filename;

                [ToolParameter("For capture: capture only 3D render without UI (default: false)")]
                public bool? game_only;

            }

            [ToolParameter("Array of steps to execute sequentially (mutually exclusive with scenario_file)")]
            public Step[] steps;

            [ToolParameter("Path to a JSON scenario file relative to project root (mutually exclusive with steps)")]
            public string scenario_file;

            [ToolParameter("Log type filter for collected logs (default: 'all')", Enum = "all,errors,warnings,info")]
            public string log_filter;

            [ToolParameter("Maximum number of logs to collect across all steps", Default = 100)]
            public int log_limit;
        }

        /// <summary>出力定義</summary>
        [Serializable]
        public class Output
        {
            public bool success;
            public string error;
            public int steps_executed;
            public int total_steps;
            public List<string> steps;
            public int total_log_count;
            public List<LogEntryCompact> logs;
            public List<string> captures;
        }

        protected internal override async Awaitable<ToolResult> ExecuteAsync(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);

            var steps = ResolveSteps(args);
            if (steps == null)
                return ToolResult.Fail("Either 'steps' or 'scenario_file' is required");
            if (steps.Length == 0)
                return ToolResult.Fail("Steps array is empty");

            var logFilter = args.GetString("log_filter", "all");
            var logLimit = args.GetInt("log_limit", 100);

            var result = await AutoPlayService.Instance.ExecuteScenarioAsync(steps, logFilter, logLimit);

            return ToolResult.Ok(new Output
            {
                success = result.Success,
                error = result.Error,
                steps_executed = result.StepsExecuted,
                total_steps = result.TotalSteps,
                steps = result.Steps,
                total_log_count = result.TotalLogCount,
                logs = result.Logs,
                captures = result.Captures
            });
        }

        private static JsonObject[] ResolveSteps(ToolArgsParser args)
        {
            var inlineSteps = args.Json.GetObjectArray("steps");
            var scenarioFile = args.GetString("scenario_file");

            bool hasInline = inlineSteps != null && inlineSteps.Length > 0;
            bool hasFile = !string.IsNullOrEmpty(scenarioFile);

            if (hasInline && hasFile)
                return null; // 排他エラーは呼び出し元で処理

            if (hasFile)
                return LoadScenarioFile(scenarioFile);

            return inlineSteps;
        }

        private static JsonObject[] LoadScenarioFile(string relativePath)
        {
            var projectRoot = Path.GetFullPath(Application.dataPath + "/..");
            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            if (!fullPath.StartsWith(projectRoot))
            {
                Debug.LogError($"[AutoPlay] Scenario file must be within the project directory: {relativePath}");
                return null;
            }

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[AutoPlay] Scenario file not found: {relativePath}");
                return null;
            }

            try
            {
                var json = File.ReadAllText(fullPath);
                var root = JsonObject.Parse(json);
                var steps = root.GetObjectArray("steps");

                if (steps == null || steps.Length == 0)
                {
                    Debug.LogError($"[AutoPlay] Scenario file has no steps: {relativePath}");
                    return null;
                }

                return steps;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AutoPlay] Failed to load scenario file: {ex.Message}");
                return null;
            }
        }
    }
}
