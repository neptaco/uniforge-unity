using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UniForge.Tools.Queries;
using UniForge.Tools.Mutations;
using UniForge.Tools.Mutations.InputSimulation;

namespace UniForge.Services
{
    /// <summary>
    /// 入力シミュレーション、ログ検証、キャプチャのコアサービス。
    /// SimulateInputHandler と AutoPlayHandler の両方から利用される。
    /// </summary>
    public class AutoPlayService
    {
        private static AutoPlayService _instance;
        public static AutoPlayService Instance => _instance ??= new AutoPlayService();

        private IInputSimulator _keyboardSimulator;
#if ENABLE_INPUT_SYSTEM
        private InputSystemSimulator _inputSystemSimulator;
#endif

        private const int DefaultWaitForTimeoutMs = 10000;
        private const int DefaultWaitForPollMs = 250;

        // ---------------------------------------------------------------
        //  Result types
        // ---------------------------------------------------------------

        /// <summary>単一ステップの実行結果</summary>
        public class StepResult
        {
            public bool Success;
            public string Error;

            // Action info
            public string Action;
            public string Details;
            public string Message;
            public string SimulatorType;

            // Wait/log info (populated when wait_ms > 0)
            public int WaitedMs;
            public List<LogEntryCompact> Logs;
            public int LogCount;

            // Capture info
            public string CapturePath;

            // UI hit info
            // Input System (com.unity.inputsystem) 未導入時、tap 等の入力シミュレーション系
            // アクションでは常に null（tap_ui はアクション自体が失敗する）。
            // input_text のみ Input System なしでも設定される。
            public UiHitCompact hit_ui;
            public List<UiHitCompact> ui_hits;

            public static StepResult Fail(string error) => new() { Success = false, Error = error };
        }

        public class UiHitCompact
        {
            public string name;
            public string path;
            public int instance_id;
            public string module;
            public int depth;
            public int sorting_order;
            public string component_type;
            public bool? interactable;
            public string text;
        }

        /// <summary>シナリオ(複数ステップ)の実行結果</summary>
        public class ScenarioResult
        {
            public bool Success;
            public string Error;
            public int StepsExecuted;
            public int TotalSteps;
            public List<string> Steps;
            public int TotalLogCount;
            public List<LogEntryCompact> Logs;
            public List<string> Captures;
        }

        // ---------------------------------------------------------------
        //  Public API
        // ---------------------------------------------------------------

        /// <summary>単一アクションを実行(同期、wait 非対応)</summary>
        public StepResult ExecuteStep(JsonObject args)
        {
            return ExecuteActionCore(args);
        }

