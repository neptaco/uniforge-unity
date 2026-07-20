using System;
using System.Collections.Generic;
using UniForge.TestRunner;
using UnityEditor;
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
        internal Func<bool> TestCacheReadyOverrideForTests { get; set; }
        internal Action RefreshTestCacheOverrideForTests { get; set; }
        internal Func<string> DirtyScenesOverrideForTests { get; set; }
        internal Func<TestExecutionSettings, string> StartTestsOverrideForTests { get; set; }

        [Serializable]
        public class RunTestsWaitState
        {
            public string run_id;
            public long run_start_time;
            public long test_started_at;
            public bool waiting_for_compile;
            public string mode;
            public string[] test_names;
            public string[] categories;
            public string[] assemblies;
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
            if (HasUnsavedSceneChanges(out var dirtyScenes))
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

            return StartTestsOrWaitForCompilation(settings, args.GetInt("timeout", 300000));
        }

        internal ToolResult StartTestsOrWaitForCompilation(TestExecutionSettings settings, int timeoutMs)
        {
            var state = CreateWaitState(settings);
            if (CompilationWatcher.Instance.GetStatus().isCompiling)
            {
                state.waiting_for_compile = true;
                return WaitForDomainReload(
                    state,
                    new RunStartedOutput
                    {
                        message = "Waiting for compilation before test run",
                        mode = settings.Mode,
                        running = false
                    },
                    timeoutMs,
                    250);
            }

            if (!TryStartTests(
                    settings,
                    state,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    out var startError))
            {
                return StartTestsFailure(startError);
            }

            return WaitForDomainReload(
                state,
                new RunStartedOutput
                {
                    message = "Test run started",
                    run_id = state.run_id,
                    mode = settings.Mode,
                    running = true
                },
                timeoutMs,
                250);
        }

        protected override ToolResult ResumeAfterDomainReload(
            RunTestsWaitState state,
            DomainReloadResumeContext context)
        {
            if (state.waiting_for_compile)
            {
                var compileStatus = CompilationWatcher.Instance.GetStatus();
                if (compileStatus.isCompiling && !context.IsTimedOut)
                {
                    return ContinueAfterDomainReload(state, 250);
                }

                if (context.IsTimedOut)
                {
                    return ToolResult.Complete(new RunTimedOutOutput
                    {
                        message = "Timed out waiting for compilation before test run",
                        timed_out = true,
                        run_id = state.run_id
                    }, success: false);
                }

                if (compileStatus.errors.Count > 0)
                {
                    return ToolResult.Fail(
                        $"Cannot run tests: compilation finished with {compileStatus.errors.Count} error(s)");
                }

                if (!IsTestCacheReady())
                {
                    RefreshTestCache();
                    return ContinueAfterDomainReload(state, 250);
                }

                if (HasUnsavedSceneChanges(out var dirtyScenes))
                {
                    return ToolResult.Fail(
                        $"Scene(s) have unsaved changes: {dirtyScenes}. Please save or discard changes before running tests.");
                }

                var settings = CreateExecutionSettings(state);
                if (!TryStartTests(
                        settings,
                        state,
                        context.CurrentTimeUnixMs,
                        out var startError))
                {
                    return StartTestsFailure(startError);
                }

                return WaitForDomainReload(
                    state,
                    null,
                    ClampTimeoutMilliseconds(context.TimeoutMs),
                    250);
            }

            var cache = TestResultCache.instance;
            var run = ResolveRun(cache, state);
            var testContext = CreateTestRunContext(state, context);

            if (run == null)
            {
                if (!testContext.IsTimedOut)
                {
                    return ContinueAfterDomainReload(state, 250);
                }

                return ToolResult.Complete(new RunNotFoundOutput
                {
                    message = "Test run not found",
                    run_id = state.run_id
                }, success: false);
            }

            var shouldContinueWaiting =
                ShouldContinueWaitingAfterDomainReload(run, testContext.IsTimedOut) ||
                (!run.completed && !testContext.IsTimedOut);
            if (shouldContinueWaiting)
            {
                if (!run.runStarted)
                {
                    EditorApplication.QueuePlayerLoopUpdate();
                }

                return ContinueAfterDomainReload(state, 250);
            }

            if (!run.completed)
            {
                var timeoutMessage = $"Test run timed out after {context.TimeoutMs}ms";
                if (!run.runStarted)
                {
                    timeoutMessage += " (the test run has not reported starting — the editor may be throttled/unfocused or a compile may be pending)";
                }

                // timeout した run の状態を残すと以後の run-tests がすべて
                // 「already in progress」で拒否されるため、ここで中断する
                TestRunnerService.instance.CancelRun(
                    run.runId,
                    $"Timed out after {context.TimeoutMs}ms",
                    Math.Max(0L, testContext.ElapsedMs) / 1000.0);

                return ToolResult.Complete(new RunTimedOutOutput
                {
                    message = timeoutMessage,
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

        private static RunTestsWaitState CreateWaitState(TestExecutionSettings settings)
        {
            return new RunTestsWaitState
            {
                mode = settings.Mode,
                test_names = settings.TestNames,
                categories = settings.Categories,
                assemblies = settings.Assemblies,
                has_filter = settings.TestNames != null ||
                             settings.Categories != null ||
                             settings.Assemblies != null
            };
        }

        private static TestExecutionSettings CreateExecutionSettings(RunTestsWaitState state)
        {
            return new TestExecutionSettings
            {
                Mode = state.mode,
                TestNames = state.test_names,
                Categories = state.categories,
                Assemblies = state.assemblies
            };
        }

        private bool TryStartTests(
            TestExecutionSettings settings,
            RunTestsWaitState state,
            long testStartedAtUnixMs,
            out string startError)
        {
            string runId;
            if (StartTestsOverrideForTests != null)
            {
                runId = StartTestsOverrideForTests(settings);
                startError = null;
            }
            else
            {
                runId = TestRunnerService.instance.StartTests(settings, out startError);
            }

            if (string.IsNullOrEmpty(runId))
            {
                return false;
            }

            var run = TestResultCache.instance.GetRun(runId);
            state.waiting_for_compile = false;
            state.run_id = runId;
            state.run_start_time = run != null ? run.startTime : 0;
            state.test_started_at = testStartedAtUnixMs;
            return true;
        }

        private bool IsTestCacheReady()
        {
            if (TestCacheReadyOverrideForTests != null)
            {
                return TestCacheReadyOverrideForTests();
            }

#if UNITY_INCLUDE_TESTS
            return TestRunnerService.instance.IsCacheReady;
#else
            return true;
#endif
        }

        private void RefreshTestCache()
        {
            if (RefreshTestCacheOverrideForTests != null)
            {
                RefreshTestCacheOverrideForTests();
                return;
            }

#if UNITY_INCLUDE_TESTS
            TestRunnerService.instance.RefreshCache();
#endif
        }

        private bool HasUnsavedSceneChanges(out string dirtyScenes)
        {
            if (DirtyScenesOverrideForTests != null)
            {
                dirtyScenes = DirtyScenesOverrideForTests();
                return !string.IsNullOrEmpty(dirtyScenes);
            }

            return SceneHelper.HasUnsavedSceneChanges(out dirtyScenes);
        }

        private static DomainReloadResumeContext CreateTestRunContext(
            RunTestsWaitState state,
            DomainReloadResumeContext context)
        {
            var testStartedAt = state.test_started_at > 0
                ? state.test_started_at
                : state.run_start_time > 0
                    ? state.run_start_time
                    : context.RequestStartedAtUnixMs;

            return new DomainReloadResumeContext(
                testStartedAt,
                context.CurrentTimeUnixMs,
                context.TimeoutMs);
        }

        private static int ClampTimeoutMilliseconds(long timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                return 0;
            }

            return timeoutMs > int.MaxValue ? int.MaxValue : (int)timeoutMs;
        }

        private static ToolResult StartTestsFailure(string startError)
        {
            return ToolResult.Fail(string.IsNullOrEmpty(startError)
                ? "Failed to start test run"
                : $"Failed to start test run: {startError}");
        }

        private static TestRunRecord ResolveRun(TestResultCache cache, RunTestsWaitState state)
        {
            if (!string.IsNullOrEmpty(state.run_id))
            {
                var run = cache.GetRun(state.run_id);
                if (run != null)
                {
                    return run;
                }
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
                if (state == null || state.waiting_for_compile)
                {
                    return true;
                }

                var run = ResolveRun(cache, state);
                if (run == null || !run.completed)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ShouldContinueWaitingAfterDomainReload(TestRunRecord run, bool isTimedOut)
        {
            return IsDomainReloadAbort(run) && !isTimedOut;
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
