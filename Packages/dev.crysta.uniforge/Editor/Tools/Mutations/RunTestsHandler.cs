using System;
using System.Collections.Generic;
using UniForge.TestRunner;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Run Unity tests
    /// </summary>
    [Tool("run-tests",
        Description = "Run Unity tests and wait for completion.",
        Title = "Run Tests",
        Category = ToolCategory.Test,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public class RunTestsHandler : DomainReloadResumableMutationHandler<RunTestsHandler.RunTestsWaitState>
    {
        [Serializable]
        public class RunTestsWaitState
        {
            public string run_id;
            public long run_start_time;
            public bool has_filter;   // test_names / categories / assemblies のいずれかが明示指定されたか
        }

        public class Args
        {
            [ToolParameter("Test mode to run", Enum = "EditMode,PlayMode,Both", Default = "EditMode")]
            public string mode;

            [ToolParameter("Test names to run (comma-separated). If empty, runs all tests")]
            public string test_names;

            [ToolParameter("Category filter (comma-separated)")]
            public string categories;

            [ToolParameter("Assembly filter (comma-separated)")]
            public string assemblies;

            [ToolParameter("Timeout in milliseconds before the test run is considered timed out", Default = 300000)]
            public int timeout;
        }

        public class RunStartedOutput
        {
            public string message;
            public string run_id;
            public string mode;
            public bool running;
        }

        [Serializable]
        public class RunCompletedOutput
        {
            public string run_id;
            public bool success;
            public int pass_count;
            public int fail_count;
            public int skip_count;
            public int total_count;
            public double duration_seconds;
            public string message;
        }

        [Serializable]
        public class RunTimedOutOutput
        {
            public string message;
            public bool timed_out;
            public string run_id;
        }

        [Serializable]
        public class RunAbortedOutput
        {
            public string message;
            public bool aborted;
            public string aborted_reason;
            public string run_id;
        }

        [Serializable]
        public class RunNotFoundOutput
        {
            public string message;
            public string run_id;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            if (!TestRunnerService.IsTestFrameworkAvailable)
            {
                return ToolResult.Fail("Test Framework package is not installed");
            }

            if (HasPendingRunTestsRequest())
            {
                return ToolResult.Fail("A test run is already waiting to resume after domain reload");
            }

            if (TestRunnerService.instance.IsRunning)
            {
                return ToolResult.Fail($"A test run is already in progress: {TestRunnerService.instance.CurrentRunId}");
            }

            // 未保存のシーン変更をチェック（テスト実行時にダイアログがブロックするのを防ぐ）
            if (SceneHelper.HasUnsavedSceneChanges(out var dirtyScenes))
            {
                return ToolResult.Fail($"Scene(s) have unsaved changes: {dirtyScenes}. Please save or discard changes before running tests.");
            }

            var args = new ToolArgsParser(argsJson);
            var mode = args.GetString("mode", "EditMode");
            var testNames = args.GetString("test_names", null);
            var categories = args.GetString("categories", null);
            var assemblies = args.GetString("assemblies", null);

            // Parse comma-separated values
            var settings = new TestExecutionSettings
            {
                Mode = mode,
                TestNames = ParseCommaSeparated(testNames),
                Categories = ParseCommaSeparated(categories),
                Assemblies = ParseCommaSeparated(assemblies)
            };

            var runId = TestRunnerService.instance.StartTests(settings, out var startError);

            if (string.IsNullOrEmpty(runId))
            {
                return ToolResult.Fail(string.IsNullOrEmpty(startError)
                    ? "Failed to start test run"
                    : $"Failed to start test run: {startError}");
            }

            var run = TestResultCache.instance.GetRun(runId);
            return WaitForDomainReload(
                new RunTestsWaitState
                {
                    run_id = runId,
                    run_start_time = run != null ? run.startTime : 0,
                    has_filter = settings.TestNames != null ||
                                 settings.Categories != null ||
                                 settings.Assemblies != null
                },
                new RunStartedOutput
                {
                    message = "Test run started",
                    run_id = runId,
                    mode = mode,
                    running = true
                },
                args.GetInt("timeout", 300000),
                250);
        }

        protected override ToolResult ResumeAfterDomainReload(
            RunTestsWaitState state,
            DomainReloadResumeContext context)
        {
            var cache = TestResultCache.instance;
            var run = ResolveRun(cache, state);

            if (run == null)
            {
                if (!context.IsTimedOut)
                {
                    return ContinueAfterDomainReload(state, 250);
                }

                return ToolResult.Complete(new RunNotFoundOutput
                {
                    message = "Test run not found",
                    run_id = state.run_id
                }, success: false);
            }

            if (ShouldContinueWaitingAfterDomainReload(run, context))
            {
                return ContinueAfterDomainReload(state, 250);
            }

            if (!run.completed && !context.IsTimedOut)
            {
                return ContinueAfterDomainReload(state, 250);
            }

            if (!run.completed)
            {
                // timeout した run の状態を残すと以後の run-tests がすべて
                // 「already in progress」で拒否されるため、ここで中断する
                TestRunnerService.instance.CancelRun(
                    run.runId,
                    $"Timed out after {context.TimeoutMs}ms",
                    context.ElapsedMs / 1000.0);

                return ToolResult.Complete(new RunTimedOutOutput
                {
                    message = $"Test run timed out after {context.TimeoutMs}ms",
                    timed_out = true,
                    run_id = state.run_id
                }, success: false);
            }

            if (run.aborted)
            {
                return ToolResult.Complete(new RunAbortedOutput
                {
                    message = $"Test run aborted: {run.abortedReason}",
                    aborted = true,
                    aborted_reason = run.abortedReason,
                    run_id = run.runId
                }, success: false);
            }

            // 明示的なフィルタ指定で 0 件実行になった場合は成功として報告しない
            // (存在しないテスト名の指定が「All tests passed」になるのを防ぐ)
            if (state.has_filter && run.totalCount == 0)
            {
                return ToolResult.Complete(new RunCompletedOutput
                {
                    run_id = run.runId,
                    success = false,
                    pass_count = run.passCount,
                    fail_count = run.failCount,
                    skip_count = run.skipCount,
                    total_count = run.totalCount,
                    duration_seconds = run.durationSeconds,
                    message = "No tests matched the specified filter"
                }, success: false);
            }

            return ToolResult.Complete(new RunCompletedOutput
            {
                run_id = run.runId,
                success = run.success,
                pass_count = run.passCount,
                fail_count = run.failCount,
                skip_count = run.skipCount,
                total_count = run.totalCount,
                duration_seconds = run.durationSeconds,
                message = run.success ? "All tests passed" : $"{run.failCount} test(s) failed"
            }, success: run.success);
        }

        private static TestRunRecord ResolveRun(TestResultCache cache, RunTestsWaitState state)
        {
            var run = cache.GetRun(state.run_id);
            if (run != null)
            {
                return run;
            }

            if (state.run_start_time <= 0)
            {
                return null;
            }

            foreach (var candidate in cache.Runs)
            {
                if (candidate.startTime == state.run_start_time)
                {
                    return candidate;
                }
            }

            return null;
        }

        internal static bool HasPendingRunTestsRequest()
        {
            var cache = TestResultCache.instance;
            foreach (var pending in PendingDomainReloadToolRequestsStorage.instance.Requests)
            {
                if (pending.toolName != "run-tests" || pending.readyToSend)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(pending.stateJson))
                {
                    return true;
                }

                var state = JsonUtility.FromJson<RunTestsWaitState>(pending.stateJson);
                var run = ResolveRun(cache, state);
                if (run == null || !run.completed)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ShouldContinueWaitingAfterDomainReload(TestRunRecord run, DomainReloadResumeContext context)
        {
            return IsDomainReloadAbort(run) && !context.IsTimedOut;
        }

        internal static bool IsDomainReloadAbort(TestRunRecord run)
        {
            return run != null &&
                   run.aborted &&
                   string.Equals(run.abortedReason, TestResultCache.DomainReloadAbortReason, StringComparison.Ordinal);
        }

        private string[] ParseCommaSeparated(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parts = value.Split(',');
            var result = new List<string>();

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result.Count > 0 ? result.ToArray() : null;
        }
    }
}
