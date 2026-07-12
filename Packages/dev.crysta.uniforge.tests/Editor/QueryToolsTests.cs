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
