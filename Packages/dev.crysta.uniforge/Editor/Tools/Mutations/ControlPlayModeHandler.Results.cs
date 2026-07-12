using System;
using System.Collections.Generic;
using UnityEditor;
using UniForge.Tools.Queries;

namespace UniForge.Tools.Mutations
{
    public partial class ControlPlayModeHandler
    {
        private static Dictionary<string, object> BuildStateCompletedResult(DomainReloadState state, string currentState)
        {
            return MergeJsonObjectPayload(state.initial_result_text, new Dictionary<string, object>
            {
                { "current_state", currentState },
                { "message", $"Play mode action completed: now in {currentState} mode" },
                { "editor_state", GetEditorStateHandler.CaptureState() }
            });
        }

        private Dictionary<string, object> BuildStateWaitTimeoutResult(DomainReloadState state)
        {
            return MergeJsonObjectPayload(state.initial_result_text, new Dictionary<string, object>
            {
                { "current_state", GetCurrentState() },
                { "message", "Play mode wait timed out before reaching the expected state" },
                { "editor_state", GetEditorStateHandler.CaptureState() }
            });
        }

        private Dictionary<string, object> BuildLogMatchedResult(
            DomainReloadState state,
            long startTime,
            long matchedTimestamp,
            long now)
        {
            var snapshot = GetCompactLogsSnapshot(
                state.log_since_ts,
                matchedTimestamp,
                state.wait_for_log.filter,
                LogSnapshotLimit);

            return MergeJsonObjectPayload(state.initial_result_text, new Dictionary<string, object>
            {
                { "current_state", state.expected_state },
                { "message", $"Play mode action completed: now in {state.expected_state} mode (matched log pattern: {state.wait_for_log.pattern})" },
                { "log_wait_found", true },
                { "log_wait_pattern", state.wait_for_log.pattern },
                { "log_wait_elapsed_ms", (int)(now - startTime) },
                { "logs", snapshot.logs },
                { "log_count", snapshot.log_count },
                { "logs_has_more", snapshot.logs_has_more },
                { "editor_state", GetEditorStateHandler.CaptureState() }
            });
        }

        private Dictionary<string, object> BuildLogWaitTimeoutResult(
            DomainReloadState state,
            long startTime,
            long now)
        {
            var snapshot = GetCompactLogsSnapshot(
                state.log_since_ts,
                null,
                state.wait_for_log.filter,
                LogSnapshotLimit);

            return MergeJsonObjectPayload(state.initial_result_text, new Dictionary<string, object>
            {
                { "current_state", GetCurrentState() },
                { "message", $"Play mode log wait timed out for pattern: {state.wait_for_log.pattern}" },
                { "log_wait_found", false },
                { "log_wait_pattern", state.wait_for_log.pattern },
                { "log_wait_timeout_ms", state.wait_for_log.timeout_ms },
                { "log_wait_elapsed_ms", (int)(now - startTime) },
                { "logs", snapshot.logs },
                { "log_count", snapshot.log_count },
                { "logs_has_more", snapshot.logs_has_more },
                { "editor_state", GetEditorStateHandler.CaptureState() }
            });
        }

        private static (List<LogEntryCompact> logs, int log_count, bool logs_has_more) GetCompactLogsSnapshot(
            long sinceTimestamp,
            long? untilTimestamp,
            string filter,
            int limit)
        {
            var logs = ConsoleLogCapture.instance.GetLogsFiltered(new LogFilterOptions
            {
                TypeFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter,
                Since = sinceTimestamp,
                Until = untilTimestamp,
                Limit = limit
            });

            var totalCount = ConsoleLogCapture.instance.CountLogs(new LogFilterOptions
            {
                TypeFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter,
                Since = sinceTimestamp,
                Until = untilTimestamp,
                Limit = 1
            });

            var compactLogs = new List<LogEntryCompact>(logs.Count);
            foreach (var log in logs)
            {
                compactLogs.Add(new LogEntryCompact
                {
                    message = log.message,
                    type = log.type,
                    timestamp = log.timestamp
                });
            }

            return (compactLogs, compactLogs.Count, totalCount > compactLogs.Count);
        }

        private static Dictionary<string, object> MergeJsonObjectPayload(string baseJson, object appendix)
        {
            if (string.IsNullOrEmpty(baseJson))
            {
                return appendix is Dictionary<string, object> dictionary
                    ? new Dictionary<string, object>(dictionary)
                    : SimpleJson.Parse(SimpleJson.Serialize(appendix));
            }

            var merged = SimpleJson.Parse(baseJson);
            var appendixObject = SimpleJson.Parse(SimpleJson.Serialize(appendix));

            foreach (var entry in appendixObject)
            {
                merged[entry.Key] = entry.Value;
            }

            return merged;
        }

        private string GetCurrentState()
        {
            if (!EditorApplication.isPlaying)
            {
                return "edit";
            }
            else if (EditorApplication.isPaused)
            {
                return "paused";
            }
            else
            {
                return "playing";
            }
        }
    }
}