        /// <summary>単一アクションを実行(非同期、wait 対応)</summary>
        public async Awaitable<StepResult> ExecuteStepAsync(JsonObject args)
        {
            var actionLower = (args.GetString("action") ?? "").ToLowerInvariant();

            // 非同期ステップを直接ディスパッチ
            if (IsAsyncAction(actionLower))
                return await ExecuteAsyncStep(args, actionLower, "all", 50, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            var result = ExecuteActionCore(args);
            if (!result.Success) return result;

            var waitMs = GetRequestedWaitMs(args, actionLower);
            if (waitMs > 0)
            {
                var waitResult = await WaitAndCollectLogsAsync(
                    waitMs,
                    args.GetString("log_filter", "all"),
                    args.GetInt("log_limit", 50));
                result.WaitedMs = waitResult.WaitedMs;
                result.Logs = waitResult.Logs;
                result.LogCount = waitResult.LogCount;
            }

            return result;
        }

        /// <summary>シナリオ(複数ステップ)を実行</summary>
        public async Awaitable<ScenarioResult> ExecuteScenarioAsync(
            JsonObject[] steps,
            string logFilter,
            int logLimit)
        {
            var allLogs = new List<LogEntryCompact>();
            var stepResults = new List<string>();
            var captures = new List<string>();
            int totalLogCount = 0;

            var scenarioStartTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var lastStepTs = scenarioStartTs;
            var stepTimestamps = new Dictionary<string, long>();

            for (int i = 0; i < steps.Length; i++)
            {
                var args = steps[i];
                var actionLower = (args.GetString("action") ?? "").ToLowerInvariant();

                // ステップ開始時刻を記録（id 参照時はこのステップのログも含まれる）
                var stepStartTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var stepId = args.GetString("id");
                if (!string.IsNullOrEmpty(stepId))
                    stepTimestamps[stepId] = stepStartTs;

                StepResult result;

                if (IsAsyncAction(actionLower))
                {
                    var sinceTs = ResolveSinceTs(args, scenarioStartTs, lastStepTs, stepTimestamps);
                    result = await ExecuteAsyncStep(args, actionLower, logFilter, Math.Max(1, logLimit - totalLogCount), sinceTs);
                }
                else
                {
                    result = ExecuteActionCore(args);
                    if (result.Success)
                    {
                        var waitMs = GetRequestedWaitMs(args, actionLower);
                        if (waitMs > 0)
                        {
                            var waitResult = await WaitAndCollectLogsAsync(
                                waitMs, logFilter, Math.Max(1, logLimit - totalLogCount));
                            totalLogCount += waitResult.LogCount;
                            allLogs.AddRange(waitResult.Logs);
                        }
                    }
                }

                // 直前ステップ完了時刻を更新（"previous" 参照用）
                lastStepTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (!result.Success)
                {
                    stepResults.Add($"step_{i + 1}: {actionLower} - FAIL: {result.Error}");
                    return new ScenarioResult
                    {
                        Success = false,
                        Error = $"Step {i + 1} failed: {result.Error}",
                        StepsExecuted = stepResults.Count,
                        TotalSteps = steps.Length,
                        Steps = stepResults,
                        TotalLogCount = totalLogCount,
                        Logs = allLogs,
                        Captures = captures
                    };
                }

                // 非同期ステップのログを集約
                if (result.Logs != null && result.Logs.Count > 0)
                {
                    totalLogCount += result.LogCount;
                    allLogs.AddRange(result.Logs);
                }

                if (!string.IsNullOrEmpty(result.CapturePath))
                    captures.Add(result.CapturePath);

                var stepLabelText = FormatStepLabel(i + 1, actionLower, args, result);
                stepResults.Add(stepLabelText);
            }

            return new ScenarioResult
            {
                Success = true,
                StepsExecuted = stepResults.Count,
                TotalSteps = steps.Length,
                Steps = stepResults,
                TotalLogCount = totalLogCount,
                Logs = allLogs,
                Captures = captures
            };
        }

        // ---------------------------------------------------------------
        //  Async step dispatch
        // ---------------------------------------------------------------

        private static bool IsAsyncAction(string action)
        {
            return action is "wait_for_log" or "wait_for_object" or "wait_for_ui_state" or "capture";
        }

        private async Awaitable<StepResult> ExecuteAsyncStep(
            JsonObject args, string action, string logFilter, int logLimit, long sinceTs)
        {
            return action switch
            {
                "wait_for_log" => await ExecuteWaitForLog(args, sinceTs),
                "wait_for_object" => await ExecuteWaitForObject(args),
                "wait_for_ui_state" => await ExecuteWaitForUiState(args),
                "capture" => ExecuteCapture(args),
                _ => StepResult.Fail($"Unknown async action: {action}")
            };
        }

        /// <summary>
        /// since パラメータを解決する。
        /// - 未指定 or "previous": 直前のステップ完了時刻
        /// - "start": シナリオ開始時刻
        /// - その他: ラベル名として検索
        /// </summary>
        private static long ResolveSinceTs(
            JsonObject args,
            long scenarioStartTs,
            long lastStepTs,
            Dictionary<string, long> stepTimestamps)
        {
            var since = args.GetString("since");

            if (string.IsNullOrEmpty(since) || since == "previous")
                return lastStepTs;

            if (since == "start")
                return scenarioStartTs;

            if (stepTimestamps.TryGetValue(since, out var labelTs))
                return labelTs;

            // 不明なラベルは直前のステップにフォールバック
            Debug.LogWarning($"[AutoPlay] Unknown 'since' label: '{since}', falling back to previous step");
            return lastStepTs;
        }

        // ---------------------------------------------------------------
        //  wait_for_log
        // ---------------------------------------------------------------

        private async Awaitable<StepResult> ExecuteWaitForLog(JsonObject args, long baseSinceTs = 0)
        {
            var pattern = args.GetString("pattern");
            if (string.IsNullOrEmpty(pattern))
                return StepResult.Fail("wait_for_log requires 'pattern' parameter");

            try { _ = new Regex(pattern); }
            catch (ArgumentException ex) { return StepResult.Fail($"Invalid regex pattern: {ex.Message}"); }

            var timeoutMs = args.GetInt("timeout_ms", DefaultWaitForTimeoutMs);
            var filter = args.GetString("filter", "all");
            var pollMs = args.GetInt("poll_interval_ms", DefaultWaitForPollMs);
            var sinceTs = baseSinceTs > 0 ? baseSinceTs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var startTime = DateTimeOffset.UtcNow;
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);

            while (DateTimeOffset.UtcNow - startTime < timeout)
            {
                var matched = ConsoleLogCapture.instance.GetLogsFiltered(new LogFilterOptions
                {
                    TypeFilter = filter,
                    Since = sinceTs,
                    Pattern = pattern,
                    IgnoreCase = true,
                    Limit = 10
                });

                if (matched.Count > 0)
                {
                    var elapsed = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                    return new StepResult
                    {
                        Success = true,
                        Action = "wait_for_log",
                        Details = $"pattern='{pattern}' elapsed={elapsed}ms",
                        Message = $"Log matched: {matched[0].message}",
                        Logs = ToCompactLogs(matched),
                        LogCount = matched.Count
                    };
                }

                await Awaitable.WaitForSecondsAsync(pollMs / 1000f);
            }

            return StepResult.Fail(
                $"wait_for_log timed out after {timeoutMs}ms. Pattern: '{pattern}'");
        }

        // ---------------------------------------------------------------
        //  wait_for_object
        // ---------------------------------------------------------------

        private async Awaitable<StepResult> ExecuteWaitForObject(JsonObject args)
        {
            var objectName = args.GetString("name");
            var objectPath = args.GetString("path");

            if (string.IsNullOrEmpty(objectName) && string.IsNullOrEmpty(objectPath))
                return StepResult.Fail("wait_for_object requires 'name' or 'path' parameter");

            var state = args.GetString("state", "exists");
            var expectExists = !state.Equals("destroyed", StringComparison.OrdinalIgnoreCase);
            var timeoutMs = args.GetInt("timeout_ms", DefaultWaitForTimeoutMs);
            var pollMs = args.GetInt("poll_interval_ms", DefaultWaitForPollMs);

            var identifier = !string.IsNullOrEmpty(objectPath) ? objectPath : objectName;
            var startTime = DateTimeOffset.UtcNow;
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);

            while (DateTimeOffset.UtcNow - startTime < timeout)
            {
                var found = FindGameObject(objectName, objectPath);
                bool conditionMet = expectExists ? found != null : found == null;

                if (conditionMet)
                {
                    var elapsed = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                    return new StepResult
                    {
                        Success = true,
                        Action = "wait_for_object",
                        Details = $"{identifier} state={state} elapsed={elapsed}ms",
                        Message = expectExists
                            ? $"GameObject '{identifier}' found"
                            : $"GameObject '{identifier}' destroyed"
                    };
                }

                await Awaitable.WaitForSecondsAsync(pollMs / 1000f);
            }

            return StepResult.Fail(
                $"wait_for_object timed out after {timeoutMs}ms. Object: '{identifier}', expected: {state}");
        }

        private static GameObject FindGameObject(string name, string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                return GameObject.Find(path);
            }

