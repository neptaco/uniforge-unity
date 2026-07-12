using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// コンパイルリクエスト / ステータス取得ツール
    /// </summary>
    [Tool("compile",
        Description = "Get compilation status, or request compile with optional wait for completion.",
        Category = ToolCategory.Compilation,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public partial class RequestCompileHandler : CompilationWaitMutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("If true, only return current compile status without triggering compilation", Default = false)]
            public bool status;

            [ToolParameter("If true, wait for compilation to complete and return the result", Default = false)]
            public bool wait;

            [ToolParameter("Timeout in milliseconds when waiting for completion", Default = 30000)]
            public int timeout;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<RequestCompileHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var statusOnly = args.GetBool("status", false);
            var waitForCompletion = args.GetBool("wait", false);
            var timeout = args.GetInt("timeout", 30000);

            // ステータスのみ返す（コンパイルを発火しない）
            if (statusOnly)
            {
                return ReturnStatus();
            }

            string playModeWarning = null;
            bool cannotWait = false;

            // プレイモード中の場合、ScriptChangesWhilePlaying 設定に応じて処理を分岐
            // EditorPrefs の "ScriptCompilationDuringPlay" キーで設定を取得
            // 0: RecompileAndContinuePlaying, 1: RecompileAfterFinishedPlaying, 2: StopPlayingAndRecompile
            if (EditorApplication.isPlaying)
            {
                var scriptChangesSetting = EditorPrefs.GetInt("ScriptCompilationDuringPlay", 1);
                var settingName = GetScriptCompilationSettingName(scriptChangesSetting);

                playModeWarning = scriptChangesSetting switch
                {
                    0 => $"Play mode active. Scripts will hot-reload ({settingName}).",
                    1 => $"Play mode active. Compilation is deferred until play mode ends ({settingName}).",
                    2 => $"Play mode active. Play mode will stop when scripts are compiled ({settingName}).",
                    _ => $"Play mode active. Unknown compilation setting ({settingName})."
                };
                cannotWait = scriptChangesSetting == 1;
            }

            var currentStatus = CompilationWatcher.Instance.GetStatus();
            CompilationWatcher.Instance.RequestCompilation();

            // プレイモード中で RecompileAfterFinishedPlaying の場合は待機せずに即座に返す
            if (waitForCompletion && cannotWait)
            {
                return ToolResult.Ok(new CompileRequestResult
                {
                    message = "Compilation requested but cannot wait",
                    wasCompiling = currentStatus.isCompiling,
                    warning = playModeWarning,
                    waitSkipped = true,
                    reason = "Play mode is active with RecompileAfterFinishedPlaying setting. Stop play mode to compile."
                });
            }

            if (waitForCompletion)
            {
                return WaitForCompilation(
                    currentStatus,
                    new CompileRequestResult
                    {
                        message = "Compilation requested",
                        wasCompiling = currentStatus.isCompiling,
                        warning = playModeWarning
                    },
                    timeout > 0 ? timeout : 30000);
            }

            return ToolResult.Ok(new CompileRequestResult
            {
                message = "Compilation requested",
                wasCompiling = currentStatus.isCompiling,
                warning = playModeWarning
            });
        }

        private static string GetScriptCompilationSettingName(int setting) => setting switch
        {
            0 => "RecompileAndContinuePlaying",
            1 => "RecompileAfterFinishedPlaying",
            2 => "StopPlayingAndRecompile",
            _ => "Unknown"
        };

        private static ToolResult ReturnStatus()
        {
            var status = CompilationWatcher.Instance.GetStatus();

            if (EditorApplication.isPlaying)
            {
                var scriptChangesSetting = EditorPrefs.GetInt("ScriptCompilationDuringPlay", 1);

                return ToolResult.Ok(new CompileStatusOutput
                {
                    isCompiling = status.isCompiling,
                    lastCompileTime = status.lastCompileTime,
                    errors = status.errors,
                    warnings = status.warnings,
                    success = status.success,
                    isPlaying = true,
                    scriptCompilationDuringPlay = GetScriptCompilationSettingName(scriptChangesSetting),
                    note = scriptChangesSetting == 1 ? "Compilation is deferred until play mode ends" : null
                });
            }

            return ToolResult.Ok(status);
        }
    }

    /// <summary>
    /// コンパイルリクエスト結果
    /// </summary>
    public class CompileRequestResult
    {
        public string message;
        public bool wasCompiling;
        public string warning;
        public bool? waitSkipped;
        public string reason;
    }

    /// <summary>
    /// コンパイルステータス出力（プレイモード時）
    /// </summary>
    public class CompileStatusOutput
    {
        public bool isCompiling;
        public long lastCompileTime;
        public List<CompilerError> errors;
        public List<CompilerError> warnings;
        public bool success;
        public bool isPlaying;
        public string scriptCompilationDuringPlay;
        public string note;
    }
}
