using System;
using System.IO;
using NUnit.Framework;
using UniForge.TestRunner;

namespace UniForge.Tests
{
    [TestFixture]
    public class TestRunPersistenceTests
    {
        private string _tempDir;
        private ToolRuntimeStateScope _runtimeStateScope;

        [SetUp]
        public void SetUp()
        {
            _runtimeStateScope = new ToolRuntimeStateScope();
            _tempDir = Path.Combine(Path.GetTempPath(), "uniforge-test-runs", Guid.NewGuid().ToString("N"));
            TestRunPersistence.BaseDirectoryOverrideForTests = () => _tempDir;
            TestResultCache.instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            TestRunPersistence.BaseDirectoryOverrideForTests = () => _tempDir;
            TestResultCache.instance.Clear();
            TestRunPersistence.BaseDirectoryOverrideForTests = null;

            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }

            _runtimeStateScope?.Dispose();
            _runtimeStateScope = null;
        }

        [Test]
        public void TestResultCache_PersistsRunsToLibraryState()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.AddResult(run.runId, new TestResultEntry
            {
                fullName = "UniForge.Tests.Sample.Test",
                displayName = "Test",
                status = "Passed",
                durationSeconds = 0.5
            });
            cache.CompleteRun(run.runId, 1.25);

            Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "index.json")));
            Assert.IsTrue(File.Exists(Path.Combine(_tempDir, run.runId + ".json")));

            Assert.IsTrue(TestRunPersistence.TryLoad(out var runs, out var currentRunId));
            Assert.AreEqual(1, runs.Count);
            Assert.AreEqual(run.runId, runs[0].runId);
            Assert.IsNull(currentRunId);
            Assert.IsTrue(runs[0].completed);
            Assert.AreEqual(1, runs[0].passCount);
        }

        [Test]
        public void TestResultCache_Clear_RemovesPersistedFiles()
        {
            var cache = TestResultCache.instance;
            cache.CreateRun("EditMode");

            Assert.IsTrue(Directory.Exists(_tempDir));

            cache.Clear();

            Assert.IsFalse(Directory.Exists(_tempDir));
        }

        [Test]
        public void HandleEditorLoad_FirstLoadInSession_AbortsOrphanedRun()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");

            TestResultCacheInitializer.HandleEditorLoad(cache, firstLoadInSession: true);

            var reloadedRun = cache.GetRun(run.runId);
            Assert.IsNotNull(reloadedRun);
            Assert.IsTrue(reloadedRun.completed);
            Assert.IsTrue(reloadedRun.aborted);
            Assert.AreEqual(TestResultCache.EditorRestartAbortReason, reloadedRun.abortedReason);
            Assert.IsFalse(cache.IsRunning);
            Assert.IsNull(cache.CurrentRunId);
        }

        [Test]
        public void HandleEditorLoad_DomainReload_KeepsActiveRunRunning()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");

            TestResultCacheInitializer.HandleEditorLoad(cache, firstLoadInSession: false);

            var reloadedRun = cache.GetRun(run.runId);
            Assert.IsNotNull(reloadedRun);
            Assert.IsFalse(reloadedRun.completed);
            Assert.IsFalse(reloadedRun.aborted);
            Assert.IsTrue(cache.IsRunning);
            Assert.AreEqual(run.runId, cache.CurrentRunId);
        }
    }
}
