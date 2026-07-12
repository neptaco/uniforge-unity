using System.Collections.Generic;
using UniForge.TestRunner;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// Get test results from the most recent or specified test run
    /// </summary>
    [Tool("test-results",
        Description = "Get test results from the most recent or specified test run",
        Title = "Get Test Results",
        Category = ToolCategory.Test,
        Kind = ToolKind.Query,
        Idempotent = true)]
    public partial class GetTestResultsHandler : QueryHandler
    {
        public class Args
        {
            [ToolParameter("Run ID to get results for. If empty, returns last run")]
            public string run_id;

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
            public bool completed;
            public bool running;
            public bool success;
            public bool aborted;
            public string aborted_reason;
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

        private ToolDefinition _definition;

        public override ToolDefinition Definition
        {
            get
            {
                _definition ??= ToolDefinitionBuilder.FromHandler<GetTestResultsHandler>();
                return _definition;
            }
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            if (!TestRunnerService.IsTestFrameworkAvailable)
            {
                return ToolResult.Ok(new Output
                {
                    available = false,
                    message = "Test Framework package is not installed"
                });
            }

            var args = new ToolArgsParser(argsJson);
            var runId = args.GetString("run_id", null);
            var statusFilter = args.GetString("status_filter", "all");
            var includeStackTrace = args.GetBool("include_stack_trace", true);
            var limit = args.GetInt("limit", 100);

            var cache = TestResultCache.instance;
            var run = cache.GetRun(runId);

            if (run == null)
            {
                return ToolResult.Ok(new Output
                {
                    available = true,
                    message = "No test run found"
                });
            }

            var output = new Output
            {
                run_id = run.runId,
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

            // Filter and collect results
            int totalMatching = 0;
            foreach (var result in run.results)
            {
                // Apply status filter
                bool include = statusFilter switch
                {
                    "passed" => result.status == "Passed",
                    "failed" => result.status == "Failed",
                    "skipped" => result.status == "Skipped" || result.status == "Inconclusive",
                    _ => true
                };

                if (!include) continue;

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

            return ToolResult.Ok(output);
        }
    }
}
