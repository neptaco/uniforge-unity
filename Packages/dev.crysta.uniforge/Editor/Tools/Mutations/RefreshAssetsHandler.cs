using System.Collections.Generic;
using UnityEditor;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// refresh ツールの出力
    /// </summary>
    public class RefreshAssetsOutput
    {
        public bool success;
        public string message;
        public string warning;
        public bool compile_started;
        public bool is_compiling_after_refresh;
        public bool waited_for_reload;
    }

    [System.Serializable]
    public class RefreshCompilationWaitAck
    {
        public bool success;
        public string message;
        public string warning;
        public bool compile_started;
        public bool is_compiling_after_refresh;
        public bool waited_for_reload;
    }

    /// <summary>
    /// アセットリフレッシュツール
    /// </summary>
    [Tool("refresh",
        Description = "Refresh Unity AssetDatabase to detect new or changed files. Call this after creating or modifying files outside Unity.",
        Category = ToolCategory.Asset,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public partial class RefreshAssetsHandler : CompilationWaitMutationHandler
    {
        private const int CompileStartGraceMs = 750;

        private static readonly ToolDefinition _definition = new()
        {
            name = "refresh",
            description = "Refresh Unity AssetDatabase to detect new or changed files. Call this after creating or modifying files outside Unity.",
            inputSchema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>
                    {
                        { "wait_for_reload", new Dictionary<string, object>
                            {
                                { "type", "boolean" },
                                { "description", "If true, wait for domain reload to complete before returning. Useful when scripts were modified." },
                                { "default", false }
                            }
                        },
                        { "timeout", new Dictionary<string, object>
                            {
                                { "type", "integer" },
                                { "description", "Timeout in milliseconds when waiting for reload" },
                                { "default", 30000 }
                            }
                        }
                    }
                },
                { "required", new List<string>() }
            },
            annotations = ToolAnnotations.ForMutation(
                title: "Refresh Assets",
                destructive: false,
                idempotent: true
            )
        };

        public override ToolDefinition Definition => _definition;

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var waitForReload = args.GetBool("wait_for_reload", false);
            var timeout = args.GetInt("timeout", 30000);

            string playModeWarning = null;

            // プレイモード中の場合、ScriptChangesWhilePlaying 設定に応じて処理を分岐
            // EditorPrefs の "ScriptCompilationDuringPlay" キーで設定を取得
            // 0: RecompileAndContinuePlaying, 1: RecompileAfterFinishedPlaying, 2: StopPlayingAndRecompile
            if (EditorApplication.isPlaying)
            {
                var scriptChangesSetting = EditorPrefs.GetInt("ScriptCompilationDuringPlay", 1);

                switch (scriptChangesSetting)
                {
                    case 0: // RecompileAndContinuePlaying
                        playModeWarning = "Play mode active. Scripts will hot-reload (RecompileAndContinuePlaying).";
                        break;

                    case 1: // RecompileAfterFinishedPlaying
                        playModeWarning = "Play mode active. Compilation will occur after play mode ends (RecompileAfterFinishedPlaying).";
                        break;

                    case 2: // StopPlayingAndRecompile
                        playModeWarning = "Play mode active. Play mode will stop when scripts are compiled (StopPlayingAndRecompile).";
                        break;
                }
            }

            var currentStatus = CompilationWatcher.Instance.GetStatus();

            // アセットをリフレッシュ（これによりコンパイルがトリガーされる可能性がある）
            AssetDatabase.Refresh();
            var refreshedStatus = CompilationWatcher.Instance.GetStatus();
            var compileStarted = refreshedStatus.isCompiling || refreshedStatus.lastCompileTime != currentStatus.lastCompileTime;

            if (waitForReload)
            {
                return WaitForCompilation(
                    refreshedStatus,
                    new RefreshCompilationWaitAck
                    {
                        success = true,
                        message = "AssetDatabase refreshed",
                        warning = playModeWarning,
                        compile_started = compileStarted,
                        is_compiling_after_refresh = refreshedStatus.isCompiling,
                        waited_for_reload = compileStarted
                    },
                    timeout > 0 ? timeout : 30000,
                    CompileStartGraceMs,
                    true);
            }

            return ToolResult.Ok(new RefreshAssetsOutput
            {
                success = true,
                message = "AssetDatabase refreshed",
                warning = playModeWarning,
                compile_started = compileStarted,
                is_compiling_after_refresh = refreshedStatus.isCompiling,
                waited_for_reload = false
            });
        }
    }
}
