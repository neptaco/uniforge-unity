using System;
using System.Collections.Generic;

namespace UniForge.Tools.Mutations
{
    public partial class ControlPlayModeHandler
    {
        [Serializable]
        public class DomainReloadWaitForLogState
        {
            public string pattern;
            public int timeout_ms = 10000;
            public string filter = "all";
            public int poll_interval_ms = 500;
        }

        [Serializable]
        public class DomainReloadState
        {
            public string initial_result_text;
            public string expected_state;
            public long log_since_ts;
            public long log_wait_started_at;
            public DomainReloadWaitForLogState wait_for_log;
        }

        protected override ToolResult ResumeAfterDomainReload(DomainReloadState state, DomainReloadResumeContext context)
        {
            if (state == null)
            {
                return ToolResult.Fail("Missing control-playmode resume state");
            }

            NormalizeWaitForLog(state);

            var currentState = GetCurrentState();
            if (!string.Equals(currentState, state.expected_state, StringComparison.Ordinal))
            {
                if (context.IsTimedOut)
                {
                    return ToolResult.Complete(BuildStateWaitTimeoutResult(state));
                }

                return ContinueAfterDomainReload(state, StatePollIntervalMs);
            }

            if (state.wait_for_log == null)
            {
                return ToolResult.Complete(BuildStateCompletedResult(state, currentState));
            }

            if (state.log_wait_started_at <= 0)
            {
                state.log_wait_started_at = context.CurrentTimeUnixMs;
            }

            if (context.IsTimedOut || context.CurrentTimeUnixMs - state.log_wait_started_at > state.wait_for_log.timeout_ms)
            {
                return ToolResult.Complete(
                    BuildLogWaitTimeoutResult(
                        state,
                        context.RequestStartedAtUnixMs,
                        context.CurrentTimeUnixMs));
            }

            var matchedLogs = ConsoleLogCapture.instance.GetLogsFiltered(new LogFilterOptions
            {
                TypeFilter = state.wait_for_log.filter,
                Since = state.log_since_ts,
                Pattern = state.wait_for_log.pattern,
                IgnoreCase = true,
                Limit = 10
            });

            if (matchedLogs.Count == 0)
            {
                return ContinueAfterDomainReload(state, state.wait_for_log.poll_interval_ms);
            }

            return ToolResult.Complete(
                BuildLogMatchedResult(
                    state,
                    context.RequestStartedAtUnixMs,
                    matchedLogs[0].timestamp,
                    context.CurrentTimeUnixMs));
        }

        private ToolResult CreateDomainReloadWaitResult(
            string expectedState,
            Output ackResult,
            DomainReloadWaitForLogState waitForLog)
        {
            return WaitForDomainReload(
                new DomainReloadState
                {
                    initial_result_text = SimpleJson.Serialize(ackResult),
                    expected_state = expectedState,
                    log_since_ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    wait_for_log = waitForLog
                },
                ackResult,
                DomainReloadWaitTimeoutMs + (waitForLog?.timeout_ms ?? 0));
        }

        private static DomainReloadWaitForLogState ParseWaitForLog(Dictionary<string, object> waitForLog)
        {
            if (waitForLog == null)
            {
                return null;
            }

            var parsed = new DomainReloadWaitForLogState();

            if (waitForLog.TryGetValue("pattern", out var pattern))
            {
                parsed.pattern = pattern?.ToString();
            }

            if (string.IsNullOrWhiteSpace(parsed.pattern))
            {
                return null;
            }

            if (waitForLog.TryGetValue("timeout_ms", out var timeoutMs))
            {
                parsed.timeout_ms = ConvertToInt(timeoutMs, parsed.timeout_ms);
            }

            if (waitForLog.TryGetValue("filter", out var filter) && filter != null)
            {
                parsed.filter = filter.ToString();
            }

            if (waitForLog.TryGetValue("poll_interval_ms", out var pollIntervalMs))
            {
                parsed.poll_interval_ms = ConvertToInt(pollIntervalMs, parsed.poll_interval_ms);
            }

            if (parsed.timeout_ms <= 0)
            {
                parsed.timeout_ms = 10000;
            }

            if (parsed.poll_interval_ms <= 0)
            {
                parsed.poll_interval_ms = 500;
            }

            if (string.IsNullOrWhiteSpace(parsed.filter))
            {
                parsed.filter = "all";
            }

            return parsed;
        }

        private static int ConvertToInt(object value, int defaultValue)
        {
            if (value == null)
            {
                return defaultValue;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return defaultValue;
            }
        }

        private static void NormalizeWaitForLog(DomainReloadState state)
        {
            if (state.wait_for_log != null && string.IsNullOrWhiteSpace(state.wait_for_log.pattern))
            {
                state.wait_for_log = null;
            }
        }
    }
}
