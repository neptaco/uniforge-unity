using System;
using System.Collections.Generic;
using NUnit.Framework;
using UniForge.Tools;
using UniForge.Tools.Mutations;
using UnityEngine;

namespace UniForge.Tests
{
    [TestFixture]
    public class CompilationWaitHandlerTests
    {
        [Serializable]
        private class CompilationDomainReloadPayload
        {
            public bool success;
            public bool domainReloaded;
            public List<CompilerError> errors;
        }

        [Test]
        public void ResumeAfterDomainReload_ReportsErrorsWhenStatusHasErrorsAfterDomainReload()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            try
            {
                CompilationWatcher.Instance.SeedForTest(
                    isCompiling: false,
                    lastCompileEndTime: now,
                    success: false,
                    errors: new List<CompilerError>
                    {
                        new CompilerError("error CS0136: ...", "Test.cs", 10, 5, "error")
                    },
                    warnings: new List<CompilerError>());
                DomainReloadTracker.instance.MarkDomainReload();

                var result = ResumeAfterDomainReload(now);
                Assert.IsFalse(result.Success);

                var payload = JsonUtility.FromJson<CompilationDomainReloadPayload>(result.ResultText);
                Assert.IsTrue(payload.domainReloaded);
                Assert.IsFalse(payload.success);
                Assert.AreEqual(1, payload.errors.Count);
            }
            finally
            {
                CompilationWatcher.Instance.ResetForTest();
            }
        }

        [Test]
        public void ResumeAfterDomainReload_SucceedsAfterCleanDomainReload()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            try
            {
                CompilationWatcher.Instance.SeedForTest(
                    isCompiling: false,
                    lastCompileEndTime: now,
                    success: true,
                    errors: new List<CompilerError>(),
                    warnings: new List<CompilerError>());
                DomainReloadTracker.instance.MarkDomainReload();

                var result = ResumeAfterDomainReload(now);
                Assert.IsTrue(result.Success);

                var payload = JsonUtility.FromJson<CompilationDomainReloadPayload>(result.ResultText);
                Assert.IsTrue(payload.domainReloaded);
            }
            finally
            {
                CompilationWatcher.Instance.ResetForTest();
            }
        }

        private static ToolResult ResumeAfterDomainReload(long now)
        {
            var handler = new RequestCompileHandler();
            var stateJson = JsonUtility.ToJson(new CompilationWaitState
            {
                was_compiling = true,
                initial_compile_time = 0
            });
            var context = new DomainReloadResumeContext(
                requestStartedAtUnixMs: now - 60000,
                currentTimeUnixMs: now + 1000,
                timeoutMs: 300000);

            return ((IDomainReloadResumableTool)handler).ResumeAfterDomainReload(stateJson, context);
        }
    }
}
