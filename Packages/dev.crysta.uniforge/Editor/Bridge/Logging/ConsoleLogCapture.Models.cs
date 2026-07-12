using System;
using UnityEngine;

namespace UniForge
{
    /// <summary>
    /// ログフィルタリングオプション
    /// </summary>
    public class LogFilterOptions
    {
        /// <summary>ログタイプフィルタ: "all", "errors", "warnings", "info", または特定のLogType名</summary>
        public string TypeFilter { get; set; } = "all";

        /// <summary>この時刻以降のログのみ（Unix timestamp ms）</summary>
        public long? Since { get; set; }

        /// <summary>この時刻以前のログのみ（Unix timestamp ms）</summary>
        public long? Until { get; set; }

        /// <summary>メッセージに対するgrep（正規表現パターン）</summary>
        public string Pattern { get; set; }

        /// <summary>大文字小文字を無視するか</summary>
        public bool IgnoreCase { get; set; } = true;

        /// <summary>スタックトレースも検索対象にするか</summary>
        public bool SearchStackTrace { get; set; } = false;

        /// <summary>最大件数</summary>
        public int Limit { get; set; } = 100;
    }

    /// <summary>
    /// Captures Unity console logs for retrieval via MCP.
    /// </summary>
    [Serializable]
    public class LogEntry
    {
        public string message;
        public string stackTrace;
        public string type;
        public long timestamp;

        public LogEntry() { }

        public LogEntry(string condition, string stackTrace, LogType logType)
        {
            this.message = condition;
            this.stackTrace = stackTrace;
            this.type = logType.ToString();
            this.timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
