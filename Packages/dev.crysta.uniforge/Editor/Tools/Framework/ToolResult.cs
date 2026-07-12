using System;
using System.Collections;
using System.Collections.Generic;

namespace UniForge.Tools
{
    /// <summary>
    /// High-level tool result state.
    /// </summary>
    public enum ToolResultKind
    {
        Complete,
        Fail,
        WaitForDomainReload
    }

    /// <summary>
    /// Tool execution result with an object payload that is serialized at the service boundary.
    /// </summary>
    public readonly struct ToolResult
    {
        /// <summary>Legacy success flag for completed results.</summary>
        public bool Success => Kind == ToolResultKind.Complete && ResponseSuccess;

        /// <summary>The result state for this execution step.</summary>
        public ToolResultKind Kind { get; }

        /// <summary>Whether the tool should be resumed after domain reload.</summary>
        public bool WaitsForDomainReload => Kind == ToolResultKind.WaitForDomainReload;

        /// <summary>Success flag for the eventual JSON-RPC response.</summary>
        public bool ResponseSuccess { get; }

        /// <summary>Structured result payload. This must serialize to a JSON object.</summary>
        public object ResultPayload { get; }

        /// <summary>Serialized JSON result payload.</summary>
        public string ResultText => ResultPayload != null ? SimpleJson.Serialize(ResultPayload) : null;

        /// <summary>Error message for failed results.</summary>
        public string Error { get; }

        internal string DomainReloadStateJson { get; }
        internal int DomainReloadTimeoutMs { get; }
        internal int DomainReloadNextPollMs { get; }

        private ToolResult(
            ToolResultKind kind,
            bool responseSuccess,
            object resultPayload,
            string error,
            string domainReloadStateJson,
            int domainReloadTimeoutMs,
            int domainReloadNextPollMs)
        {
            Kind = kind;
            ResponseSuccess = responseSuccess;
            ResultPayload = resultPayload;
            Error = error;
            DomainReloadStateJson = domainReloadStateJson;
            DomainReloadTimeoutMs = domainReloadTimeoutMs;
            DomainReloadNextPollMs = domainReloadNextPollMs;
        }

        /// <summary>Serialize an object as JSON and return it as a completed result.</summary>
        public static ToolResult Complete(object data, bool success = true)
        {
            ValidateResultPayload(data, nameof(data));
            return CompletePayload(data, success);
        }

        /// <summary>Backward-compatible alias for completed results.</summary>
        public static ToolResult Ok(object data)
        {
            return Complete(data);
        }

        /// <summary>Return a failed result.</summary>
        public static ToolResult Fail(string error)
        {
            return new ToolResult(ToolResultKind.Fail, false, null, error, null, 0, 0);
        }

        /// <summary>Return an error result from an exception.</summary>
        public static ToolResult FromException(Exception ex)
        {
            return Fail(ex.Message);
        }

        internal static ToolResult CompletePayload(object resultPayload, bool success = true)
        {
            ValidateResultPayload(resultPayload, nameof(resultPayload));
            return new ToolResult(ToolResultKind.Complete, success, resultPayload, null, null, 0, 0);
        }

        internal static ToolResult WaitForDomainReload(
            string stateJson,
            object ackResult,
            int timeoutMs,
            int nextPollMs)
        {
            if (string.IsNullOrEmpty(stateJson))
            {
                throw new ArgumentException("Domain reload state JSON is required.", nameof(stateJson));
            }

            if (ackResult != null)
            {
                ValidateResultPayload(ackResult, nameof(ackResult));
            }

            return new ToolResult(
                ToolResultKind.WaitForDomainReload,
                true,
                ackResult,
                null,
                stateJson,
                timeoutMs,
                Math.Max(0, nextPollMs));
        }

        private static void ValidateResultPayload(object payload, string paramName)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(paramName, "ToolResult payload must be a JSON object.");
            }

            if (payload is string or char or bool)
            {
                throw new ArgumentException("ToolResult payload must be a JSON object, not a scalar.", paramName);
            }

            var type = payload.GetType();
            if (type.IsPrimitive || payload is decimal || type.IsEnum)
            {
                throw new ArgumentException("ToolResult payload must be a JSON object, not a scalar.", paramName);
            }

            if (payload is IEnumerable && payload is not IDictionary)
            {
                throw new ArgumentException("ToolResult payload must be a JSON object, not an array.", paramName);
            }
        }
    }
}
