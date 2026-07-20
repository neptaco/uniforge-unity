using System;
using System.Collections.Generic;
using UniForge.TestRunner;
using UniForge.Tools.Mutations;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// Get test results from the most recent or specified test run
    /// </summary>
    [Tool("test-results",
        Description = "Get test results from the most recent, specified, or next test run, optionally waiting for completion",
        Title = "Get Test Results",
        Category = ToolCategory.Test,
        Kind = ToolKind.Query,
        Idempotent = true)]
    public class GetTestResultsHandler :
        DomainReloadResumableQueryHandler<GetTestResultsHandler.DomainReloadState>
    {
        private const int DefaultTimeoutMs = 60000;
        private const int PollIntervalMs = 250;

        public class Args
        {
            [ToolParameter("Run ID to get results for. If empty, returns last run")]
            public string run_id;

            [ToolParameter("Return the first run started after this run ID")]
            public string after_run_id;

            [ToolParameter("Wait for the selected run to finish", Default = false)]
            public bool wait;

            [ToolParameter("Maximum total wait time in milliseconds", Default = DefaultTimeoutMs)]
            public int timeout;

            [ToolParameter("Filter by status", Enum = "all,passed,failed,skipped", Default = "all")]
            public string status_filter;

            [ToolParameter("Include stack traces", Default = true)]
            public bool include_stack_trace;

            [ToolParameter("Maximum results to return", Default = 100)]
            public int limit;
        }

        public class Output
        {
            public string run_id;
            public string target_run_id;
            public string after_run_id;
            public bool found;
            public bool completed;
            public bool running;
            public bool success;
            public bool aborted;
            public string aborted_reason;
            public bool timed_out;
            public string mode;
            public int pass_count;
            public int fail_count;
            public int skip_count;
            public int total_count;
            public double duration_seconds;
            public List<ResultEntry> results;
            public bool has_more;
            public bool available;
            public string message;
        }

        public class ResultEntry
        {
            public string full_name;
            public string display_name;
            public string status;
            public double duration_seconds;
            public string message;
            public string stack_trace;
        }

        [Serializable]
        public class DomainReloadState
        {
            public string after_run_id;
            public string target_run_id;
            public bool wait;
            public string status_filter;
            public bool include_stack_trace;
            public int limit;
            public int timeout;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var runId = args.GetString("run_id", null);
            var afterRunId = args.GetString("after_run_id", null);
            var wait = args.GetBool("wait", false);
            var timeout = args.GetInt("timeout", DefaultTimeoutMs);
            var statusFilter = args.GetString("status_filter", "all");
            var includeStackTrace = args.GetBool("include_stack_trace", true);
            var limit = args.GetInt("limit", 100);

            var hasRunId = !string.IsNullOrEmpty(runId);
            // Unlike run_id, an empty after_run_id has no "latest run" meaning.
            // Treat an explicitly supplied empty anchor as unknown instead of falling back.
            var hasAfterRunId = args.HasKey("after_run_id");

            if (hasRunId && hasAfterRunId)
            {
                return ToolResult.Fail("Parameters 'run_id' and 'after_run_id' are mutually exclusive");
            }

            if (wait && !hasRunId && !hasAfterRunId)
            {
                return ToolResult.Fail("Parameter 'wait' requires 'run_id' or 'after_run_id'");
            }

            var cache = TestResultCache.instance;
            TestRunRecord run = null;

            if (hasAfterRunId)
            {
                var resolution = cache.ResolveSuccessor(afterRunId, out run);
                if (resolution == TestRunSuccessorResolution.AnchorNotFound)
                {
                    return ToolResult.Fail($"Test run anchor '{afterRunId}' was not found in the cache");
                }
            }

            if (!TestRunnerService.IsTestFrameworkAvailable)
            {
                return ToolResult.Ok(new Output
                {
                    available = false,
                    found = false,
                    after_run_id = afterRunId,
                    message = "Test Framework package is not installed"
                });
            }

            if (hasAfterRunId && run == null)
            {
                var state = CreateState(
                    afterRunId,
                    null,
                    wait,
                    statusFilter,
                    includeStackTrace,
                    limit,
                    timeout);

                if (!wait)
                {
                    return ToolResult.Ok(CreateNoSuccessorOutput(afterRunId));
                }

                if (timeout <= 0)
                {
                    return CreateTimedOutResult(cache, state, null);
                }

                return WaitForDomainReload(
                    state,
                    CreateWaitingOutput(cache, state, null),
                    timeout,
                    PollIntervalMs);
            }

            if (!hasAfterRunId)
            {
                run = cache.GetRun(runId);
            }

            if (run == null)
            {
                return ToolResult.Ok(new Output
                {
                    available = true,
                    found = false,
                    message = "No test run found"
                });
            }

            if (!wait)
            {
                return ToolResult.Ok(CreateRunOutput(
                    cache,
                    run,
                    afterRunId,
                    statusFilter,
                    includeStackTrace,
                    limit));
            }

            var waitState = CreateState(
                afterRunId,
                run.runId,
                true,
                statusFilter,
                includeStackTrace,
                limit,
                timeout);

            if (HasTerminalResult(run))
            {
                return ToolResult.Ok(CreateRunOutput(
                    cache,
                    run,
                    afterRunId,
                    statusFilter,
                    includeStackTrace,
                    limit));
            }

            if (timeout <= 0)
            {
                return CreateTimedOutResult(cache, waitState, run);
            }

            return WaitForDomainReload(
                waitState,
                CreateWaitingOutput(cache, waitState, run),
                timeout,
                PollIntervalMs);
        }

        protected override ToolResult ResumeAfterDomainReload(
            DomainReloadState state,
            DomainReloadResumeContext context)
        {
            if (state == null || !state.wait)
            {
                return ToolResult.Fail("Missing test-results resume state");
            }

            var cache = TestResultCache.instance;
            TestRunRecord run = null;

            if (!string.IsNullOrEmpty(state.target_run_id))
            {
                run = cache.GetRun(state.target_run_id);
            }
            else
            {
                if (string.IsNullOrEmpty(state.after_run_id))
                {
                    return ToolResult.Fail("Missing test-results target run state");
                }

                var resolution = cache.ResolveSuccessor(state.after_run_id, out run);
                if (resolution == TestRunSuccessorResolution.AnchorNotFound)
                {
                    return ToolResult.Fail(
                        $"Test run anchor '{state.after_run_id}' was not found in the cache");
                }

                if (resolution == TestRunSuccessorResolution.Found)
                {
                    state.target_run_id = run.runId;
                }
            }

            if (HasReachedTimeout(state, context))
            {
                return CreateTimedOutResult(cache, state, run);
            }

            if (run != null && HasTerminalResult(run))
            {
                return ToolResult.Ok(CreateRunOutput(
                    cache,
                    run,
                    state.after_run_id,
                    state.status_filter,
                    state.include_stack_trace,
                    state.limit));
            }

            return ContinueAfterDomainReload(state, PollIntervalMs);
        }

        private static DomainReloadState CreateState(
            string afterRunId,
            string targetRunId,
            bool wait,
            string statusFilter,
            bool includeStackTrace,
            int limit,
            int timeout)
        {
            return new DomainReloadState
            {
                after_run_id = afterRunId,
                target_run_id = targetRunId,
                wait = wait,
                status_filter = statusFilter,
                include_stack_trace = includeStackTrace,
                limit = limit,
                timeout = timeout
            };
        }

        private static Output CreateNoSuccessorOutput(string afterRunId)
        {
            return new Output
            {
                available = true,
                found = false,
                after_run_id = afterRunId,
                message = $"No run started after {afterRunId}"
            };
        }

        private static Output CreateWaitingOutput(
            TestResultCache cache,
            DomainReloadState state,
            TestRunRecord run)
        {
            if (run == null)
            {
                return new Output
                {
                    available = true,
                    found = false,
                    after_run_id = state.after_run_id,
                    target_run_id = state.target_run_id,
                    message = $"Waiting for a run started after {state.after_run_id}"
                };
            }

            var output = CreateRunOutput(
                cache,
                run,
                state.after_run_id,
                state.status_filter,
                state.include_stack_trace,
                state.limit);
            output.message = $"Waiting for test results (run {run.runId})";
            return output;
        }

        private static ToolResult CreateTimedOutResult(
            TestResultCache cache,
            DomainReloadState state,
            TestRunRecord run)
        {
            Output output;
            if (run != null)
            {
                output = CreateRunOutput(
                    cache,
                    run,
                    state.after_run_id,
                    state.status_filter,
                    state.include_stack_trace,
                    state.limit);
                output.success = false;
            }
            else
            {
                output = new Output
                {
                    available = true,
                    found = false,
                    after_run_id = state.after_run_id,
                    target_run_id = state.target_run_id
                };
            }

            var targetDescription = !string.IsNullOrEmpty(state.target_run_id)
                ? $"run {state.target_run_id}"
                : $"run after {state.after_run_id}";

            output.timed_out = true;
            output.message = $"Timed out waiting for test results ({targetDescription})";
            return ToolResult.Complete(output, success: false);
        }

        private static bool HasTerminalResult(TestRunRecord run)
        {
            if (RunTestsHandler.IsDomainReloadAbort(run))
            {
                return false;
            }

            return run.completed || run.aborted;
        }

        private static bool HasReachedTimeout(
            DomainReloadState state,
            DomainReloadResumeContext context)
        {
            return state.timeout <= 0 || context.ElapsedMs >= state.timeout;
        }

        private static Output CreateRunOutput(
            TestResultCache cache,
            TestRunRecord run,
            string afterRunId,
            string statusFilter,
            bool includeStackTrace,
            int limit)
        {
            var output = new Output
            {
                run_id = run.runId,
                target_run_id = run.runId,
                after_run_id = afterRunId,
                found = true,
                completed = run.completed,
                running = !run.completed && cache.CurrentRunId == run.runId,
                success = run.success,
                aborted = run.aborted,
                aborted_reason = run.abortedReason,
                mode = run.mode,
                pass_count = run.passCount,
                fail_count = run.failCount,
                skip_count = run.skipCount,
                total_count = run.totalCount,
                duration_seconds = run.durationSeconds,
                results = new List<ResultEntry>(),
                available = true
            };

            var totalMatching = 0;
            foreach (var result in run.results)
            {
                var include = statusFilter switch
                {
                    "passed" => result.status == "Passed",
                    "failed" => result.status == "Failed",
                    "skipped" => result.status == "Skipped" || result.status == "Inconclusive",
                    _ => true
                };

                if (!include)
                {
                    continue;
                }

                totalMatching++;

                if (output.results.Count < limit)
                {
                    output.results.Add(new ResultEntry
                    {
                        full_name = result.fullName,
                        display_name = result.displayName,
                        status = result.status,
                        duration_seconds = result.durationSeconds,
                        message = result.message,
                        stack_trace = includeStackTrace ? result.stackTrace : null
                    });
                }
            }

            output.has_more = totalMatching > limit;
            return output;
        }
    }
}
