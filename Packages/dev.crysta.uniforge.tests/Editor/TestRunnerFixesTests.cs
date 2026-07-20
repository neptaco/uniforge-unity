using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using UniForge.TestRunner;
using UniForge.Tools;
using UniForge.Tools.Mutations;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UniForge.Tests
{
    /// <summary>
    /// テストランナー周りの不具合修正のテスト
    /// (timeout 後の run 状態リーク、RunFinished 補正の永続化、AddResult の書き込み範囲など)
    /// </summary>
    [TestFixture]
    public class TestRunnerFixesTests
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

        #region Fix 1: timeout 時に run 状態をクリアする

        [Test]
        public void RunTestsHandler_ResumeAfterDomainReload_TimeoutAbortsRunState()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");

            var handler = new RunTestsHandler();
            var result = ResumeRunTests(handler, run.runId, run.startTime, elapsedMs: 6000, timeoutMs: 5000);

            Assert.IsFalse(result.Success);
            Assert.That(result.ResultText, Does.Contain("\"timed_out\":true"));

            // timeout 後に run 状態が残ると以後の run-tests がすべて拒否されるため、中断されていること
            Assert.IsFalse(cache.IsRunning);
            Assert.IsNull(cache.CurrentRunId);

            var abortedRun = cache.GetRun(run.runId);
            Assert.IsNotNull(abortedRun);
            Assert.IsTrue(abortedRun.completed);
            Assert.IsTrue(abortedRun.aborted);
            Assert.That(abortedRun.abortedReason, Does.Contain("Timed out"));
        }

        #endregion

        #region Fix 4: RunFinished のスイートレベル補正が永続化される

        [Test]
        public void RunFinished_SuiteLevelFailure_PersistsCorrectedSuccess()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.AddResult(run.runId, new TestResultEntry
            {
                fullName = "UniForge.Tests.Sample.Test",
                displayName = "Test",
                status = "Passed",
                durationSeconds = 0.1
            });

            // OneTimeSetUp 失敗などのスイートレベル失敗は leaf 結果に現れないが、
            // RunFinished の集計 (FailCount=1) には現れる
            var callbacks = new TestRunnerCallbacks(run.runId, run.startTime);
            callbacks.RunStarted(new FakeTestAdaptor { TestCaseCount = 1 });
            callbacks.RunFinished(new FakeRunResultAdaptor
            {
                PassCount = 1,
                FailCount = 1,
                SkipCount = 0
            });

            // ディスクから再読み込みしても補正後の値が残っていること
            Assert.IsTrue(TestRunPersistence.TryLoad(out var runs, out _));
            var reloaded = runs.Find(r => r.runId == run.runId);
            Assert.IsNotNull(reloaded);
            Assert.IsTrue(reloaded.completed);
            Assert.IsFalse(reloaded.success, "スイートレベル失敗の補正が永続化されていない");
            Assert.AreEqual(1, reloaded.passCount);
            Assert.AreEqual(1, reloaded.failCount);
            Assert.AreEqual(2, reloaded.totalCount);
        }

        #endregion

        #region Execution GUID による callback の帰属確認

        [Test]
        public void TestRunnerCallbacks_GuidMismatch_IgnoresRunStarted()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            var callbacks = new TestRunnerCallbacks(
                run.runId,
                run.startTime,
                (runId, executionGuid) =>
                    runId == run.runId && executionGuid == "current-guid");
            callbacks.BindExecutionGuid("stale-guid");

            callbacks.RunStarted(new FakeTestAdaptor { TestCaseCount = 1 });

            Assert.IsFalse(cache.GetRun(run.runId).runStarted);
        }

        [Test]
        public void TestRunnerCallbacks_GuidMismatch_IgnoresResultsForStartedRun()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.MarkRunStarted(run.runId);
            var completedEventRaised = false;
            var callbacks = new TestRunnerCallbacks(
                run.runId,
                run.startTime,
                (runId, executionGuid) =>
                    runId == run.runId && executionGuid == "current-guid");
            callbacks.BindExecutionGuid("stale-guid");
            callbacks.OnRunCompleted += _ => completedEventRaised = true;

            callbacks.TestFinished(new FakeRunResultAdaptor());
            callbacks.RunFinished(new FakeRunResultAdaptor());

            var record = cache.GetRun(run.runId);
            Assert.IsTrue(record.runStarted);
            Assert.IsEmpty(record.results);
            Assert.IsFalse(record.completed);
            Assert.IsFalse(callbacks.IsCompleted);
            Assert.IsFalse(completedEventRaised);
        }

        [Test]
        public void TestRunnerCallbacks_DelayedPreviousRunFinished_DoesNotCompleteNextRun()
        {
            var cache = TestResultCache.instance;
            var previousRun = cache.CreateRun("EditMode");
            cache.CompleteRun(previousRun.runId, 0.1);
            var nextRun = cache.CreateRun("EditMode");
            var completedEventRaised = false;
            var callbacks = new TestRunnerCallbacks(
                nextRun.runId,
                nextRun.startTime,
                (runId, executionGuid) =>
                    runId == nextRun.runId && executionGuid == "next-guid");
            callbacks.BindExecutionGuid("next-guid");
            callbacks.OnRunCompleted += _ => completedEventRaised = true;
            callbacks.RunStarted(new FakeTestAdaptor { TestCaseCount = 1 });
            Assert.IsTrue(cache.GetRun(nextRun.runId).runStarted);

            var delayedResult = new FakeRunResultAdaptor
            {
                PassCount = 1,
                StartTime = DateTimeOffset
                    .FromUnixTimeMilliseconds(nextRun.startTime - 1)
                    .UtcDateTime
            };
            callbacks.TestFinished(delayedResult);
            callbacks.RunFinished(delayedResult);

            var record = cache.GetRun(nextRun.runId);
            Assert.IsEmpty(record.results);
            Assert.IsFalse(record.completed);
            Assert.AreEqual(nextRun.runId, cache.CurrentRunId);
            Assert.IsFalse(completedEventRaised);

            callbacks.RunFinished(new FakeRunResultAdaptor
            {
                PassCount = 1,
                StartTime = DateTime.SpecifyKind(
                    DateTimeOffset
                        .FromUnixTimeMilliseconds(nextRun.startTime + 1)
                        .UtcDateTime,
                    DateTimeKind.Unspecified)
            });

            Assert.IsTrue(record.completed);
            Assert.IsTrue(completedEventRaised);
        }

        [Test]
        public void TestRunnerCallbacks_DefaultResultStartTime_AllowsCurrentRun()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("PlayMode");
            var callbacks = new TestRunnerCallbacks(
                run.runId,
                run.startTime,
                (runId, executionGuid) =>
                    runId == run.runId && executionGuid == "current-guid");
            callbacks.BindExecutionGuid("current-guid");
            callbacks.RunStarted(new FakeTestAdaptor { TestCaseCount = 1 });

            callbacks.RunFinished(new FakeRunResultAdaptor
            {
                StartTime = default
            });

            Assert.IsTrue(cache.GetRun(run.runId).completed);
        }

        #endregion

        #region Fix 1 補助: 最終中断済みの run を遅延 RunFinished が上書きしない

        [Test]
        public void CompleteRun_AfterFinalAbort_KeepsAbortedState()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.AbortRun(run.runId, "Timed out after 5000ms", 5.0);

            // timeout 中断後に遅れて届いた RunFinished 相当の完了処理
            cache.CompleteRun(run.runId, 6.0);

            var record = cache.GetRun(run.runId);
            Assert.IsNotNull(record);
            Assert.IsTrue(record.aborted, "最終中断済みの run が遅延完了で上書きされた");
            Assert.AreEqual("Timed out after 5000ms", record.abortedReason);
            Assert.IsFalse(record.success);
        }

        [Test]
        public void CompleteRun_AfterDomainReloadAbort_OverwritesAbort()
        {
            // domain reload による中断は実行再開後の完了で上書きされる従来動作を維持する
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.AbortRun(run.runId, TestResultCache.DomainReloadAbortReason, 0);

            cache.CompleteRun(run.runId, 1.0);

            var record = cache.GetRun(run.runId);
            Assert.IsNotNull(record);
            Assert.IsFalse(record.aborted);
            Assert.IsTrue(record.completed);
        }

        #endregion

        #region Fix 5: AddResult は現在の run のファイルのみ書き込む

        [Test]
        public void AddResult_WritesOnlyCurrentRunFile()
        {
            var cache = TestResultCache.instance;
            var previousRun = cache.CreateRun("EditMode");
            cache.CompleteRun(previousRun.runId, 0.5);

            var currentRun = cache.CreateRun("EditMode");

            // 過去のタイムスタンプを設定して、書き換えられたかどうかを検出する
            var oldTimestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var indexPath = Path.Combine(_tempDir, "index.json");
            var previousRunPath = Path.Combine(_tempDir, previousRun.runId + ".json");
            File.SetLastWriteTimeUtc(indexPath, oldTimestamp);
            File.SetLastWriteTimeUtc(previousRunPath, oldTimestamp);

            cache.AddResult(currentRun.runId, new TestResultEntry
            {
                fullName = "UniForge.Tests.Sample.CurrentTest",
                displayName = "CurrentTest",
                status = "Passed",
                durationSeconds = 0.1
            });

            // 現在の run のファイルには結果が書き込まれていること
            var currentRunPath = Path.Combine(_tempDir, currentRun.runId + ".json");
            Assert.IsTrue(File.Exists(currentRunPath));
            Assert.That(File.ReadAllText(currentRunPath), Does.Contain("UniForge.Tests.Sample.CurrentTest"));

            // index と他の run のファイルは書き換えられていないこと (O(N^2) I/O の防止)
            Assert.AreEqual(oldTimestamp, File.GetLastWriteTimeUtc(indexPath), "index.json が書き換えられた");
            Assert.AreEqual(oldTimestamp, File.GetLastWriteTimeUtc(previousRunPath), "他の run のファイルが書き換えられた");
        }

        #endregion

        #region Fix 2: 存在しないテスト名・0 件実行の扱い

        [Test]
        public void ExpandTestNames_FullTestName_ReturnsAsIsWithoutUnmatched()
        {
            var allTests = new List<TestInfo>
            {
                new TestInfo { fullName = "Foo.BarTests.TestA", hasChildren = false },
                new TestInfo { fullName = "Foo.BarTests", hasChildren = true }
            };

            var expanded = TestRunnerService.ExpandTestNames(
                new[] { "Foo.BarTests.TestA" }, allTests, out var unmatched);

            Assert.AreEqual(new[] { "Foo.BarTests.TestA" }, expanded);
            Assert.IsEmpty(unmatched);
        }

        [Test]
        public void ExpandTestNames_ClassPrefix_ExpandsToLeafTests()
        {
            var allTests = new List<TestInfo>
            {
                new TestInfo { fullName = "Foo.BarTests", hasChildren = true },
                new TestInfo { fullName = "Foo.BarTests.TestA", hasChildren = false },
                new TestInfo { fullName = "Foo.BarTests.TestB", hasChildren = false },
                new TestInfo { fullName = "Foo.OtherTests.TestC", hasChildren = false }
            };

            var expanded = TestRunnerService.ExpandTestNames(
                new[] { "Foo.BarTests" }, allTests, out var unmatched);

            CollectionAssert.AreEquivalent(
                new[] { "Foo.BarTests.TestA", "Foo.BarTests.TestB" }, expanded);
            Assert.IsEmpty(unmatched);
        }

        [Test]
        public void ExpandTestNames_UnmatchedName_IsReportedAsUnmatched()
        {
            var allTests = new List<TestInfo>
            {
                new TestInfo { fullName = "Foo.BarTests.TestA", hasChildren = false }
            };

            var expanded = TestRunnerService.ExpandTestNames(
                new[] { "Foo.MissingTests" }, allTests, out var unmatched);

            // 呼び出し側で失敗判定できるよう、そのまま通しつつ unmatched に記録される
            Assert.AreEqual(new[] { "Foo.MissingTests" }, expanded);
            Assert.AreEqual(new List<string> { "Foo.MissingTests" }, unmatched);
        }

        [Test]
        public void ExpandTestNames_EmptyCache_PassesThroughAndReportsUnmatched()
        {
            var expanded = TestRunnerService.ExpandTestNames(
                new[] { "Foo.BarTests.TestA" }, new List<TestInfo>(), out var unmatched);

            Assert.AreEqual(new[] { "Foo.BarTests.TestA" }, expanded);
            Assert.AreEqual(1, unmatched.Count);
        }

        [Test]
        public void RunTestsHandler_Resume_ZeroTestsWithExplicitFilter_Fails()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.CompleteRun(run.runId, 0.5); // 0 件実行 (leaf 結果なし)

            var handler = new RunTestsHandler();
            var result = ResumeRunTests(handler, run.runId, run.startTime, elapsedMs: 1000, timeoutMs: 5000, hasFilter: true);

            Assert.IsFalse(result.Success);
            Assert.That(result.ResultText, Does.Contain("No tests matched"));
        }

        [Test]
        public void RunTestsHandler_Resume_ZeroTestsWithoutFilter_Succeeds()
        {
            // フィルタ未指定 (全件実行でテストが 0 個のプロジェクト) は従来どおり成功扱い
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.CompleteRun(run.runId, 0.5);

            var handler = new RunTestsHandler();
            var result = ResumeRunTests(handler, run.runId, run.startTime, elapsedMs: 1000, timeoutMs: 5000, hasFilter: false);

            Assert.IsTrue(result.Success);
            Assert.That(result.ResultText, Does.Contain("All tests passed"));
        }

        #endregion

        #region Fix 3: TestInfo.assembly の解決

        [Test]
        public void ResolveAssemblyName_TypeInfo_UsesTypeAssembly()
        {
            var node = new FakeTestAdaptor
            {
                Name = "TestRunnerFixesTests",
                TypeInfo = new NUnit.Framework.Internal.TypeWrapper(typeof(TestRunnerFixesTests))
            };

            Assert.AreEqual("UniForge.Tests", TestRunnerService.ResolveAssemblyName(node, null));
        }

        [Test]
        public void ResolveAssemblyName_AssemblyNode_StripsDllSuffix()
        {
            var node = new FakeTestAdaptor
            {
                Name = "UniForge.Tests.dll",
                IsTestAssembly = true
            };

            Assert.AreEqual("UniForge.Tests", TestRunnerService.ResolveAssemblyName(node, null));
        }

        [Test]
        public void ResolveAssemblyName_SuiteWithoutTypeInfo_InheritsParent()
        {
            var node = new FakeTestAdaptor
            {
                Name = "UniForge",   // namespace suite
                IsTestAssembly = false,
                TypeInfo = null
            };

            Assert.AreEqual("UniForge.Tests", TestRunnerService.ResolveAssemblyName(node, "UniForge.Tests"));
        }

        #endregion

        #region Fix 4 補助: 中断済み run への集計補正は無視される

        [Test]
        public void ApplyRunSummary_AbortedRun_IsIgnored()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.AbortRun(run.runId, "Timed out after 5000ms", 5.0);

            cache.ApplyRunSummary(run.runId, 3, 0, 0, true);

            var record = cache.GetRun(run.runId);
            Assert.IsNotNull(record);
            Assert.IsTrue(record.aborted);
            Assert.IsFalse(record.success);
            Assert.AreEqual(0, record.passCount);
        }

        #endregion

        private static ToolResult ResumeRunTests(
            RunTestsHandler handler,
            string runId,
            long runStartTime,
            long elapsedMs,
            long timeoutMs,
            bool hasFilter = false)
        {
            return ((IDomainReloadResumableTool)handler).ResumeAfterDomainReload(
                JsonUtility.ToJson(new RunTestsHandler.RunTestsWaitState
                {
                    run_id = runId,
                    run_start_time = runStartTime,
                    has_filter = hasFilter
                }),
                new DomainReloadResumeContext(runStartTime, runStartTime + elapsedMs, timeoutMs));
        }

        /// <summary>
        /// ITestAdaptor の設定可能なフェイク（アセンブリ名解決のテスト用）
        /// </summary>
        private sealed class FakeTestAdaptor : ITestAdaptor
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string FullName { get; set; }
            public int TestCaseCount { get; set; }
            public bool HasChildren { get; set; }
            public bool IsSuite { get; set; }
            public IEnumerable<ITestAdaptor> Children { get; set; } = Array.Empty<ITestAdaptor>();
            public ITestAdaptor Parent { get; set; }
            public int TestCaseTimeout { get; set; }
            public ITypeInfo TypeInfo { get; set; }
            public IMethodInfo Method { get; set; }
            public object[] Arguments { get; set; }
            public string[] Categories { get; set; }
            public bool IsTestAssembly { get; set; }
            public UnityEditor.TestTools.TestRunner.Api.RunState RunState { get; set; }
            public string Description { get; set; }
            public string SkipReason { get; set; }
            public string ParentId { get; set; }
            public string ParentFullName { get; set; }
            public string UniqueName { get; set; }
            public string ParentUniqueName { get; set; }
            public int ChildIndex { get; set; }
            public TestMode TestMode { get; set; }
        }

        /// <summary>
        /// RunFinished に渡すスイートレベル集計のフェイク
        /// </summary>
        private sealed class FakeRunResultAdaptor : ITestResultAdaptor
        {
            public ITestAdaptor Test { get; set; } = new FakeTestAdaptor
            {
                Name = "FakeTest",
                FullName = "UniForge.Tests.FakeTest",
                Categories = Array.Empty<string>()
            };
            public string Name => "FakeRun";
            public string FullName => "FakeRun";
            public string ResultState => "Failed";
            public UnityEditor.TestTools.TestRunner.Api.TestStatus TestStatus
                => UnityEditor.TestTools.TestRunner.Api.TestStatus.Failed;
            public double Duration => 1.0;
            public DateTime StartTime { get; set; } = DateTime.UtcNow;
            public DateTime EndTime => DateTime.UtcNow;
            public string Message => null;
            public string StackTrace => null;
            public int AssertCount => 0;
            public int FailCount { get; set; }
            public int PassCount { get; set; }
            public int SkipCount { get; set; }
            public int InconclusiveCount => 0;
            public bool HasChildren => false;
            public IEnumerable<ITestResultAdaptor> Children => Array.Empty<ITestResultAdaptor>();
            public string Output => null;
            public TNode ToXml() => null;
        }
    }
}
