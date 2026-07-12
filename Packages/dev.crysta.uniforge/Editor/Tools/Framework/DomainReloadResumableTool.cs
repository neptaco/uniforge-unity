using UnityEditor;
using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>
    /// Context passed to tools that resume after a domain reload.
    /// </summary>
    public sealed class DomainReloadResumeContext
    {
        public long RequestStartedAtUnixMs { get; }
        public long CurrentTimeUnixMs { get; }
        public long TimeoutMs { get; }
        public long ElapsedMs => CurrentTimeUnixMs - RequestStartedAtUnixMs;
        public bool IsTimedOut => ElapsedMs > TimeoutMs;

        public DomainReloadResumeContext(long requestStartedAtUnixMs, long currentTimeUnixMs, long timeoutMs)
        {
            RequestStartedAtUnixMs = requestStartedAtUnixMs;
            CurrentTimeUnixMs = currentTimeUnixMs;
            TimeoutMs = timeoutMs;
        }
    }

    /// <summary>
    /// Implemented by tools that can resume after a domain reload.
    /// </summary>
    public interface IDomainReloadResumableTool
    {
        ToolResult ResumeAfterDomainReload(string stateJson, DomainReloadResumeContext context);
    }

    /// <summary>
    /// Shared stateless logic for domain-reload-resumable tools.
    /// </summary>
    internal static class DomainReloadHelper
    {
        public static ToolResult WaitForDomainReload<TState>(TState state, object ackResult, int timeoutMs, int nextPollMs = 0)
        {
            return ToolResult.WaitForDomainReload(
                JsonUtility.ToJson(state),
                ackResult,
                timeoutMs,
                nextPollMs);
        }

        public static ToolResult ContinueAfterDomainReload<TState>(TState state, int nextPollMs = 0)
        {
            return ToolResult.WaitForDomainReload(
                JsonUtility.ToJson(state),
                null,
                0,
                nextPollMs);
        }

        public static string Serialize<TState>(TState state)
        {
            return JsonUtility.ToJson(state);
        }

        public static TState Deserialize<TState>(string stateJson) where TState : class, new()
        {
            return JsonUtility.FromJson<TState>(stateJson) ?? new TState();
        }
    }

    /// <summary>
    /// Query tool base class with typed domain reload resume support.
    /// </summary>
    public abstract class DomainReloadResumableQueryHandler<TState> : QueryHandler, IDomainReloadResumableTool
        where TState : class, new()
    {
        protected ToolResult WaitForDomainReload(TState state, object ackResult, int timeoutMs, int nextPollMs = 0)
            => DomainReloadHelper.WaitForDomainReload(state, ackResult, timeoutMs, nextPollMs);

        protected ToolResult ContinueAfterDomainReload(TState state, int nextPollMs = 0)
            => DomainReloadHelper.ContinueAfterDomainReload(state, nextPollMs);

        protected virtual string SerializeDomainReloadState(TState state)
            => DomainReloadHelper.Serialize(state);

        protected virtual TState DeserializeDomainReloadState(string stateJson)
            => DomainReloadHelper.Deserialize<TState>(stateJson);

        protected abstract ToolResult ResumeAfterDomainReload(TState state, DomainReloadResumeContext context);

        ToolResult IDomainReloadResumableTool.ResumeAfterDomainReload(string stateJson, DomainReloadResumeContext context)
        {
            return ResumeAfterDomainReload(DeserializeDomainReloadState(stateJson), context);
        }
    }

    /// <summary>
    /// Mutation tool base class with typed domain reload resume support.
    /// </summary>
    public abstract class DomainReloadResumableMutationHandler<TState> : MutationHandler, IDomainReloadResumableTool
        where TState : class, new()
    {
        protected ToolResult WaitForDomainReload(TState state, object ackResult, int timeoutMs, int nextPollMs = 0)
            => DomainReloadHelper.WaitForDomainReload(state, ackResult, timeoutMs, nextPollMs);

        protected ToolResult ContinueAfterDomainReload(TState state, int nextPollMs = 0)
            => DomainReloadHelper.ContinueAfterDomainReload(state, nextPollMs);

        protected virtual string SerializeDomainReloadState(TState state)
            => DomainReloadHelper.Serialize(state);

        protected virtual TState DeserializeDomainReloadState(string stateJson)
            => DomainReloadHelper.Deserialize<TState>(stateJson);

        protected abstract ToolResult ResumeAfterDomainReload(TState state, DomainReloadResumeContext context);

        ToolResult IDomainReloadResumableTool.ResumeAfterDomainReload(string stateJson, DomainReloadResumeContext context)
        {
            var undoName = Definition.Title;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            try
            {
                return ResumeAfterDomainReload(DeserializeDomainReloadState(stateJson), context);
            }
            finally
            {
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            }
        }
    }
}
