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

        private static Regex BuildPatternRegex(LogFilterOptions options)
        {
            if (string.IsNullOrEmpty(options.Pattern))
            {
                return null;
            }

            try
            {
                var regexOptions = options.IgnoreCase
                    ? RegexOptions.IgnoreCase | RegexOptions.Compiled
                    : RegexOptions.Compiled;
                return new Regex(options.Pattern, regexOptions);
            }
            catch (ArgumentException)
            {
                var escaped = Regex.Escape(options.Pattern);
                var regexOptions = options.IgnoreCase
                    ? RegexOptions.IgnoreCase | RegexOptions.Compiled
                    : RegexOptions.Compiled;
                return new Regex(escaped, regexOptions);
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
