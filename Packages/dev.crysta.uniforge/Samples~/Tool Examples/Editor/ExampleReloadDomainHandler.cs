using System;
using UniForge.Tools;
using UnityEditor;

namespace UniForge.Samples.ToolExamples
{
    /// <summary>
    /// Shows a mutation that acknowledges a reload request and resumes by polling.
    /// Carries serializable state across domains and detects reload through static initialization.
    /// </summary>
    [Tool("example-reload-domain", Title = "Example: Reload Domain",
        Kind = ToolKind.Mutation, Destructive = false, Idempotent = false, Category = "Examples",
        Description = "Request a script domain reload and wait for it to complete")]
    [ToolOutput(typeof(Output))]
    public class ExampleReloadDomainHandler
        : DomainReloadResumableMutationHandler<ExampleReloadDomainHandler.State>
    {
        private const int PollIntervalMs = 250;
        private static long s_domainLoadedAtUnixMs;

        public class Args
        {
            [ToolParameter("Maximum time to wait for the reload in milliseconds", Default = 60000)]
            public int timeout_ms;
        }

        // JsonUtility carries TState's public fields, not handler instances or static values.
        [Serializable]
        public class State
        {
            public long requested_at_unix_ms;
        }

        public class AckOutput
        {
            public string status;
            public string message;
        }

        public class Output
        {
            public bool reloaded;
            public long elapsed_ms;
            public string message;
        }

        [InitializeOnLoadMethod]
        private static void RecordDomainLoadTime()
        {
            s_domainLoadedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // Outside the package assembly, protected internal is overridden as protected.
        protected override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            int timeoutMs = args.GetInt("timeout_ms", 60000);
            if (timeoutMs < 1)
                return ToolResult.Fail("timeout_ms must be at least 1");

            var state = new State
            {
                requested_at_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            EditorUtility.RequestScriptReload();
            var ack = new AckOutput
            {
                status = "reload_requested",
                message = "Script domain reload requested; waiting for a new domain"
            };
            return WaitForDomainReload(state, ack, timeoutMs, PollIntervalMs);
        }

        protected override ToolResult ResumeAfterDomainReload(State state, DomainReloadResumeContext context)
        {
            if (s_domainLoadedAtUnixMs > state.requested_at_unix_ms)
            {
                return ToolResult.Ok(new Output
                {
                    reloaded = true,
                    elapsed_ms = context.ElapsedMs,
                    message = "Script domain reload completed"
                });
            }

            if (context.IsTimedOut)
            {
                return ToolResult.Complete(new Output
                {
                    reloaded = false,
                    elapsed_ms = context.ElapsedMs,
                    message = "Timed out waiting for a script domain reload"
                }, success: false);
            }

            // Resume can run before the reload; keep polling until a new domain is detected.
            return ContinueAfterDomainReload(state, PollIntervalMs);
        }
    }
}
