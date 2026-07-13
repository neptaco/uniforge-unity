using System.Collections.Generic;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// get-logs ツールの出力（スタックトレース付き）
    /// </summary>
    public class GetLogsOutput
    {
        public List<LogEntry> logs;
        public int count;
        public bool hasMore;
    }

    /// <summary>
    /// get-logs ツールの出力（スタックトレースなし）
    /// </summary>
    public class GetLogsCompactOutput
    {
        public List<LogEntryCompact> logs;
        public int count;
        public bool hasMore;
    }

    /// <summary>
    /// スタックトレースなしログエントリ
    /// </summary>
    public class LogEntryCompact
    {
        public string message;
        public string type;
        public long timestamp;
    }

    /// <summary>
    /// ログ取得ツール（拡張フィルタ対応）
    /// </summary>
    [Tool("logs",
        Description = "Get console logs from Unity Editor with advanced filtering options",
        Title = "Get Logs",
        Category = ToolCategory.Logs,
        Kind = ToolKind.Query,
        Idempotent = true)]
    [ToolOutput(typeof(GetLogsOutput))]
    public class GetLogsHandler : QueryHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Filter logs by type. 'errors' includes Error/Exception/Assert.", Enum = "all,errors,warnings,info", Default = "all")]
            public string filter;

            [ToolParameter("Maximum number of logs to return", Default = 100)]
            public int limit;

            [ToolParameter("Only return logs after this Unix timestamp (milliseconds)")]
            public long? since;

            [ToolParameter("Only return logs before this Unix timestamp (milliseconds)")]
            public long? until;

            [ToolParameter("Regex pattern to filter log messages (grep)")]
            public string pattern;

            [ToolParameter("Case insensitive pattern matching", Default = true)]
            public bool ignore_case;

            [ToolParameter("Also search in stack traces", Default = false)]
            public bool search_stack_trace;

            [ToolParameter("Include stack traces in output", Default = false)]
            public bool include_stack_trace;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);

            var options = new LogFilterOptions
            {
                TypeFilter = args.GetString("filter", "all"),
                Limit = args.GetInt("limit", 100),
                Pattern = args.GetString("pattern"),
                IgnoreCase = args.GetBool("ignore_case", true),
                SearchStackTrace = args.GetBool("search_stack_trace", false)
            };

            var includeStackTrace = args.GetBool("include_stack_trace", false);

            // 時間フィルタ
            if (args.HasKey("since"))
            {
                options.Since = args.GetLong("since");
            }
            if (args.HasKey("until"))
            {
                options.Until = args.GetLong("until");
            }

            var logsRaw = ConsoleLogCapture.instance.GetLogsFiltered(options);

            // 空エントリを除外（message が null/空のログを返さない）
            var logs = logsRaw.FindAll(l => !string.IsNullOrEmpty(l.message));

            // 件数超過チェック用
            var totalCount = ConsoleLogCapture.instance.CountLogs(options);

            // スタックトレース除外オプション
            if (includeStackTrace)
            {
                return ToolResult.Ok(new GetLogsOutput
                {
                    logs = logs,
                    count = logs.Count,
                    hasMore = totalCount > logs.Count
                });
            }

            var compactLogs = new List<LogEntryCompact>();
            foreach (var log in logs)
            {
                compactLogs.Add(new LogEntryCompact
                {
                    message = log.message,
                    type = log.type,
                    timestamp = log.timestamp
                });
            }

            return ToolResult.Ok(new GetLogsCompactOutput
            {
                logs = compactLogs,
                count = logs.Count,
                hasMore = totalCount > logs.Count
            });
        }
    }
}