            // 名前検索（非アクティブも含む）
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go.scene.isLoaded && go.name == name)
                    return go;
            }

            return null;
        }

        // ---------------------------------------------------------------
        //  capture
        // ---------------------------------------------------------------

        private StepResult ExecuteCapture(JsonObject args)
        {
            var filename = args.GetString("filename");
            var gameOnly = args.GetBool("game_only", false);

            string outputPath;
            if (!string.IsNullOrEmpty(filename))
            {
                var safeName = filename.Replace(".", "_").Replace(" ", "_").Replace("/", "_");
                var dir = "Temp/Screenshots";
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                outputPath = $"{dir}/{safeName}.png";
            }
            else
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var dir = "Temp/Screenshots";
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                outputPath = $"{dir}/auto_play_{timestamp}.png";
            }

            try
            {
                if (EditorApplication.isPlaying && gameOnly)
                {
                    if (!TryCaptureFromCamera(outputPath, out var w, out var h, out var error))
                        return StepResult.Fail($"Capture failed: {error}");

                    return new StepResult
                    {
                        Success = true,
                        Action = "capture",
                        Details = $"{w}x{h} -> {outputPath}",
                        Message = $"Screenshot saved: {outputPath}",
                        CapturePath = outputPath
                    };
                }

                // 非PlayMode or game_only=false: Game View ウィンドウキャプチャ
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameView == null)
                    return StepResult.Fail("Game View not found");

                gameView.Focus();
                gameView.Repaint();

                if (!TryCaptureWindowInternal(gameView, outputPath, out var width, out var height))
                    return StepResult.Fail("Window capture failed");

                return new StepResult
                {
                    Success = true,
                    Action = "capture",
                    Details = $"{width}x{height} -> {outputPath}",
                    Message = $"Screenshot saved: {outputPath}",
                    CapturePath = outputPath
                };
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"Capture error: {ex.Message}");
            }
        }

        private bool TryCaptureFromCamera(string outputPath, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;

            var camera = Camera.main;
            if (camera == null)
            {
                error = "No main camera found";
                return false;
            }

            RenderTexture rt = null;
            Texture2D texture = null;

            try
            {
                width = Screen.width > 0 ? Screen.width : 1920;
                height = Screen.height > 0 ? Screen.height : 1080;

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                var prevTarget = camera.targetTexture;
                camera.targetTexture = rt;
                camera.Render();
                camera.targetTexture = prevTarget;

                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = null;

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private bool TryCaptureWindowInternal(EditorWindow window, string outputPath, out int width, out int height)
        {
            width = 0;
            height = 0;

            RenderTexture rt = null;
            Texture2D texture = null;

            try
            {
                var parentField = typeof(EditorWindow).GetField("m_Parent",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (parentField == null) return false;

                var hostView = parentField.GetValue(window);
                if (hostView == null) return false;

                var grabPixelsMethod = hostView.GetType().GetMethod("GrabPixels",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new[] { typeof(RenderTexture), typeof(Rect) },
                    null);
                if (grabPixelsMethod == null) return false;

                var pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                var position = window.position;
                width = (int)(position.width * pixelsPerPoint);
                height = (int)(position.height * pixelsPerPoint);

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                grabPixelsMethod.Invoke(hostView, new object[] { rt, new Rect(0, 0, width, height) });

                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = null;

                File.WriteAllBytes(outputPath, texture.EncodeToPNG());
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        // ---------------------------------------------------------------
        //  Log collection
        // ---------------------------------------------------------------

        public class WaitResult
        {
            public int WaitedMs;
            public List<LogEntryCompact> Logs;
            public int LogCount;
        }

        /// <summary>指定時間待機し、その間のログを収集して返す</summary>
        public async Awaitable<WaitResult> WaitAndCollectLogsAsync(
            int waitMs, string logFilter, int logLimit)
        {
            var sinceTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await Awaitable.WaitForSecondsAsync(waitMs / 1000f);

            var rawLogs = ConsoleLogCapture.instance.GetLogsFiltered(new LogFilterOptions
            {
                TypeFilter = logFilter ?? "all",
                Since = sinceTs,
                Limit = logLimit
            });

            return new WaitResult
            {
                WaitedMs = waitMs,
                Logs = ToCompactLogs(rawLogs),
                LogCount = rawLogs.Count
            };
        }

        // ---------------------------------------------------------------
        //  Core action execution (input simulation)
        // ---------------------------------------------------------------

        private StepResult ExecuteActionCore(JsonObject args)
        {
            var action = args.GetString("action");
            if (string.IsNullOrEmpty(action))
                return StepResult.Fail("Parameter 'action' is required");

            if (!EditorApplication.isPlaying)
                return StepResult.Fail("Requires play mode. Use control-playmode to start play mode first.");

            var actionLower = action.ToLowerInvariant();

            try
            {
                if (actionLower == "wait")
                {
                    var waitMs = GetRequestedWaitMs(args, actionLower);
                    return new StepResult
                    {
                        Success = true,
                        Action = "wait",
                        Details = waitMs + "ms",
                        Message = $"Waiting {waitMs}ms (game continues running)",
                        SimulatorType = "none"
                    };
                }

                // EventSystem 系アクション
                if (actionLower is "tap_ui" or "input_text")
                    return ExecuteUiAction(args, actionLower);

                InputSimulationResult simResult;
                if (IsMouseAction(actionLower))
                    simResult = ExecuteMouseAction(args, actionLower);
                else
                    simResult = ExecuteKeyboardAction(args, actionLower);

                if (!simResult.Success)
                    return StepResult.Fail(simResult.Error);

                var key = args.GetString("key");
                var logMsg = key != null
                    ? $"[UniForge] simulate-input: {action} {key}"
                    : $"[UniForge] simulate-input: {action}";
                var durationMs = args.GetNullableInt("duration_ms");
                if (durationMs.HasValue)
                    logMsg += $" ({durationMs}ms)";
                Debug.Log(logMsg);

                return new StepResult
                {
                    Success = true,
                    Action = simResult.Action,
                    Details = simResult.Details,
                    Message = simResult.Message,
                    SimulatorType = simResult.SimulatorType,
#if ENABLE_INPUT_SYSTEM
                    hit_ui = GetPrimaryUiHit(args, actionLower),
                    ui_hits = GetUiHits(args, actionLower)
#endif
                };
            }
            catch (Exception ex)
            {
                return StepResult.Fail($"Failed to simulate input: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------
        //  Keyboard
        // ---------------------------------------------------------------

        private InputSimulationResult ExecuteKeyboardAction(JsonObject args, string action)
        {
            var simulator = GetKeyboardSimulator();
            if (simulator == null)
                return InputSimulationResult.Fail("No input simulator available for keyboard simulation.");
            if (!simulator.IsAvailable)
                return InputSimulationResult.Fail($"Input simulator '{simulator.Name}' is not available.");

            var key = args.GetString("key");
            var durationMs = args.GetNullableInt("duration_ms") ?? 100;

            simulator.FocusApplication();

            return action switch
            {
                "key_down" => simulator.KeyDown(key),
                "key_up" => simulator.KeyUp(key),
                "key_press" => simulator.KeyPress(key, durationMs),
                _ => InputSimulationResult.Fail($"Invalid keyboard action: {action}. Valid actions: key_down, key_up, key_press")
            };
        }

        private IInputSimulator GetKeyboardSimulator()
        {
            if (_keyboardSimulator != null)
                return _keyboardSimulator;

            _keyboardSimulator = CreateKeyboardSimulator();
            return _keyboardSimulator;
        }

        private static IInputSimulator CreateKeyboardSimulator()
        {
#if ENABLE_INPUT_SYSTEM
            var inputSystemSimulator = new InputSystemSimulator();
            if (inputSystemSimulator.IsAvailable)
                return inputSystemSimulator;
#endif

#if UNITY_EDITOR_OSX
            return new MacOSInputSimulator();
#elif UNITY_EDITOR_WIN
            return new WindowsInputSimulator();
#else
            return null;
#endif
        }

        // ---------------------------------------------------------------
        //  Mouse
        // ---------------------------------------------------------------

        private InputSimulationResult ExecuteMouseAction(JsonObject args, string action)
        {
#if ENABLE_INPUT_SYSTEM
            var simulator = GetInputSystemSimulator();
            if (simulator == null)
                return InputSimulationResult.Fail("Mouse simulation requires the Input System package, but no mouse device is available.");

            simulator.FocusApplication();

            var coordinate = args.GetString("coordinate") ?? "screen";
            bool isWorld = coordinate.Equals("world", StringComparison.OrdinalIgnoreCase);

            return action switch
            {
                "tap" => ExecuteTap(args, simulator, isWorld),
                "drag" => ExecuteDrag(args, simulator, isWorld),
                "long_press" => ExecuteLongPress(args, simulator, isWorld),
                _ => ExecuteLowLevelMouseAction(args, action, simulator, isWorld)
            };
#else
            return InputSimulationResult.Fail(
                "Mouse simulation requires the Unity Input System package (com.unity.inputsystem). " +
                "Install it using the package-manager tool: action='add', package_id='com.unity.inputsystem'");
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private InputSystemSimulator GetInputSystemSimulator()
        {
            if (_inputSystemSimulator != null && _inputSystemSimulator.IsAvailable)
                return _inputSystemSimulator;

            _inputSystemSimulator = new InputSystemSimulator();
            return _inputSystemSimulator.IsAvailable ? _inputSystemSimulator : null;
        }

        private static InputSimulationResult ExecuteLowLevelMouseAction(
            JsonObject args, string action, InputSystemSimulator simulator, bool isWorld)
        {
            float? x = args.HasKey("x") ? args.GetFloat("x") : null;
            float? y = args.HasKey("y") ? args.GetFloat("y") : null;
            float? scrollDelta = args.HasKey("scroll_delta") ? args.GetFloat("scroll_delta") : null;
            var btn = args.GetNullableInt("button") ?? 0;

            if (isWorld && x.HasValue && y.HasValue)
            {
                var screenPos = WorldToUnityScreen(x.Value, y.Value);
                if (screenPos == null)
                    return InputSimulationResult.Fail("Cannot convert world coordinates: no active camera found");
                x = screenPos.Value.x;
                y = screenPos.Value.y;
            }

            return action switch
            {
                "mouse_down" => simulator.MouseDown(btn, x, y),
                "mouse_up" => simulator.MouseUp(btn, x, y),
                "mouse_click" => simulator.MouseClick(btn, x, y),
                "mouse_move" => (x.HasValue && y.HasValue)
                    ? simulator.MouseMove(x.Value, y.Value)
                    : InputSimulationResult.Fail("Parameters 'x' and 'y' are required for mouse_move action"),
                "mouse_scroll" => simulator.MouseScroll(scrollDelta ?? 0),
                _ => InputSimulationResult.Fail($"Invalid mouse action: {action}")
            };
        }

        private static InputSimulationResult ExecuteTap(JsonObject args, InputSystemSimulator simulator, bool isWorld)
        {
            var screenPos = ResolveScreenPosition(args, isWorld);
            if (screenPos == null)
                return InputSimulationResult.Fail("tap requires 'position' [x,y] or 'x'/'y' parameters");

            var btn = args.GetNullableInt("button") ?? 0;
            var pos = screenPos.Value;
            return simulator.MouseClick(btn, pos.x, pos.y);
        }

        private static InputSimulationResult ExecuteDrag(JsonObject args, InputSystemSimulator simulator, bool isWorld)
        {
            var fromArr = args.GetFloatArray("from");
            var toArr = args.GetFloatArray("to");

            if (fromArr == null || fromArr.Length < 2)
                return InputSimulationResult.Fail("drag requires 'from' [x,y] parameter");
            if (toArr == null || toArr.Length < 2)
                return InputSimulationResult.Fail("drag requires 'to' [x,y] parameter");

            var fromScreen = ResolveArrayToScreen(fromArr, isWorld);
            var toScreen = ResolveArrayToScreen(toArr, isWorld);

            if (fromScreen == null || toScreen == null)
                return InputSimulationResult.Fail("Cannot convert world coordinates: no active camera found");

            var durationMs = args.GetNullableInt("duration_ms") ?? 500;
            var btn = args.GetNullableInt("button") ?? 0;
            var from = fromScreen.Value;
            var to = toScreen.Value;

            const int steps = 20;
            float stepInterval = durationMs / 1000f / steps;

            simulator.MouseMove(from.x, from.y);

            EditorApplication.delayCall += () =>
            {
                simulator.MouseDown(btn, from.x, from.y);

                int currentStep = 0;
                float lastStepTime = (float)EditorApplication.timeSinceStartup;
                float timeoutTime = lastStepTime + durationMs / 1000f + 5f;

                EditorApplication.CallbackFunction updateCallback = null;
                updateCallback = () =>
                {
                    float now = (float)EditorApplication.timeSinceStartup;

                    if (now > timeoutTime)
                    {
                        EditorApplication.update -= updateCallback;
                        return;
                    }

                    if (now - lastStepTime < stepInterval) return;
                    lastStepTime = now;
                    currentStep++;

                    if (currentStep <= steps)
                    {
                        float t = (float)currentStep / steps;
                        float ix = Mathf.Lerp(from.x, to.x, t);
                        float iy = Mathf.Lerp(from.y, to.y, t);
                        simulator.MouseDragMove(btn, ix, iy);
                    }
                    else
                    {
                        simulator.MouseUp(btn, to.x, to.y);
                        EditorApplication.update -= updateCallback;
                    }
                };
                EditorApplication.update += updateCallback;
            };

            return InputSimulationResult.Ok(
                "drag",
                $"from=({fromArr[0]},{fromArr[1]}) to=({toArr[0]},{toArr[1]}) duration={durationMs}ms coord={(isWorld ? "world" : "screen")}",
                $"Drag started ({steps} steps over {durationMs}ms)",
                simulator.Name
            );
        }

        private static InputSimulationResult ExecuteLongPress(JsonObject args, InputSystemSimulator simulator, bool isWorld)
        {
            var screenPos = ResolveScreenPosition(args, isWorld);
            if (screenPos == null)
                return InputSimulationResult.Fail("long_press requires 'position' [x,y] or 'x'/'y' parameters");

            var durationMs = args.GetNullableInt("duration_ms") ?? 500;
            var btn = args.GetNullableInt("button") ?? 0;
            var pos = screenPos.Value;

            simulator.MouseMove(pos.x, pos.y);
            simulator.MouseDown(btn, pos.x, pos.y);

            float pressEndTime = (float)EditorApplication.timeSinceStartup + durationMs / 1000f;
            float pressTimeoutTime = pressEndTime + 5f;
            EditorApplication.CallbackFunction releaseCallback = null;
            releaseCallback = () =>
            {
                float now = (float)EditorApplication.timeSinceStartup;

                if (now > pressTimeoutTime)
                {
                    EditorApplication.update -= releaseCallback;
                    return;
                }

                if (now >= pressEndTime)
                {
                    simulator.MouseUp(btn, pos.x, pos.y);
                    EditorApplication.update -= releaseCallback;
                }
            };
            EditorApplication.update += releaseCallback;

            return InputSimulationResult.Ok(
                "long_press",
                $"screen_pos=({pos.x:F1},{pos.y:F1}) duration={durationMs}ms",
                $"Long press started ({durationMs}ms)",
                simulator.Name
            );
        }
#endif

        // ---------------------------------------------------------------
        //  Coordinate resolution / UI hit detection (Input System 非依存)
        // ---------------------------------------------------------------

        private static Vector2? WorldToUnityScreen(float worldX, float worldY)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var screenPos = cam.WorldToScreenPoint(new Vector3(worldX, worldY, 0));
            return new Vector2(screenPos.x, screenPos.y);
        }

        private static Vector2? ResolveArrayToScreen(float[] pos, bool isWorld)
        {
            if (pos == null || pos.Length < 2) return null;
            if (isWorld)
                return WorldToUnityScreen(pos[0], pos[1]);
            return new Vector2(pos[0], pos[1]);
        }

        private static Vector2? ResolveScreenPosition(JsonObject args, bool isWorld)
        {
            var posArr = args.GetFloatArray("position");
            if (posArr != null && posArr.Length >= 2)
                return ResolveArrayToScreen(posArr, isWorld);

            float? px = args.HasKey("x") ? args.GetFloat("x") : null;
            float? py = args.HasKey("y") ? args.GetFloat("y") : null;
            if (px.HasValue && py.HasValue)
            {
                if (isWorld)
                    return WorldToUnityScreen(px.Value, py.Value);
                return new Vector2(px.Value, py.Value);
            }

            return null;
        }

        private static UiHitCompact GetPrimaryUiHit(JsonObject args, string action)
        {
            var hits = GetUiHits(args, action);
            return hits != null && hits.Count > 0 ? hits[0] : null;
        }

        private static List<UiHitCompact> GetUiHits(JsonObject args, string action)
        {
            var pointerPosition = ResolveUiHitPosition(args, action);
            if (pointerPosition == null || EventSystem.current == null)
                return null;

            var eventData = new PointerEventData(EventSystem.current)
            {
                position = pointerPosition.Value
            };

            var raycastResults = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, raycastResults);

            if (raycastResults.Count == 0)
                return new List<UiHitCompact>();

            var hits = new List<UiHitCompact>(raycastResults.Count);
            foreach (var raycastResult in raycastResults)
            {
                var go = raycastResult.gameObject;
                if (go == null)
                    continue;

                hits.Add(BuildUiHitCompact(go, raycastResult));
            }

            return hits;
        }

        private static Vector2? ResolveUiHitPosition(JsonObject args, string action)
        {
            var coordinate = args.GetString("coordinate") ?? "screen";
            var isWorld = coordinate.Equals("world", StringComparison.OrdinalIgnoreCase);

            return action switch
            {
                "tap" or "long_press" => ResolveScreenPosition(args, isWorld),
                "drag" => ResolveArrayToScreen(args.GetFloatArray("to"), isWorld),
                "mouse_down" or "mouse_up" or "mouse_click" or "mouse_move" => ResolveScreenPosition(args, isWorld),
                _ => null
            };
        }

        // ---------------------------------------------------------------
        //  EventSystem UI actions
        // ---------------------------------------------------------------

        /// <summary>
        /// UI 要素をパス/名前で解決し、アクションを実行する。
        /// </summary>
        private StepResult ExecuteUiAction(JsonObject args, string action)
        {
            return action switch
            {
                "tap_ui" => ExecuteTapUi(args),
                "input_text" => ExecuteInputText(args),
                _ => StepResult.Fail($"Unknown UI action: {action}")
            };
        }

        /// <summary>
        /// パス/名前指定で UI 要素をタップする。
        /// EventSystem に直接イベントを配送し、Unity Editor のアクティブ化や物理カーソル移動を避ける。
        /// </summary>
        private StepResult ExecuteTapUi(JsonObject args)
        {
            var resolveResult = ResolveUiGameObject(args);
            if (!resolveResult.Success)
                return StepResult.Fail(resolveResult.Error);

            var go = resolveResult.GameObject;
            var rectTransform = go.GetComponent<RectTransform>();
            if (rectTransform == null)
                return StepResult.Fail($"GameObject '{GameObjectResolver.GetHierarchyPath(go)}' has no RectTransform");

            var selectable = go.GetComponent<Selectable>();
            if (selectable != null && !selectable.interactable)
                return StepResult.Fail($"UI element '{GameObjectResolver.GetHierarchyPath(go)}' is not interactable");

            if (!go.activeInHierarchy)
                return StepResult.Fail($"UI element '{GameObjectResolver.GetHierarchyPath(go)}' is not active");

            var screenPos = GetRectTransformScreenCenter(rectTransform);
            if (screenPos == null)
                return StepResult.Fail($"Cannot resolve screen position for '{GameObjectResolver.GetHierarchyPath(go)}'");

            var pos = screenPos.Value;
            if (!TryExecuteUiClick(go, pos, out var clickError))
                return StepResult.Fail(clickError);

            var goPath = GameObjectResolver.GetHierarchyPath(go);
            return new StepResult
            {
                Success = true,
                Action = "tap_ui",
                Details = $"path='{goPath}' screen=({pos.x:F1},{pos.y:F1})",
                Message = $"Tapped UI element: {goPath}",
                SimulatorType = "EventSystem",
                hit_ui = BuildUiHitFromGameObject(go),
                ui_hits = new List<UiHitCompact> { BuildUiHitFromGameObject(go) }
            };
        }

        internal static bool TryExecuteUiClick(GameObject target, Vector2 screenPosition, out string error)
        {
            return TryExecuteUiClick(target, screenPosition, EventSystem.current, out error);
        }

        internal static bool TryExecuteUiClick(
            GameObject target,
            Vector2 screenPosition,
            EventSystem eventSystem,
            out string error)
        {
            if (eventSystem == null)
            {
                error = "Cannot tap UI element because no active EventSystem exists in the scene";
                return false;
            }

            var eventTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
            if (eventTarget == null)
            {
                error = $"UI element '{GameObjectResolver.GetHierarchyPath(target)}' has no pointer click handler";
                return false;
            }

            var eventData = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = screenPosition,
                pointerId = -1,
                pointerPress = eventTarget,
                rawPointerPress = target,
                eligibleForClick = true,
                clickCount = 1,
                clickTime = Time.unscaledTime
            };

            ExecuteEvents.Execute(eventTarget, eventData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(eventTarget, eventData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(eventTarget, eventData, ExecuteEvents.pointerClickHandler);

            error = null;
            return true;
        }

        /// <summary>
        /// InputField / TMP_InputField にテキストを入力する。
        /// </summary>
        private static StepResult ExecuteInputText(JsonObject args)
        {
            var resolveResult = ResolveUiGameObject(args);
            if (!resolveResult.Success)
                return StepResult.Fail(resolveResult.Error);

            var go = resolveResult.GameObject;
            var text = args.GetString("text");
            if (text == null)
                return StepResult.Fail("Parameter 'text' is required for input_text action");

            var append = args.GetBool("append", false);
            var goPath = GameObjectResolver.GetHierarchyPath(go);

            // TMP_InputField を優先して検索
            var tmpInputField = FindTMPInputField(go);
            if (tmpInputField != null)
            {
                if (append)
                    SetTMPInputFieldText(tmpInputField, GetTMPInputFieldText(tmpInputField) + text);
                else
                    SetTMPInputFieldText(tmpInputField, text);

                return new StepResult
                {
                    Success = true,
                    Action = "input_text",
                    Details = $"path='{goPath}' text='{TruncateForDisplay(text, 50)}' type=TMP_InputField",
                    Message = $"Set text on {goPath}",
                    hit_ui = BuildUiHitFromGameObject(go)
                };
            }

            // 標準 InputField
            var inputField = go.GetComponent<InputField>();
            if (inputField != null)
            {
                if (append)
                    inputField.text += text;
                else
                    inputField.text = text;

                // onValueChanged / onEndEdit を発火
                inputField.onValueChanged.Invoke(inputField.text);

                return new StepResult
                {
                    Success = true,
                    Action = "input_text",
                    Details = $"path='{goPath}' text='{TruncateForDisplay(text, 50)}' type=InputField",
                    Message = $"Set text on {goPath}",
                    hit_ui = BuildUiHitFromGameObject(go)
                };
            }

            return StepResult.Fail(
                $"GameObject '{goPath}' has no InputField or TMP_InputField component");
        }

        // ---------------------------------------------------------------
        //  wait_for_ui_state
        // ---------------------------------------------------------------

        private async Awaitable<StepResult> ExecuteWaitForUiState(JsonObject args)
        {
            var resolveResult = ResolveUiGameObject(args);
            if (!resolveResult.Success)
                return StepResult.Fail(resolveResult.Error);

            var go = resolveResult.GameObject;
            var condition = args.GetString("condition");
            if (string.IsNullOrEmpty(condition))
                return StepResult.Fail("wait_for_ui_state requires 'condition' parameter");

            var timeoutMs = args.GetInt("timeout_ms", DefaultWaitForTimeoutMs);
            var pollMs = args.GetInt("poll_interval_ms", DefaultWaitForPollMs);
            var goPath = GameObjectResolver.GetHierarchyPath(go);

            var startTime = DateTimeOffset.UtcNow;
            var timeout = TimeSpan.FromMilliseconds(timeoutMs);

            while (DateTimeOffset.UtcNow - startTime < timeout)
            {
                var (met, detail) = EvaluateUiCondition(go, condition, args);
                if (met)
                {
                    var elapsed = (int)(DateTimeOffset.UtcNow - startTime).TotalMilliseconds;
                    return new StepResult
                    {
                        Success = true,
                        Action = "wait_for_ui_state",
                        Details = $"{goPath} condition={condition} elapsed={elapsed}ms",
                        Message = $"UI condition met: {detail}",
                        hit_ui = BuildUiHitFromGameObject(go)
                    };
                }

                await Awaitable.WaitForSecondsAsync(pollMs / 1000f);

                // GameObject が破棄された場合のハンドリング
                if (go == null)
                    return StepResult.Fail($"GameObject '{goPath}' was destroyed while waiting");
            }

            var (_, currentDetail) = EvaluateUiCondition(go, condition, args);
            return StepResult.Fail(
                $"wait_for_ui_state timed out after {timeoutMs}ms. " +
                $"Path: '{goPath}', condition: '{condition}', current: {currentDetail}");
        }

        /// <summary>
        /// UI 条件を評価する。
        /// </summary>
        /// <returns>(条件を満たしたか, 現在の状態の説明)</returns>
        private static (bool met, string detail) EvaluateUiCondition(GameObject go, string condition, JsonObject args)
        {
            switch (condition)
            {
                case "interactable":
                {
                    var expected = args.GetBool("value", true);
                    var selectable = go.GetComponent<Selectable>();
                    if (selectable == null)
                        return (false, "no Selectable component");
                    var current = selectable.interactable;
                    return (current == expected, $"interactable={current}");
                }

                case "active":
                {
                    var expected = args.GetBool("value", true);
                    var current = go.activeInHierarchy;
                    return (current == expected, $"active={current}");
                }

                case "text_equals":
                {
                    var expected = args.GetString("value", "");
                    var currentText = GetUiText(go);
                    if (currentText == null)
                        return (false, "no Text/TMP component");
                    return (currentText == expected, $"text='{TruncateForDisplay(currentText, 50)}'");
                }

                case "text_contains":
                {
                    var expected = args.GetString("value", "");
                    var currentText = GetUiText(go);
                    if (currentText == null)
                        return (false, "no Text/TMP component");
                    var contains = currentText.IndexOf(expected, StringComparison.Ordinal) >= 0;
                    return (contains, $"text='{TruncateForDisplay(currentText, 50)}'");
                }

                case "toggle_on":
                {
                    var expected = args.GetBool("value", true);
                    var toggle = go.GetComponent<Toggle>();
                    if (toggle == null)
                        return (false, "no Toggle component");
                    return (toggle.isOn == expected, $"toggle={toggle.isOn}");
                }

                case "slider_value":
                {
                    var expectedMin = args.HasKey("min") ? (float?)args.GetFloat("min") : null;
                    var expectedMax = args.HasKey("max") ? (float?)args.GetFloat("max") : null;
                    var expectedValue = args.HasKey("value") ? (float?)args.GetFloat("value") : null;
                    var slider = go.GetComponent<Slider>();
                    if (slider == null)
                        return (false, "no Slider component");

                    var v = slider.value;
                    bool met = true;
                    if (expectedValue.HasValue)
                        met = Mathf.Approximately(v, expectedValue.Value);
                    if (expectedMin.HasValue && v < expectedMin.Value)
                        met = false;
                    if (expectedMax.HasValue && v > expectedMax.Value)
                        met = false;
                    return (met, $"slider={v:F3}");
                }

                default:
                    return (false, $"unknown condition: {condition}");
            }
        }

        // ---------------------------------------------------------------
        //  UI helper methods
        // ---------------------------------------------------------------

        /// <summary>
        /// args から path / name / instance_id で GameObject を解決する。
        /// </summary>
        private static GameObjectResolver.Result ResolveUiGameObject(JsonObject args)
        {
            var path = args.GetString("path");
            var name = args.GetString("name");
            int? instanceId = args.HasKey("instance_id") ? (int?)args.GetInt("instance_id") : null;

            // path が無ければ name を path として扱う
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(name))
                path = name;

            if (string.IsNullOrEmpty(path) && !instanceId.HasValue)
                return GameObjectResolver.Result.Fail("Parameter 'path', 'name', or 'instance_id' is required");

            return GameObjectResolver.Resolve(path, instanceId);
        }

        /// <summary>
        /// RectTransform の中心をスクリーン座標で返す。
        /// </summary>
        private static Vector2? GetRectTransformScreenCenter(RectTransform rectTransform)
        {
            var canvas = rectTransform.GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            // Canvas の RenderMode に応じてカメラを決定
            Camera cam = null;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera ?? Camera.main;

            // RectTransform のワールド座標の四隅からスクリーン座標を算出
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);

            var center = (corners[0] + corners[2]) / 2f;

            if (cam != null)
            {
                var screenPoint = cam.WorldToScreenPoint(center);
                return new Vector2(screenPoint.x, screenPoint.y);
            }

            // ScreenSpaceOverlay: ワールド座標 ≒ スクリーン座標
            return new Vector2(center.x, center.y);
        }

        /// <summary>
        /// GameObject から UI テキスト内容を取得する（Text, TMP_Text 対応）。
        /// </summary>
        private static string GetUiText(GameObject go)
        {
            // 標準 Text
            var text = go.GetComponent<Text>();
            if (text != null) return text.text;

            // TMP_Text（リフレクションで型非依存）
            var tmpText = FindTMPTextComponent(go);
            if (tmpText != null)
                return GetTMPText(tmpText);

            // InputField のテキスト
            var inputField = go.GetComponent<InputField>();
            if (inputField != null) return inputField.text;

            // TMP_InputField のテキスト
            var tmpInputField = FindTMPInputField(go);
            if (tmpInputField != null)
                return GetTMPInputFieldText(tmpInputField);

            return null;
        }

        /// <summary>
        /// UiHitCompact を RaycastResult 付きで構築する。
        /// </summary>
        private static UiHitCompact BuildUiHitCompact(GameObject go, RaycastResult raycastResult)
        {
            var hit = BuildUiHitFromGameObject(go);
            hit.module = raycastResult.module != null ? raycastResult.module.GetType().Name : null;
            hit.depth = raycastResult.depth;
            hit.sorting_order = raycastResult.sortingOrder;
            return hit;
        }

        /// <summary>
        /// GameObject から UiHitCompact を構築する（RaycastResult なし）。
        /// </summary>
        private static UiHitCompact BuildUiHitFromGameObject(GameObject go)
        {
            var selectable = go.GetComponent<Selectable>();
            return new UiHitCompact
            {
                name = go.name,
                path = GameObjectResolver.GetHierarchyPath(go),
                instance_id = go.GetInstanceID(),
                component_type = GetSelectableTypeName(selectable),
                interactable = selectable != null ? selectable.interactable : null,
                text = GetUiText(go)
            };
        }

        /// <summary>
        /// Selectable の具体的な型名を返す。
        /// </summary>
        private static string GetSelectableTypeName(Selectable selectable)
        {
            if (selectable == null) return null;
            // 具体的な派生型名を返す (Button, Toggle, Slider, InputField, Dropdown, Scrollbar, etc.)
            return selectable.GetType().Name;
        }

        // ---------------------------------------------------------------
        //  TMP helpers (reflection-based to avoid hard dependency)
        // ---------------------------------------------------------------

        private static Type _tmpTextType;
        private static Type _tmpInputFieldType;
        private static bool _tmpTypesResolved;

        private static void ResolveTMPTypes()
        {
            if (_tmpTypesResolved) return;
            _tmpTypesResolved = true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_tmpTextType == null)
                {
                    _tmpTextType = assembly.GetType("TMPro.TMP_Text");
                }
                if (_tmpInputFieldType == null)
                {
                    _tmpInputFieldType = assembly.GetType("TMPro.TMP_InputField");
                }
                if (_tmpTextType != null && _tmpInputFieldType != null) break;
            }
        }

        private static Component FindTMPTextComponent(GameObject go)
        {
            ResolveTMPTypes();
            if (_tmpTextType == null) return null;
            return go.GetComponent(_tmpTextType);
        }

        private static string GetTMPText(Component tmpComponent)
        {
            var prop = tmpComponent.GetType().GetProperty("text");
            return prop?.GetValue(tmpComponent) as string;
        }

        private static Component FindTMPInputField(GameObject go)
        {
            ResolveTMPTypes();
            if (_tmpInputFieldType == null) return null;
            return go.GetComponent(_tmpInputFieldType);
        }

        private static string GetTMPInputFieldText(Component tmpInputField)
        {
            var prop = tmpInputField.GetType().GetProperty("text");
            return prop?.GetValue(tmpInputField) as string;
        }

        private static void SetTMPInputFieldText(Component tmpInputField, string text)
        {
            var prop = tmpInputField.GetType().GetProperty("text");
            prop?.SetValue(tmpInputField, text);

            // onValueChanged を発火
            var onValueChanged = tmpInputField.GetType().GetField("onValueChanged");
            if (onValueChanged != null)
            {
                var eventObj = onValueChanged.GetValue(tmpInputField);
                var invokeMethod = eventObj?.GetType().GetMethod("Invoke", new[] { typeof(string) });
                invokeMethod?.Invoke(eventObj, new object[] { text });
            }
        }

        private static string TruncateForDisplay(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------

        private static bool IsMouseAction(string action)
        {
            return action is "mouse_down" or "mouse_up" or "mouse_click"
                or "mouse_move" or "mouse_scroll" or "tap" or "drag" or "long_press";
        }

        private static int GetRequestedWaitMs(JsonObject args, string action)
        {
            if (action == "wait")
            {
                return args.GetNullableInt("ms")
                    ?? args.GetNullableInt("duration_ms")
                    ?? args.GetNullableInt("wait_ms")
                    ?? 1000;
            }

            return args.GetNullableInt("wait_ms") ?? 0;
        }

        private static List<LogEntryCompact> ToCompactLogs(List<LogEntry> logs)
        {
            var compact = new List<LogEntryCompact>(logs.Count);
            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.message)) continue;
                compact.Add(new LogEntryCompact
                {
                    message = log.message,
                    type = log.type,
                    timestamp = log.timestamp
                });
            }
            return compact;
        }

        private static string FormatStepLabel(int index, string action, JsonObject args, StepResult result)
        {
            var suffix = result.Success ? "ok" : $"FAIL: {result.Error}";

            switch (action)
            {
                case "key_press":
                case "key_down":
                case "key_up":
                    var key = args.GetString("key");
                    return !string.IsNullOrEmpty(key)
                        ? $"step_{index}: {action} {key} - {suffix}"
                        : $"step_{index}: {action} - {suffix}";

                case "wait_for_log":
                    var pattern = args.GetString("pattern");
                    return $"step_{index}: {action} '{pattern}' - {suffix}";

                case "wait_for_object":
                    var obj = args.GetString("path") ?? args.GetString("name");
                    var state = args.GetString("state", "exists");
                    return $"step_{index}: {action} '{obj}' {state} - {suffix}";

                case "wait_for_ui_state":
                    var uiPath = args.GetString("path") ?? args.GetString("name");
                    var cond = args.GetString("condition");
                    return $"step_{index}: {action} '{uiPath}' {cond} - {suffix}";

                case "tap_ui":
                    var tapPath = args.GetString("path") ?? args.GetString("name");
                    return $"step_{index}: {action} '{tapPath}' - {suffix}";

                case "input_text":
                    var inputPath = args.GetString("path") ?? args.GetString("name");
                    var inputText = args.GetString("text");
                    return $"step_{index}: {action} '{inputPath}' text='{TruncateForDisplay(inputText, 20)}' - {suffix}";

                case "capture":
                    return $"step_{index}: {action} -> {result.CapturePath ?? "?"} - {suffix}";

                default:
                    return $"step_{index}: {action} - {suffix}";
            }
        }
    }
}
