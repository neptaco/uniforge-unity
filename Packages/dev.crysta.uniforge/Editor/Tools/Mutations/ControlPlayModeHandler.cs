using UnityEditor;
using UniForge.Tools.Queries;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Play モードを制御するツール
    /// </summary>
    [Tool("control-playmode",
        Description = "Control Unity Editor play mode (play, pause, stop, step)",
        Title = "Control Play Mode",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public partial class ControlPlayModeHandler : DomainReloadResumableMutationHandler<ControlPlayModeHandler.DomainReloadState>
    {
        private const int DomainReloadWaitTimeoutMs = 30000;
        private const int LogSnapshotLimit = 200;
        private const int StatePollIntervalMs = 250;

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<ControlPlayModeHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var action = args.GetString("action");
            var waitForTransition = args.GetBool("wait", false);
            var waitForLog = ParseWaitForLog(args.GetDictionary("wait_for_log"));

            if (string.IsNullOrEmpty(action))
            {
                return ToolResult.Fail("Parameter 'action' is required");
            }

            var previousState = GetCurrentState();

            switch (action.ToLowerInvariant())
            {
                case "play":
                    if (EditorApplication.isPlaying)
                    {
                        var alreadyPlayingResult = new Output
                        {
                            success = true,
                            action = action,
                            previous_state = previousState,
                            current_state = previousState,
                            message = "Already in play mode",
                            editor_state = GetEditorStateHandler.CaptureState()
                        };

                        if (waitForLog != null)
                        {
                            return CreateDomainReloadWaitResult(
                                previousState,
                                alreadyPlayingResult,
                                waitForLog);
                        }

                        return ToolResult.Complete(alreadyPlayingResult);
                    }

                    // コンパイル状態チェック
                    if (EditorApplication.isCompiling)
                    {
                        return ToolResult.Fail("Cannot enter play mode while compiling");
                    }

                    // コンパイルエラーチェック
                    // success=false かつ errors>0 で判定。警告のみ(success=false, errors=0)の場合は
                    // PlayMode を許可する（Unity の標準動作に合わせる）
                    var compileStatus = CompilationWatcher.Instance.GetStatus();
                    if (!compileStatus.success && compileStatus.errors.Count > 0)
                    {
                        var firstError = compileStatus.errors[0];
                        return ToolResult.Fail(
                            $"Cannot enter play mode: compilation has {compileStatus.errors.Count} error(s). " +
                            $"First error: {firstError.message}");
                    }

                    // EditorApplication.update 経由で次フレームに実行
                    // レスポンスを先に返してから PlayMode に入る
                    void EnterPlay()
                    {
                        EditorApplication.update -= EnterPlay;
                        EditorApplication.isPlaying = true;
                    }
                    EditorApplication.update += EnterPlay;

                    var playResult = new Output
                    {
                        success = true,
                        action = action,
                        previous_state = previousState,
                        current_state = "starting",
                        message = "Play mode starting.",
                        editor_state = GetEditorStateHandler.CaptureState()
                    };

                    if (waitForTransition || waitForLog != null)
                    {
                        return CreateDomainReloadWaitResult("playing", playResult, waitForLog);
                    }

                    return ToolResult.Complete(playResult);

                case "pause":
                    if (!EditorApplication.isPlaying)
                    {
                        return ToolResult.Fail("Cannot pause when not in play mode");
                    }
                    EditorApplication.isPaused = true;
                    break;

                case "resume":
                    if (!EditorApplication.isPlaying)
                    {
                        return ToolResult.Fail("Cannot resume when not in play mode");
                    }
                    EditorApplication.isPaused = false;
                    break;

                case "stop":
                    if (!EditorApplication.isPlaying)
                    {
                        var alreadyStoppedResult = new Output
                        {
                            success = true,
                            action = action,
                            previous_state = previousState,
                            current_state = previousState,
                            message = "Already in edit mode",
                            editor_state = GetEditorStateHandler.CaptureState()
                        };

                        if (waitForLog != null)
                        {
                            return CreateDomainReloadWaitResult(
                                previousState,
                                alreadyStoppedResult,
                                waitForLog);
                        }

                        return ToolResult.Complete(alreadyStoppedResult);
                    }

                    // プレイモード終了も遅延実行し、レスポンスを先に返す
                    // Domain Reload が発生するとブリッジが再初期化されるため
                    EditorApplication.delayCall += () => EditorApplication.isPlaying = false;

                    var stopResult = new Output
                    {
                        success = true,
                        action = action,
                        previous_state = previousState,
                        current_state = "stopping",
                        message = "Play mode stopping.",
                        editor_state = GetEditorStateHandler.CaptureState()
                    };

                    if (waitForTransition || waitForLog != null)
                    {
                        return CreateDomainReloadWaitResult("edit", stopResult, waitForLog);
                    }

                    return ToolResult.Complete(stopResult);

                case "step":
                    if (!EditorApplication.isPlaying)
                    {
                        return ToolResult.Fail("Cannot step when not in play mode");
                    }
                    EditorApplication.Step();
                    break;

                default:
                    return ToolResult.Fail($"Invalid action: {action}. Valid actions: play, pause, resume, stop, step");
            }

            var currentState = GetCurrentState();

            return ToolResult.Complete(new Output
            {
                success = true,
                action = action,
                previous_state = previousState,
                current_state = currentState,
                message = $"Play mode action '{action}' executed: {previousState} -> {currentState}",
                editor_state = GetEditorStateHandler.CaptureState()
            });
        }
    }
}
