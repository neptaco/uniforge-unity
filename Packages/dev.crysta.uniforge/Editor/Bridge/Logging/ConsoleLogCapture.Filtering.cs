using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UniForge
{
    public partial class ConsoleLogCapture
    {
        public List<LogEntry> GetLogs(string filter = "all", int limit = 100)
        {
            lock (_lock)
            {
                var result = new List<LogEntry>();
                var buffer = LogBuffer;

                for (int i = buffer.Count - 1; i >= 0 && result.Count < limit; i--)
                {
                    var log = buffer[i];

                    bool include = filter switch
                    {
                        "errors" => log.type == "Error" || log.type == "Exception" || log.type == "Assert",
                        "warnings" => log.type == "Warning",
                        _ => true
                    };

                    if (include)
                    {
                        result.Add(log);
                    }
                }

                result.Reverse();
                return result;
            }
        }

        public List<LogEntry> GetErrors(int limit = 100)
        {
            return GetLogs("errors", limit);
        }

        public List<LogEntry> GetWarnings(int limit = 100)
        {
            return GetLogs("warnings", limit);
        }

        public List<LogEntry> GetLogsFiltered(LogFilterOptions options)
        {
            if (options == null)
            {
                options = new LogFilterOptions();
            }

            Regex regex = BuildPatternRegex(options);

            lock (_lock)
            {
                var result = new List<LogEntry>();
                var buffer = LogBuffer;

                for (int i = buffer.Count - 1; i >= 0 && result.Count < options.Limit; i--)
                {
                    var log = buffer[i];

                    if (options.Since.HasValue && log.timestamp < options.Since.Value)
                    {
                        continue;
                    }

                    if (options.Until.HasValue && log.timestamp > options.Until.Value)
                    {
                        continue;
                    }

                    bool includeByType = options.TypeFilter switch
                    {
                        "errors" => log.type == "Error" || log.type == "Exception" || log.type == "Assert",
                        "warnings" => log.type == "Warning",
                        "info" => log.type == "Log",
                        "all" => true,
                        _ => log.type.Equals(options.TypeFilter, StringComparison.OrdinalIgnoreCase)
                    };

                    if (!includeByType)
                    {
                        continue;
                    }

                    if (!MatchesPattern(log, regex, options.SearchStackTrace))
                    {
                        continue;
                    }

                    result.Add(log);
                }

                result.Reverse();
                return result;
            }
        }

        public int CountLogs(LogFilterOptions options)
        {
            var originalLimit = options.Limit;
            options.Limit = int.MaxValue;
            var count = GetLogsFiltered(options).Count;
            options.Limit = originalLimit;
            return count;
        }

        // ポーリングループで同一パターンが何度も指定されるため、コンパイル済み Regex をキャッシュする
        private const int PatternRegexCacheLimit = 32;
        private static readonly object _regexCacheLock = new object();
        private static readonly Dictionary<(string pattern, bool ignoreCase), Regex> _patternRegexCache
            = new Dictionary<(string pattern, bool ignoreCase), Regex>();

        internal static Regex BuildPatternRegex(LogFilterOptions options)
        {
            if (string.IsNullOrEmpty(options.Pattern))
            {
                return null;
            }

            var key = (options.Pattern, options.IgnoreCase);

            lock (_regexCacheLock)
            {
                if (_patternRegexCache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            var regex = CreatePatternRegex(options.Pattern, options.IgnoreCase);

            lock (_regexCacheLock)
            {
                // 上限超過時は単純にクリア（LRU 管理が必要なほどの規模ではない）
                if (_patternRegexCache.Count >= PatternRegexCacheLimit)
                {
                    _patternRegexCache.Clear();
                }
                _patternRegexCache[key] = regex;
            }

            return regex;
        }

        private static Regex CreatePatternRegex(string pattern, bool ignoreCase)
        {
            var regexOptions = ignoreCase
                ? RegexOptions.IgnoreCase | RegexOptions.Compiled
                : RegexOptions.Compiled;

            try
            {
                return new Regex(pattern, regexOptions);
            }
            catch (ArgumentException)
            {
                // 不正な正規表現はリテラル一致として扱う
                return new Regex(Regex.Escape(pattern), regexOptions);
            }
        }

        private static bool MatchesPattern(LogEntry log, Regex regex, bool searchStackTrace)
        {
            if (regex == null)
            {
                return true;
            }

            bool matches = regex.IsMatch(log.message);
            if (!matches && searchStackTrace && !string.IsNullOrEmpty(log.stackTrace))
            {
                matches = regex.IsMatch(log.stackTrace);
            }

            return matches;
        }
    }
}
