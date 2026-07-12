using System;
using System.Text.RegularExpressions;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// 指定パターンにマッチするログが出現するまでポーリングで待機するツール。
    /// Domain Reload を跨いでも再開可能。
    /// </summary>
    [Tool("wait-for-log",
        Description = "Wait until a log message matching the given regex pattern appears in Unity console. Polls logs until a match is found or timeout.",
        Title = "Wait for Log",
        Category = ToolCategory.Logs,
        Kind = ToolKind.Query,
        Idempotent = true)]
    public class WaitForLogHandler : DomainReloadResumableQueryHandler<WaitForLogHandler.DomainReloadState>
    {
        private const int DefaultTimeoutMs = 10000;
        private const int DefaultPollIntervalMs = 500;
        private const int MatchedLogLimit = 10;

        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Regex pattern to match against log messages", Required = true)]
            public string pattern;

            [ToolParameter("Maximum wait time in milliseconds (default: 10000)")]
            public int? timeout_ms;

            [ToolParameter("Log type filter: 'all', 'errors', 'warnings', 'info' (default: 'all')", Enum = "all,errors,warnings,info")]
            public string filter;

            [ToolParameter("Poll interval in milliseconds (default: 500)")]
            public int? poll_interval_ms;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool found;
            public string pattern;
            public int elapsed_ms;
            public int? timeout_ms;
            public string message;
            public int log_count;
        }

        [Serializable]
        public class DomainReloadState
        {
            public string pattern;
            public int timeout_ms;
            public string filter;
            public int poll_interval_ms;
            public long since_ts;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<WaitForLogHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var pattern = args.GetString("pattern");

            if (string.IsNullOrWhiteSpace(pattern))
                return ToolResult.Fail("Parameter 'pattern' is required");

            try
            {
                _ = new Regex(pattern);
            }
            catch (ArgumentException ex)
            {
                return ToolResult.Fail($"Invalid regex pattern: {ex.Message}");
            }

            var timeoutMs = args.GetInt("timeout_ms", DefaultTimeoutMs);
            var filter = args.GetString("filter", "all");
            var pollIntervalMs = args.GetInt("poll_interval_ms", DefaultPollIntervalMs);
            var sinceTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 初回チェック: 既にマッチするログがあるか
            var matched = ConsoleLogCapture.instance.GetLogsFiltered(new LogFilterOptions
            {
                TypeFilter = filter,
                Since = sinceTs,
                Pattern = pattern,
                IgnoreCase = true,
                Limit = MatchedLogLimit
            });

            if (matched.Count > 0)
            {
                return ToolResult.Ok(new Output
                {
                    found = true,
                    pattern = pattern,
                    elapsed_ms = 0,
                    log_count = matched.Count,
                    message = "Matching log found immediately"
                });
            }

            // Domain Reload 待機に入る
            return WaitForDomainReload(
                new DomainReloadState
                {
                    pattern = pattern,
                    timeout_ms = timeoutMs,
                    filter = filter,
                    poll_interval_ms = pollIntervalMs,
                    since_ts = sinceTs
                },
                new Output
                {
                    found = false,
                    pattern = pattern,
                    message = $"Waiting for log matching '{pattern}' (timeout: {timeoutMs}ms)"
                },
                timeoutMs,
                pollIntervalMs);
        }

        protected override ToolResult ResumeAfterDomainReload(DomainReloadState state, DomainReloadResumeContext context)
        {
            if (state == null)
                return ToolResult.Fail("Missing wait-for-log resume state");

            if (context.IsTimedOut)
            {
                return ToolResult.Ok(new Output
                {
                    found = false,
                    pattern = state.pattern,
                    elapsed_ms = (int)context.ElapsedMs,
                    timeout_ms = state.timeout_ms,
                    message = "No matching log found within timeout"
                });
            }

            var matched = ConsoleLogCapture.instance.GetLogsFiltered(new LogFilterOptions
            {
                TypeFilter = state.filter,
                Since = state.since_ts,
                Pattern = state.pattern,
                IgnoreCase = true,
                Limit = MatchedLogLimit
            });

            if (matched.Count > 0)
            {
                return ToolResult.Ok(new Output
                {
                    found = true,
                    pattern = state.pattern,
                    elapsed_ms = (int)context.ElapsedMs,
                    log_count = matched.Count,
                    message = "Matching log found"
                });
            }

            return ContinueAfterDomainReload(state, state.poll_interval_ms);
        }
    }
}
