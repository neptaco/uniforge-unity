using NUnit.Framework;
using UnityEngine;
using UniForge.Tools;

namespace UniForge.Tests
{
    [TestFixture]
    public class ToolDispatcherTests
    {
        [Test]
        public void DispatchAsync_UsesAsyncHandler()
        {
            var registry = new ToolRegistry();
            var handler = new AsyncTestMutationHandler();
            registry.Register(handler);

            var dispatcher = new ToolDispatcher(registry);
            var result = dispatcher.DispatchAsync("async-test-tool", "{}").GetAwaiter().GetResult();
            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, handler.AsyncExecuteCount);
        }

        [Test]
        public void ToolResultComplete_RejectsScalarStringPayload()
        {
            Assert.Throws<System.ArgumentException>(() => ToolResult.Complete("plain-text"));
        }

        private sealed class AsyncTestMutationHandler : MutationHandler
        {
            private readonly ToolDefinition _definition = new ToolDefinition
            {
                name = "async-test-tool",
                description = "Async test tool",
                annotations = new ToolAnnotations
                {
                    title = "Async Test Tool",
                    readOnlyHint = false,
                    destructiveHint = false,
                    idempotentHint = false,
                    openWorldHint = false
                }
            };

            public int AsyncExecuteCount { get; private set; }

            public override ToolDefinition Definition => _definition;

            protected internal override async Awaitable<ToolResult> ExecuteAsync(string argsJson)
            {
                AsyncExecuteCount++;
                await Awaitable.MainThreadAsync();
                return ToolResult.Complete(new { message = "async-result" });
            }
        }
    }
}
