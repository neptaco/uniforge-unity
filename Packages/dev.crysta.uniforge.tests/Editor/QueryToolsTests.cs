using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UniForge.Tools;
using UniForge.Tools.Queries;
using UniForge.TestRunner;

namespace UniForge.Tests
{
    /// <summary>
    /// Query ツールのユニットテスト
    /// </summary>
    [TestFixture]
    public class QueryToolsTests
    {
        private sealed class EmptyComponent : MonoBehaviour
        {
        }

        private GameObject _testObject;
        private ToolRuntimeStateScope _runtimeStateScope;

        [SetUp]
        public void SetUp()
        {
            _runtimeStateScope = new ToolRuntimeStateScope();
            // テスト用オブジェクトを作成
            _testObject = new GameObject("QueryTestObject");
        }

        [TearDown]
        public void TearDown()
        {
            // テストオブジェクトをクリーンアップ
            if (_testObject != null)
            {
                Object.DestroyImmediate(_testObject);
            }

            _runtimeStateScope?.Dispose();
            _runtimeStateScope = null;
        }

        #region GetComponentPropertyHandler Tests

        [Test]
        public void GetComponentPropertyHandler_HasCorrectDefinition()
        {
            var handler = new GetComponentPropertyHandler();
            var definition = handler.Definition;

            Assert.AreEqual("component-property", definition.name);
            Assert.IsTrue(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
        }

        [Test]
        public void GetComponentPropertyHandler_Definition_DescribesAllPropertiesBehavior()
        {
            var handler = new GetComponentPropertyHandler();
            var definition = handler.Definition;

            var rootProperties = (Dictionary<string, object>)definition.inputSchema["properties"];
            var operations = (Dictionary<string, object>)rootProperties["operations"];
            var itemSchema = (Dictionary<string, object>)operations["items"];
            var itemProperties = (Dictionary<string, object>)itemSchema["properties"];
            var propertyNames = (Dictionary<string, object>)itemProperties["property_names"];

            Assert.That(propertyNames["description"], Does.Contain("Omit").IgnoreCase);
            Assert.That(propertyNames["description"], Does.Contain("all serialized properties").IgnoreCase);
        }

        [Test]
        public void GetComponentPropertyHandler_MissingOperations_Fails()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute("{}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("operations"));
        }

        [Test]
        public void GetComponentPropertyHandler_EmptyOperations_Fails()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute("{\"operations\": []}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("operations"));
        }

        [Test]
        public void GetComponentPropertyHandler_MissingComponentType_Fails()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}}}]}}");

            Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
            Assert.That(result.ResultText, Does.Contain("component_type"));
            Assert.That(result.ResultText, Does.Contain("\"success\":false"));
        }

        [Test]
        public void GetComponentPropertyHandler_InvalidComponentType_Fails()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"NonExistentComponent\"}}]}}");

            Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
            Assert.That(result.ResultText, Does.Contain("Component not found"));
            Assert.That(result.ResultText, Does.Contain("\"success\":false"));
        }

        [Test]
        public void GetComponentPropertyHandler_TransformComponent_ReturnsProperties()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"Transform\"}}]}}");

            Assert.IsTrue(result.Success);
            Assert.That(result.ResultText, Does.Contain("\"success\":true"));
            Assert.That(result.ResultText, Does.Contain("m_LocalPosition"));
        }

        [Test]
        public void GetComponentPropertyHandler_SpecificProperties_ReturnsOnlyRequested()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"Transform\", \"property_names\": [\"m_LocalPosition\"]}}]}}");

            Assert.IsTrue(result.Success);
            Assert.That(result.ResultText, Does.Contain("m_LocalPosition"));
            Assert.That(result.ResultText, Does.Not.Contain("m_LocalRotation"));
        }

        [Test]
        public void GetComponentPropertyHandler_InvalidPropertyName_ReportsError()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"Transform\", \"property_names\": [\"nonExistentProperty\"]}}]}}");

            Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
            Assert.That(result.ResultText, Does.Contain("Property not found"));
        }

        [Test]
        public void GetComponentPropertyHandler_MixedValidInvalidProperties_ReturnsPartialResult()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"Transform\", \"property_names\": [\"m_LocalPosition\", \"nonExistentProperty\"]}}]}}");

            Assert.IsTrue(result.Success);
            // 有効なプロパティは取得できる
            Assert.That(result.ResultText, Does.Contain("m_LocalPosition"));
            // エラーも報告される
            Assert.That(result.ResultText, Does.Contain("Property not found"));
        }

        [Test]
        public void GetComponentPropertyHandler_MultipleOperations_ProcessesAll()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"Transform\"}}, {{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"NonExistent\"}}]}}");

            Assert.IsTrue(result.Success);
            Assert.That(result.ResultText, Does.Contain("\"total\":2"));
            Assert.That(result.ResultText, Does.Contain("\"succeeded\":1"));
            Assert.That(result.ResultText, Does.Contain("\"failed\":1"));
        }

        [Test]
        public void GetComponentPropertyHandler_CaseInsensitiveComponentType_Works()
        {
            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"transform\"}}]}}");

            Assert.IsTrue(result.Success);
            Assert.That(result.ResultText, Does.Contain("\"success\":true"));
        }

        [Test]
        public void GetComponentPropertyHandler_ComponentWithoutSerializedFields_ReturnsEmptyProperties()
        {
            _testObject.AddComponent<EmptyComponent>();

            var handler = new GetComponentPropertyHandler();
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"EmptyComponent\"}}]}}");

            Assert.IsTrue(result.Success);
            Assert.That(result.ResultText, Does.Contain("\"property_count\":0"));
            Assert.That(result.ResultText, Does.Contain("\"properties\":{}"));
        }

        #endregion

        #region GetTestResultsHandler Tests

        [Test]
        public void GetTestResultsHandler_AbortedRun_ExposesAbortState()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var run = cache.CreateRun("EditMode");
            cache.AbortRun(run.runId, "Interrupted by domain reload", 0);

            var handler = new GetTestResultsHandler();
            var result = handler.Execute($"{{\"run_id\":\"{run.runId}\"}}");

            Assert.IsTrue(result.Success);
            Assert.That(result.ResultText, Does.Contain("\"aborted\":true"));
            Assert.That(result.ResultText, Does.Contain("Interrupted by domain reload"));
        }

        [Test]
        public void CompleteRun_AfterAbortRun_ResetsAbortState()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var run = cache.CreateRun("EditMode");
            // Domain Reload で先に AbortRun が呼ばれる
            cache.AbortRun(run.runId, "Interrupted by domain reload", 0);
            // テストが実際には完了し RunFinished → CompleteRun が呼ばれる
            cache.CompleteRun(run.runId, 4.5);

            Assert.IsFalse(run.aborted, "aborted should be reset after CompleteRun");
            Assert.IsNull(run.abortedReason, "abortedReason should be cleared after CompleteRun");
            Assert.IsTrue(run.completed);
            Assert.IsTrue(run.success);
        }

        [Test]
        public void GetTestResultsHandler_RunIdAndAfterRunId_Fails()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var anchor = cache.CreateRun("EditMode");
            cache.CompleteRun(anchor.runId, 0.1);
            var target = cache.CreateRun("EditMode");

            var handler = new GetTestResultsHandler();
            var result = handler.Execute(
                $"{{\"run_id\":\"{target.runId}\",\"after_run_id\":\"{anchor.runId}\"}}");

            Assert.AreEqual(ToolResultKind.Fail, result.Kind);
            Assert.That(result.Error, Does.Contain("run_id"));
            Assert.That(result.Error, Does.Contain("after_run_id"));
        }

        [Test]
        public void GetTestResultsHandler_WaitWithoutRunSelector_Fails()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var current = cache.CreateRun("EditMode");
            var handler = new GetTestResultsHandler();
            var result = handler.Execute("{\"wait\":true}");

            Assert.AreEqual(ToolResultKind.Fail, result.Kind);
            Assert.That(result.Error, Does.Contain("wait"));
            Assert.AreEqual(current.runId, cache.CurrentRunId,
                "wait without a selector must not implicitly target or mutate the current run");
            Assert.IsFalse(cache.GetRun(current.runId).completed);
        }

        [Test]
        public void GetTestResultsHandler_UnknownAfterRunId_FailsWithoutLatestRunFallback()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var latest = cache.CreateRun("EditMode");
            cache.CompleteRun(latest.runId, 0.1);

            const string MissingAnchor = "missing-anchor";
            var handler = new GetTestResultsHandler();
            var result = handler.Execute(
                $"{{\"after_run_id\":\"{MissingAnchor}\",\"wait\":true}}");

            Assert.AreEqual(ToolResultKind.Fail, result.Kind,
                "An unknown anchor must fail instead of falling back to the latest run");
            Assert.That(result.Error, Does.Contain(MissingAnchor));
            Assert.IsNull(result.ResultPayload,
                "The latest run must not be returned when the anchor is unknown");
            Assert.AreEqual(latest.runId, cache.GetLastRun().runId);
        }

        [Test]
        public void GetTestResultsHandler_AfterRunId_UsesFirstSuccessorAndKeepsTargetFixed()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var anchor = cache.CreateRun("EditMode");
            cache.CompleteRun(anchor.runId, 0.1);

            var firstSuccessor = cache.CreateRun("EditMode");
            cache.AddResult(firstSuccessor.runId, new TestResultEntry
            {
                fullName = "UniForge.Tests.FirstSuccessor",
                displayName = "FirstSuccessor",
                status = "Passed"
            });

            // Seed another successor before resolution so insertion order, rather
            // than latest-run fallback, determines the selected target.
            var laterSuccessor = cache.CreateRun("EditMode");
            cache.AddResult(laterSuccessor.runId, new TestResultEntry
            {
                fullName = "UniForge.Tests.LaterSuccessor",
                displayName = "LaterSuccessor",
                status = "Passed"
            });
            cache.CompleteRun(laterSuccessor.runId, 0.2);

            var handler = new GetTestResultsHandler();
            var started = handler.Execute(
                $"{{\"after_run_id\":\"{anchor.runId}\",\"wait\":true,\"timeout\":5000}}");

            Assert.IsTrue(started.WaitsForDomainReload);
            var fixedState = JsonUtility.FromJson<GetTestResultsHandler.DomainReloadState>(
                started.DomainReloadStateJson);
            Assert.AreEqual(anchor.runId, fixedState.after_run_id);
            Assert.AreEqual(firstSuccessor.runId, fixedState.target_run_id);

            // A run created after selection must not replace the fixed target.
            var newerRun = cache.CreateRun("EditMode");
            cache.AddResult(newerRun.runId, new TestResultEntry
            {
                fullName = "UniForge.Tests.NewerRun",
                displayName = "NewerRun",
                status = "Passed"
            });
            cache.CompleteRun(newerRun.runId, 0.2);

            var stillWaiting = ResumeGetTestResults(
                handler,
                started.DomainReloadStateJson,
                elapsedMs: 1000,
                timeoutMs: 5000);

            Assert.IsTrue(stillWaiting.WaitsForDomainReload);
            var resumedState = JsonUtility.FromJson<GetTestResultsHandler.DomainReloadState>(
                stillWaiting.DomainReloadStateJson);
            Assert.AreEqual(firstSuccessor.runId, resumedState.target_run_id);

            cache.CompleteRun(firstSuccessor.runId, 0.3);
            var completed = ResumeGetTestResults(
                handler,
                stillWaiting.DomainReloadStateJson,
                elapsedMs: 2000,
                timeoutMs: 5000);

            Assert.IsTrue(completed.Success);
            var output = (GetTestResultsHandler.Output)completed.ResultPayload;
            Assert.IsTrue(output.found);
            Assert.AreEqual(firstSuccessor.runId, output.target_run_id);
            Assert.AreEqual(firstSuccessor.runId, output.run_id);
            Assert.AreEqual(1, output.results.Count);
            Assert.AreEqual("UniForge.Tests.FirstSuccessor", output.results[0].full_name);
        }

        [Test]
        public void GetTestResultsHandler_AfterRunIdWithoutSuccessorAndWaitFalse_ReturnsFoundFalse()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var anchor = cache.CreateRun("EditMode");
            cache.CompleteRun(anchor.runId, 0.1);

            var handler = new GetTestResultsHandler();
            var result = handler.Execute(
                $"{{\"after_run_id\":\"{anchor.runId}\",\"wait\":false}}");

            Assert.IsTrue(result.Success);
            var output = (GetTestResultsHandler.Output)result.ResultPayload;
            Assert.IsFalse(output.found);
            Assert.IsNull(output.run_id);
            Assert.IsNull(output.target_run_id);
            Assert.AreEqual($"No run started after {anchor.runId}", output.message);
        }

        [Test]
        public void GetTestResultsHandler_WaitForSuccessor_ContinuesUntilCompletedAndPreservesState()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var anchor = cache.CreateRun("EditMode");
            cache.CompleteRun(anchor.runId, 0.1);

            var handler = new GetTestResultsHandler();
            var started = handler.Execute(
                $"{{\"after_run_id\":\"{anchor.runId}\",\"wait\":true," +
                "\"status_filter\":\"failed\",\"include_stack_trace\":false," +
                "\"limit\":1,\"timeout\":1234}");

            Assert.IsTrue(started.WaitsForDomainReload);
            var initialState = JsonUtility.FromJson<GetTestResultsHandler.DomainReloadState>(
                started.DomainReloadStateJson);
            Assert.AreEqual(anchor.runId, initialState.after_run_id);
            Assert.IsTrue(string.IsNullOrEmpty(initialState.target_run_id));
            Assert.IsTrue(initialState.wait);
            Assert.AreEqual("failed", initialState.status_filter);
            Assert.IsFalse(initialState.include_stack_trace);
            Assert.AreEqual(1, initialState.limit);
            Assert.AreEqual(1234, initialState.timeout);

            var successor = cache.CreateRun("EditMode");
            cache.AddResult(successor.runId, new TestResultEntry
            {
                fullName = "UniForge.Tests.Passed",
                displayName = "Passed",
                status = "Passed",
                stackTrace = "passed stack"
            });
            cache.AddResult(successor.runId, new TestResultEntry
            {
                fullName = "UniForge.Tests.Failed",
                displayName = "Failed",
                status = "Failed",
                message = "Expected failure",
                stackTrace = "failed stack"
            });

            var waitingForCompletion = ResumeGetTestResults(
                handler,
                started.DomainReloadStateJson,
                elapsedMs: 100,
                timeoutMs: 1234);

            Assert.IsTrue(waitingForCompletion.WaitsForDomainReload);
            var fixedState = JsonUtility.FromJson<GetTestResultsHandler.DomainReloadState>(
                waitingForCompletion.DomainReloadStateJson);
            Assert.AreEqual(anchor.runId, fixedState.after_run_id);
            Assert.AreEqual(successor.runId, fixedState.target_run_id);
            Assert.IsTrue(fixedState.wait);
            Assert.AreEqual("failed", fixedState.status_filter);
            Assert.IsFalse(fixedState.include_stack_trace);
            Assert.AreEqual(1, fixedState.limit);
            Assert.AreEqual(1234, fixedState.timeout);

            cache.CompleteRun(successor.runId, 0.5);
            var completed = ResumeGetTestResults(
                handler,
                waitingForCompletion.DomainReloadStateJson,
                elapsedMs: 200,
                timeoutMs: 1234);

            Assert.IsTrue(completed.Success);
            var output = (GetTestResultsHandler.Output)completed.ResultPayload;
            Assert.IsTrue(output.found);
            Assert.AreEqual(successor.runId, output.run_id);
            Assert.AreEqual(1, output.results.Count);
            Assert.AreEqual("Failed", output.results[0].status);
            Assert.IsNull(output.results[0].stack_trace);
        }

        [Test]
        public void GetTestResultsHandler_Timeout_DoesNotCancelRunningTest()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var anchor = cache.CreateRun("EditMode");
            cache.CompleteRun(anchor.runId, 0.1);

            var handler = new GetTestResultsHandler();
            var started = handler.Execute(
                $"{{\"after_run_id\":\"{anchor.runId}\",\"wait\":true,\"timeout\":1000}}");
            Assert.IsTrue(started.WaitsForDomainReload);

            // Most of the shared budget is spent waiting for the successor.
            var target = cache.CreateRun("EditMode");
            var targetDiscovered = ResumeGetTestResults(
                handler,
                started.DomainReloadStateJson,
                elapsedMs: 900,
                timeoutMs: 1000);

            Assert.IsTrue(targetDiscovered.WaitsForDomainReload);
            var targetState = JsonUtility.FromJson<GetTestResultsHandler.DomainReloadState>(
                targetDiscovered.DomainReloadStateJson);
            Assert.AreEqual(target.runId, targetState.target_run_id);

            var timedOut = ResumeGetTestResults(
                handler,
                targetDiscovered.DomainReloadStateJson,
                elapsedMs: 1000,
                timeoutMs: 1000);

            Assert.AreEqual(ToolResultKind.Complete, timedOut.Kind);
            Assert.IsFalse(timedOut.Success);
            Assert.IsFalse(timedOut.WaitsForDomainReload);
            var output = (GetTestResultsHandler.Output)timedOut.ResultPayload;
            Assert.IsTrue(output.found);
            Assert.IsTrue(output.timed_out);
            Assert.AreEqual(target.runId, output.target_run_id);
            Assert.AreEqual(target.runId, output.run_id);
            Assert.IsTrue(output.running);
            Assert.IsFalse(output.completed);
            Assert.That(output.message, Does.Contain("Timed out waiting for test results ("));

            var unchanged = cache.GetRun(target.runId);
            Assert.IsTrue(cache.IsRunning, "A query timeout must not cancel the test run");
            Assert.AreEqual(target.runId, cache.CurrentRunId);
            Assert.IsFalse(unchanged.completed);
            Assert.IsFalse(unchanged.aborted);

            // Reaching the total budget remains a timeout even if completion is
            // observed by that resume call; the completed state is still reported.
            cache.CompleteRun(target.runId, 0.2);
            var completedAtDeadline = ResumeGetTestResults(
                handler,
                targetDiscovered.DomainReloadStateJson,
                elapsedMs: 1000,
                timeoutMs: 1000);

            Assert.IsFalse(completedAtDeadline.Success);
            var completedOutput = (GetTestResultsHandler.Output)completedAtDeadline.ResultPayload;
            Assert.IsTrue(completedOutput.timed_out);
            Assert.IsTrue(completedOutput.completed);
            Assert.AreEqual(target.runId, completedOutput.target_run_id);
        }

        [Test]
        public void GetTestResultsHandler_DomainReloadAbortWaitsButTerminalAbortReturnsResult()
        {
            var cache = TestResultCache.instance;
            cache.Clear();

            var anchor = cache.CreateRun("EditMode");
            cache.CompleteRun(anchor.runId, 0.1);
            var target = cache.CreateRun("EditMode");
            cache.AbortRun(target.runId, TestResultCache.DomainReloadAbortReason, 0);

            var handler = new GetTestResultsHandler();
            var domainReloadAbort = handler.Execute(
                $"{{\"after_run_id\":\"{anchor.runId}\",\"wait\":true,\"timeout\":5000}}");

            Assert.IsTrue(domainReloadAbort.WaitsForDomainReload);
            var waitingState = JsonUtility.FromJson<GetTestResultsHandler.DomainReloadState>(
                domainReloadAbort.DomainReloadStateJson);
            Assert.AreEqual(target.runId, waitingState.target_run_id);

            var stillWaitingForCompletion = ResumeGetTestResults(
                handler,
                domainReloadAbort.DomainReloadStateJson,
                elapsedMs: 500,
                timeoutMs: 5000);
            Assert.IsTrue(stillWaitingForCompletion.WaitsForDomainReload,
                "A domain-reload abort is non-terminal and must continue waiting on resume");

            const string TerminalReason = "Cancelled by test";
            cache.AbortRun(target.runId, TerminalReason, 0.2);
            var terminalAbort = ResumeGetTestResults(
                handler,
                stillWaitingForCompletion.DomainReloadStateJson,
                elapsedMs: 1000,
                timeoutMs: 5000);

            Assert.IsTrue(terminalAbort.Success);
            Assert.IsFalse(terminalAbort.WaitsForDomainReload);
            var output = (GetTestResultsHandler.Output)terminalAbort.ResultPayload;
            Assert.IsTrue(output.found);
            Assert.AreEqual(target.runId, output.target_run_id);
            Assert.AreEqual(target.runId, output.run_id);
            Assert.IsTrue(output.completed);
            Assert.IsTrue(output.aborted);
            Assert.IsFalse(output.success);
            Assert.AreEqual(TerminalReason, output.aborted_reason);
        }

        private static ToolResult ResumeGetTestResults(
            GetTestResultsHandler handler,
            string stateJson,
            long elapsedMs,
            long timeoutMs)
        {
            const long RequestStartedAt = 1000;
            return ((IDomainReloadResumableTool)handler).ResumeAfterDomainReload(
                stateJson,
                new DomainReloadResumeContext(
                    RequestStartedAt,
                    RequestStartedAt + elapsedMs,
                    timeoutMs));
        }

        #endregion

        #region ComponentPropertyGetter Tests

        [Test]
        public void ComponentPropertyGetter_NullComponent_ReturnsError()
        {
            var result = ComponentPropertyGetter.GetProperties(null);

            Assert.IsFalse(result.AllSucceeded);
            Assert.AreEqual(0, result.properties.Count);
            Assert.That(result.errors, Has.Some.Contains("null"));
        }

        [Test]
        public void ComponentPropertyGetter_ValidComponent_ReturnsProperties()
        {
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform);

            Assert.IsTrue(result.AllSucceeded);
            Assert.Greater(result.properties.Count, 0);
            Assert.IsTrue(result.properties.ContainsKey("m_LocalPosition"));
        }

        [Test]
        public void ComponentPropertyGetter_SpecificProperties_ReturnsOnlyRequested()
        {
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform, new[] { "m_LocalPosition" });

            Assert.IsTrue(result.AllSucceeded);
            Assert.AreEqual(1, result.properties.Count);
            Assert.IsTrue(result.properties.ContainsKey("m_LocalPosition"));
        }

        [Test]
        public void ComponentPropertyGetter_InvalidPropertyName_RecordsError()
        {
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform, new[] { "nonExistentProperty" });

            Assert.IsFalse(result.AllSucceeded);
            Assert.AreEqual(0, result.properties.Count);
            Assert.That(result.errors, Has.Some.Contains("Property not found"));
        }

        [Test]
        public void ComponentPropertyGetter_MixedProperties_ReturnsPartialResult()
        {
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform, new[] { "m_LocalPosition", "nonExistent" });

            Assert.IsFalse(result.AllSucceeded); // エラーがあるため
            Assert.AreEqual(1, result.properties.Count);
            Assert.IsTrue(result.properties.ContainsKey("m_LocalPosition"));
            Assert.That(result.errors, Has.Some.Contains("Property not found"));
        }

        [Test]
        public void ComponentPropertyGetter_HasAnyProperty_WhenPartialSuccess()
        {
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform, new[] { "m_LocalPosition", "nonExistent" });

            Assert.IsFalse(result.AllSucceeded);
            Assert.IsTrue(result.HasAnyProperty);
        }

        [Test]
        public void ComponentPropertyGetter_EmptyPropertyName_RecordsError()
        {
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform, new[] { "" });

            Assert.IsFalse(result.AllSucceeded);
            Assert.That(result.errors, Has.Some.Contains("empty"));
        }

        [Test]
        public void ComponentPropertyGetter_Vector3Property_ReturnsArrayFormat()
        {
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform, new[] { "m_LocalPosition" });

            Assert.IsTrue(result.AllSucceeded);
            var position = result.properties["m_LocalPosition"] as float[];
            Assert.IsNotNull(position);
            Assert.AreEqual(3, position.Length);
        }

        [Test]
        public void ComponentPropertyGetter_QuaternionProperty_ReturnsEulerAndQuaternion()
        {
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform, new[] { "m_LocalRotation" });

            Assert.IsTrue(result.AllSucceeded);
            var rotation = result.properties["m_LocalRotation"] as System.Collections.Generic.Dictionary<string, object>;
            Assert.IsNotNull(rotation);
            Assert.IsTrue(rotation.ContainsKey("quaternion"));
            Assert.IsTrue(rotation.ContainsKey("euler"));
        }

        [Test]
        public void ComponentPropertyGetter_PropertyWithMPrefix_FoundAutomatically()
        {
            // "LocalPosition" で "m_LocalPosition" が見つかる
            var result = ComponentPropertyGetter.GetProperties(_testObject.transform, new[] { "LocalPosition" });

            Assert.IsTrue(result.AllSucceeded);
            Assert.AreEqual(1, result.properties.Count);
        }

        #endregion

        #region ObjectReference Tests

        [Test]
        public void ComponentPropertyGetter_NullObjectReference_ReturnsNull()
        {
            // AudioSource の audioClip は初期状態で null
            _testObject.AddComponent<AudioSource>();
            var audioSource = _testObject.GetComponent<AudioSource>();

            var result = ComponentPropertyGetter.GetProperties(audioSource, new[] { "m_audioClip" });

            Assert.IsTrue(result.AllSucceeded);
            Assert.IsNull(result.properties["m_audioClip"]);
        }

        #endregion
    }
}
