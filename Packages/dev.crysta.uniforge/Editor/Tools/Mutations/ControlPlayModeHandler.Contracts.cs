using System.Collections.Generic;
using UniForge.Tools.Queries;

namespace UniForge.Tools.Mutations
{
    public partial class ControlPlayModeHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            public class WaitForLogArgs
            {
                [ToolParameter("Regex pattern to wait for in Unity logs", Required = true)]
                public string pattern;

                [ToolParameter("Maximum time to wait in milliseconds (default: 10000)", Required = false)]
                public int? timeout_ms;

                [ToolParameter("Log type filter: \"all\", \"errors\", \"warnings\", \"info\" (default: \"all\")", Required = false, Enum = "all,errors,warnings,info")]
                public string filter;

                [ToolParameter("Poll interval in milliseconds (default: 500)", Required = false)]
                public int? poll_interval_ms;
            }

            [ToolParameter("Action to perform", Required = true, Enum = "play,pause,resume,stop,step")]
            public string action;

            [ToolParameter("Wait for the state transition to complete across domain reload", Required = false)]
            public bool wait;

            [ToolParameter("Wait for a matching Unity log after the action completes across domain reload.", Required = false)]
            public WaitForLogArgs wait_for_log;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public class LogEntry
            {
                public string message;
                public string type;
                public long timestamp;
            }

            public bool success;
            public string action;
            public string previous_state;
            public string current_state;
            public string message;
            public bool? log_wait_found;
            public string log_wait_pattern;
            public int? log_wait_elapsed_ms;
            public int? log_wait_timeout_ms;
            public int? log_count;
            public bool? logs_has_more;
            public List<LogEntry> logs;
            public GetEditorStateOutput editor_state;
        }
    }
}
