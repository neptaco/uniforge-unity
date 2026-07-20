using System;
using System.Collections.Generic;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Shared domain-reload resume logic for compile-related tools.
    /// </summary>
    [Serializable]
    public class CompilationWaitState
    {
        public string initial_result_text;
        public long initial_compile_time;
        public bool was_compiling;
        public long compile_start_grace_deadline;
        public bool no_compile_is_ok;
    }

    [Serializable]
    public class CompilationTimeoutOutput
    {
        public string message;
        public bool timedOut;
    }

    [Serializable]
    public class CompilationDomainReloadOutput
    {
        public bool isCompiling;
        public bool success;
        public bool domainReloaded;
        public List<CompilerError> errors;
        public List<CompilerError> warnings;
        public string message;
    }

    public abstract class CompilationWaitMutationHandler
        : DomainReloadResumableMutationHandler<CompilationWaitState>
    {
        private const int CompilationPollIntervalMs = 250;

        protected ToolResult WaitForCompilation(
            CompileStatus currentStatus,
            object ackResult,
            int timeoutMs)
        {
            return WaitForCompilation(currentStatus, ackResult, timeoutMs, 0, false);
        }

        protected ToolResult WaitForCompilation(
            CompileStatus currentStatus,
            object ackResult,
            int timeoutMs,
            int compileStartGraceMs,
            bool noCompileIsOk)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return WaitForDomainReload(
                new CompilationWaitState
                {
                    initial_result_text = SimpleJson.Serialize(ackResult),
                    initial_compile_time = currentStatus.lastCompileTime,
                    was_compiling = currentStatus.isCompiling,
                    compile_start_grace_deadline = compileStartGraceMs > 0 ? now + compileStartGraceMs : 0,
                    no_compile_is_ok = noCompileIsOk
                },
                ackResult,
                timeoutMs,
                CompilationPollIntervalMs);
        }

        protected override ToolResult ResumeAfterDomainReload(
            CompilationWaitState state,
            DomainReloadResumeContext context)
        {
            var status = CompilationWatcher.Instance.GetStatus();

            if (status.isCompiling)
            {
                state.was_compiling = true;
            }

            bool completed = false;
            if (!status.isCompiling && state.was_compiling)
            {
                completed = true;
            }
            else if (status.lastCompileTime > 0 && status.lastCompileTime != state.initial_compile_time)
            {
                completed = true;
            }

            if (!completed && !context.IsTimedOut)
            {
                if (!state.was_compiling
                    && state.no_compile_is_ok
                    && state.compile_start_grace_deadline > 0
                    && context.CurrentTimeUnixMs >= state.compile_start_grace_deadline)
                {
                    return ToolResult.Complete(SimpleJson.Parse(state.initial_result_text));
                }

                return ContinueAfterDomainReload(state, CompilationPollIntervalMs);
            }

            if (!completed)
            {
                return ToolResult.Complete(new CompilationTimeoutOutput
                {
                    message = $"Compilation timed out after {context.TimeoutMs}ms",
                    timedOut = true
                }, success: false);
            }

            var domainReloaded =
                DomainReloadTracker.instance.LastDomainReloadTime > context.RequestStartedAtUnixMs &&
                DomainReloadTracker.instance.LastDomainReloadTime <= context.CurrentTimeUnixMs;

            if (domainReloaded)
            {
                var reloadSuccess = status.success && status.errors.Count == 0;

                return ToolResult.Complete(new CompilationDomainReloadOutput
                {
                    isCompiling = status.isCompiling,
                    success = reloadSuccess,
                    domainReloaded = true,
                    errors = status.errors,
                    warnings = status.warnings,
                    message = reloadSuccess
                        ? "Compilation completed successfully (domain reloaded)"
                        : $"Compilation completed with {status.errors.Count} error(s) after domain reload"
                }, success: reloadSuccess);
            }

            return ToolResult.Complete(status, success: status.success);
        }
    }
}
