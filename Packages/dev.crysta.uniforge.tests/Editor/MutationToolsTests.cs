using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UniForge.TestRunner;
using UniForge.Tools;
using UniForge.Tools.Mutations;

namespace UniForge.Tests
{
    /// <summary>
    /// Mutation ツールのユニットテスト
    /// </summary>
    [TestFixture]
    public class MutationToolsTests
    {
        private GameObject _testObject;
        private ToolRuntimeStateScope _runtimeStateScope;

        [SetUp]
        public void SetUp()
        {
            _runtimeStateScope = new ToolRuntimeStateScope();
            PendingDomainReloadToolRequestsStorage.instance.Clear();
            TestResultCache.instance.Clear();

            // テスト用オブジェクトを作成
            _testObject = new GameObject("TestObject");
        }

        [TearDown]
        public void TearDown()
        {
            // テストオブジェクトをクリーンアップ
            if (_testObject != null)
            {
                Object.DestroyImmediate(_testObject);
            }

            // テスト中に作成された可能性のある追加オブジェクトをクリーンアップ
            var testObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in testObjects)
            {
                if (obj.name.StartsWith("MCP_Test_"))
                {
                    Object.DestroyImmediate(obj);
                }
            }

            _runtimeStateScope?.Dispose();
            _runtimeStateScope = null;
        }

        #region GameObjectResolver Tests

        [Test]
        public void GameObjectResolver_ResolveByInstanceId_Success()
        {
            var result = GameObjectResolver.Resolve(null, _testObject.GetInstanceID());
            Assert.IsTrue(result.Success);
            Assert.AreEqual(_testObject, result.GameObject);
        }

        [Test]
        public void GameObjectResolver_ResolveByInvalidInstanceId_Fails()
        {
            var result = GameObjectResolver.Resolve(null, -99999);
            Assert.IsFalse(result.Success);
            Assert.IsNotNull(result.Error);
        }

        [Test]
        public void GameObjectResolver_ResolveWithoutPathOrId_Fails()
        {
            var result = GameObjectResolver.Resolve(null, null);
            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("required"));
        }

        [Test]
        public void GameObjectResolver_GetHierarchyPath_ReturnsCorrectPath()
        {
            var parent = new GameObject("Parent");
            var child = new GameObject("Child");
            child.transform.SetParent(parent.transform);

            try
            {
                var path = GameObjectResolver.GetHierarchyPath(child);
                Assert.AreEqual("Parent/Child", path);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        #endregion

        #region CreateGameObjectHandler Tests

        [Test]
        public void CreateGameObjectHandler_HasCorrectDefinition()
        {
            var handler = new CreateGameObjectHandler();
            var definition = handler.Definition;

            Assert.AreEqual("create-gameobject", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsFalse(definition.annotations.idempotentHint);
        }

        [Test]
        public void CreateGameObjectHandler_MissingName_Fails()
        {
            var handler = new CreateGameObjectHandler();
            // objects配列内のオブジェクトにnameがない場合
            // バッチ操作のため ToolResult.Success は true だが、結果JSONにエラーが含まれる
            var result = handler.Execute("{\"objects\": [{}]}");

            Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
            Assert.That(result.ResultText, Does.Contain("name"));
            Assert.That(result.ResultText, Does.Contain("\"success\":false"));
        }

        [Test]
        public void CreateGameObjectHandler_DefinitionIncludesCreatedObjectCollections()
        {
            var handler = new CreateGameObjectHandler();
            var definition = handler.Definition;

            var rootProperties = (Dictionary<string, object>)definition.outputSchema["properties"];
            Assert.IsTrue(rootProperties.ContainsKey("created_roots"));
            Assert.IsTrue(rootProperties.ContainsKey("created_objects"));
        }

        #endregion

        #region DeleteGameObjectHandler Tests

        [Test]
        public void DeleteGameObjectHandler_HasCorrectDefinition()
        {
            var handler = new DeleteGameObjectHandler();
            var definition = handler.Definition;

            Assert.AreEqual("delete-gameobject", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsTrue(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
        }

        [Test]
        public void DeleteGameObjectHandler_MissingPathAndId_Fails()
        {
            var handler = new DeleteGameObjectHandler();
            var result = handler.Execute("{}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("required"));
        }

        #endregion

        #region ModifyGameObjectHandler Tests

        [Test]
        public void ModifyGameObjectHandler_HasCorrectDefinition()
        {
            var handler = new ModifyGameObjectHandler();
            var definition = handler.Definition;

            Assert.AreEqual("modify-gameobject", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
        }

        #endregion

        #region SetTransformHandler Tests

        [Test]
        public void SetTransformHandler_HasCorrectDefinition()
        {
            var handler = new SetTransformHandler();
            var definition = handler.Definition;

            Assert.AreEqual("set-transform", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
        }

        [Test]
        public void SetTransformHandler_NoTransformParams_Fails()
        {
            var handler = new SetTransformHandler();
            // operations配列形式でposition/rotation/scaleがない場合
            // バッチ操作のため ToolResult.Success は true だが、結果JSONにエラーが含まれる
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}}}]}}");

            Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
            Assert.That(result.ResultText, Does.Contain("position").Or.Contain("rotation").Or.Contain("scale").Or.Contain("transform"));
            Assert.That(result.ResultText, Does.Contain("\"success\":false"));
        }

        #endregion

        #region SetParentHandler Tests

        [Test]
        public void SetParentHandler_HasCorrectDefinition()
        {
            var handler = new SetParentHandler();
            var definition = handler.Definition;

            Assert.AreEqual("set-parent", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
        }

        #endregion

        #region AddComponentHandler Tests

        [Test]
        public void AddComponentHandler_HasCorrectDefinition()
        {
            var handler = new AddComponentHandler();
            var definition = handler.Definition;

            Assert.AreEqual("add-component", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsFalse(definition.annotations.idempotentHint);
        }

        [Test]
        public void AddComponentHandler_MissingComponentType_Fails()
        {
            var handler = new AddComponentHandler();
            // operations配列形式でcomponent_typeがない場合
            // バッチ操作のため ToolResult.Success は true だが、結果JSONにエラーが含まれる
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}}}]}}");

            Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
            Assert.That(result.ResultText, Does.Contain("component_type"));
            Assert.That(result.ResultText, Does.Contain("\"success\":false"));
        }

        #endregion

        #region RemoveComponentHandler Tests

        [Test]
        public void RemoveComponentHandler_HasCorrectDefinition()
        {
            var handler = new RemoveComponentHandler();
            var definition = handler.Definition;

            Assert.AreEqual("remove-component", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsTrue(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
        }

        [Test]
        public void RemoveComponentHandler_CannotRemoveTransform()
        {
            var handler = new RemoveComponentHandler();
            // operations配列形式でTransformを削除しようとした場合
            // バッチ操作のため ToolResult.Success は true だが、結果JSONにエラーが含まれる
            var result = handler.Execute($"{{\"operations\": [{{\"instance_id\": {_testObject.GetInstanceID()}, \"component_type\": \"Transform\"}}]}}");

            Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
            Assert.That(result.ResultText, Does.Contain("Transform"));
            Assert.That(result.ResultText, Does.Contain("\"success\":false"));
        }

        #endregion

        #region SetComponentEnabledHandler Tests

        [Test]
        public void SetComponentEnabledHandler_HasCorrectDefinition()
        {
            var handler = new SetComponentEnabledHandler();
            var definition = handler.Definition;

            Assert.AreEqual("set-component-enabled", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
        }

        #endregion

        #region SaveSceneHandler Tests

        [Test]
        public void SaveSceneHandler_HasCorrectDefinition()
        {
            var handler = new SaveSceneHandler();
            var definition = handler.Definition;

            Assert.AreEqual("save-scene", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
            Assert.That(definition.description, Does.Contain("Cannot be used during play mode"));
        }

        [Test]
        public void SaveSceneHandler_PlayModeActive_FailsWithoutThrowing()
        {
            SaveSceneHandler.PlayModeActiveOverrideForTests = () => true;

            try
            {
                var handler = new SaveSceneHandler();
                var result = handler.Execute("{}");

                Assert.IsFalse(result.Success);
                Assert.That(result.Error, Does.Contain("play mode is active"));
            }
            finally
            {
                SaveSceneHandler.PlayModeActiveOverrideForTests = null;
            }
        }

        [Test]
        public void SaveSceneHandler_ClearsExplicitDirtyStateAfterSave()
        {
            var tempScenePath = "Assets/MCP_Test_SaveSceneHandler.unity";

            try
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, tempScenePath);

                // SaveSceneHandler の検証に絞るため、Unity の暗黙 dirty 伝播には依存しない。
                EditorSceneManager.MarkSceneDirty(scene);
                Assert.IsTrue(scene.isDirty);

                var handler = new SaveSceneHandler();
                var result = handler.Execute("{}");

                Assert.IsTrue(result.Success);
                Assert.That(result.ResultText, Does.Contain("\"active_scene_is_dirty_after_save\":false"));
                Assert.That(result.ResultText, Does.Contain("\"loaded_scenes_dirty_after_save\":[false]"));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(tempScenePath);
            }
        }

        #endregion

        #region LoadSceneHandler Tests

        [Test]
        public void LoadSceneHandler_HasCorrectDefinition()
        {
            var handler = new LoadSceneHandler();
            var definition = handler.Definition;

            Assert.AreEqual("load-scene", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsTrue(definition.annotations.destructiveHint);
            Assert.IsTrue(definition.annotations.idempotentHint);
        }

        [Test]
        public void LoadSceneHandler_MissingScenePath_Fails()
        {
            var handler = new LoadSceneHandler();
            var result = handler.Execute("{}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("scene_path"));
        }

        [Test]
        public void LoadSceneHandler_NonExistentScene_Fails()
        {
            var handler = new LoadSceneHandler();
            var result = handler.Execute("{\"scene_path\": \"Assets/NonExistent.unity\", \"save_current\": false}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("not found"));
        }

        #endregion

        #region NewSceneHandler Tests

        [Test]
        public void NewSceneHandler_HasCorrectDefinition()
        {
            var handler = new NewSceneHandler();
            var definition = handler.Definition;

            Assert.AreEqual("new-scene", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsTrue(definition.annotations.destructiveHint);
            Assert.IsFalse(definition.annotations.idempotentHint);
        }

        #endregion

        #region ControlPlayModeHandler Tests

        [Test]
        public void ControlPlayModeHandler_HasCorrectDefinition()
        {
            var handler = new ControlPlayModeHandler();
            var definition = handler.Definition;

            Assert.AreEqual("control-playmode", definition.name);
            Assert.IsFalse(definition.annotations.readOnlyHint);
            Assert.IsFalse(definition.annotations.destructiveHint);
            Assert.IsFalse(definition.annotations.idempotentHint);
        }

        [Test]
        public void ControlPlayModeHandler_DefinitionIncludesNestedWaitForLogSchema()
        {
            var handler = new ControlPlayModeHandler();
            var definition = handler.Definition;

            var rootProperties = (Dictionary<string, object>)definition.inputSchema["properties"];
            Assert.IsTrue(rootProperties.ContainsKey("wait_for_log"));

            var waitForLogSchema = (Dictionary<string, object>)rootProperties["wait_for_log"];
            Assert.AreEqual("object", waitForLogSchema["type"]);

            var nestedProperties = (Dictionary<string, object>)waitForLogSchema["properties"];
            Assert.IsTrue(nestedProperties.ContainsKey("pattern"));
            Assert.IsTrue(nestedProperties.ContainsKey("timeout_ms"));
            Assert.IsTrue(nestedProperties.ContainsKey("filter"));
            Assert.IsTrue(nestedProperties.ContainsKey("poll_interval_ms"));
        }

        [Test]
        public void ControlPlayModeHandler_MissingAction_Fails()
        {
            var handler = new ControlPlayModeHandler();
            var result = handler.Execute("{}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("action"));
        }

        [Test]
        public void ControlPlayModeHandler_InvalidAction_Fails()
        {
            var handler = new ControlPlayModeHandler();
            var result = handler.Execute("{\"action\": \"invalid\"}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("Invalid action"));
        }

        [Test]
        public void ControlPlayModeHandler_PauseWhenNotPlaying_Fails()
        {
            var handler = new ControlPlayModeHandler();
            // Edit モードでは pause できない
            var result = handler.Execute("{\"action\": \"pause\"}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("not in play mode"));
        }

        [Test]
        public void ControlPlayModeHandler_StopWithWaitForLog_ReturnsDomainReloadWait()
        {
            var handler = new ControlPlayModeHandler();
            var result = handler.Execute("{\"action\":\"stop\",\"wait_for_log\":{\"pattern\":\"ready\"}}");

            Assert.IsTrue(result.WaitsForDomainReload);
            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void ControlPlayModeHandler_StopWhenAlreadyStopped_IncludesEditorState()
        {
            var handler = new ControlPlayModeHandler();
            var result = handler.Execute("{\"action\":\"stop\"}");

            Assert.IsTrue(result.Success);
            Assert.That(result.ResultText, Does.Contain("\"editor_state\""));
            Assert.That(result.ResultText, Does.Contain("\"isPlaying\":false"));
        }

        [Test]
        public void ControlPlayModeHandler_StepWhenNotPlaying_Fails()
        {
            var handler = new ControlPlayModeHandler();
            // Edit モードでは step できない
            var result = handler.Execute("{\"action\": \"step\"}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("not in play mode"));
        }

        #endregion

        #region RunTestsHandler Tests

        [Test]
        public void RunTestsHandler_Execute_FailsWhenPendingResumeExists()
        {
            PendingDomainReloadToolRequestsStorage.instance.Add(new PendingDomainReloadToolRequest
            {
                requestId = "req-1",
                toolName = "run-tests",
                readyToSend = false
            });

            var handler = new RunTestsHandler();
            var result = handler.Execute("{}");

            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Does.Contain("waiting to resume after domain reload"));
        }

        [Test]
        public void RunTestsHandler_DefinitionOmitsWaitParameter()
        {
            var handler = new RunTestsHandler();
            var definition = handler.Definition;

            var rootProperties = (Dictionary<string, object>)definition.inputSchema["properties"];
            Assert.IsFalse(rootProperties.ContainsKey("wait"));
            Assert.IsTrue(rootProperties.ContainsKey("timeout"));
        }

        [Test]
        public void RunTestsHandler_ResumeAfterDomainReload_ContinuesWaitingForDomainReloadAbort()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.AbortRun(run.runId, TestResultCache.DomainReloadAbortReason, 0);

            var handler = new RunTestsHandler();
            var result = ResumeRunTests(handler, run.runId, run.startTime, elapsedMs: 1000, timeoutMs: 5000);

            Assert.IsTrue(result.WaitsForDomainReload);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void RunTestsHandler_ResumeAfterDomainReload_ContinuesWaitingWhenRunIsTemporarilyMissing()
        {
            var handler = new RunTestsHandler();
            var result = ResumeRunTests(handler, "missing-run", System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), elapsedMs: 1000, timeoutMs: 5000);

            Assert.IsTrue(result.WaitsForDomainReload);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void RunTestsHandler_ResumeAfterDomainReload_ReturnsAbortedResultAfterTimeout()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.AbortRun(run.runId, TestResultCache.DomainReloadAbortReason, 0);

            var handler = new RunTestsHandler();
            var result = ResumeRunTests(handler, run.runId, run.startTime, elapsedMs: 6000, timeoutMs: 5000);

            Assert.IsFalse(result.Success);
            Assert.IsFalse(result.WaitsForDomainReload);
            Assert.That(result.ResultText, Does.Contain("\"aborted\":true"));
            Assert.That(result.ResultText, Does.Contain(TestResultCache.DomainReloadAbortReason));
        }

        [Test]
        public void RunTestsHandler_ResumeAfterDomainReload_ReturnsCompletedResultWhenRunFinishes()
        {
            var cache = TestResultCache.instance;
            var run = cache.CreateRun("EditMode");
            cache.AbortRun(run.runId, TestResultCache.DomainReloadAbortReason, 0);
            cache.CompleteRun(run.runId, 1.25);

            var handler = new RunTestsHandler();
            var result = ResumeRunTests(handler, run.runId, run.startTime, elapsedMs: 1000, timeoutMs: 5000);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.WaitsForDomainReload);
            Assert.That(result.ResultText, Does.Contain("\"success\":true"));
            Assert.That(result.ResultText, Does.Contain("All tests passed"));
        }

        #endregion

        #region CaptureWindowHandler Tests

        [Test]
        public void CaptureWindowHandler_DefinitionIncludesReturnImage()
        {
            var handler = new CaptureWindowHandler();
            var definition = handler.Definition;

            var rootProperties = (Dictionary<string, object>)definition.inputSchema["properties"];
            Assert.IsTrue(rootProperties.ContainsKey("return_image"));
        }

        [Test]
        public void RefreshAssetsHandler_DefinitionIncludesWaitForReload()
        {
            var handler = new RefreshAssetsHandler();
            var definition = handler.Definition;

            var rootProperties = (Dictionary<string, object>)definition.inputSchema["properties"];
            Assert.IsTrue(rootProperties.ContainsKey("wait_for_reload"));
        }

        [Test]
        public void RefreshAssetsOutput_ExposesCompileObservationFields()
        {
            var output = new RefreshAssetsOutput
            {
                success = true,
                compile_started = false,
                is_compiling_after_refresh = false,
                waited_for_reload = false
            };

            var json = SimpleJson.Serialize(output);
            Assert.That(json, Does.Contain("compile_started"));
            Assert.That(json, Does.Contain("is_compiling_after_refresh"));
            Assert.That(json, Does.Contain("waited_for_reload"));
        }

        #endregion

        #region SimulateInputHandler Tests

        [Test]
        public void SimulateInputHandler_DefinitionIncludesUiHitFields()
        {
            var handler = new SimulateInputHandler();
            var definition = handler.Definition;

            var rootProperties = (Dictionary<string, object>)definition.outputSchema["properties"];
            Assert.IsTrue(rootProperties.ContainsKey("hit_ui"));
            Assert.IsTrue(rootProperties.ContainsKey("ui_hits"));
        }

        #endregion

        #region ToolArgsParser Extension Tests

        [Test]
        public void GetFloatArray_ValidArray_ReturnsArray()
        {
            var parser = new ToolArgsParser("{\"position\": [1.0, 2.0, 3.0]}");
            var result = parser.GetFloatArray("position");

            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(1.0f, result[0], 0.001f);
            Assert.AreEqual(2.0f, result[1], 0.001f);
            Assert.AreEqual(3.0f, result[2], 0.001f);
        }

        [Test]
        public void GetFloatArray_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            var result = parser.GetFloatArray("position");

            Assert.IsNull(result);
        }

        [Test]
        public void GetNullableInt_ExistingKey_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"value\": 42}");
            var result = parser.GetNullableInt("value");

            Assert.IsNotNull(result);
            Assert.AreEqual(42, result.Value);
        }

        [Test]
        public void GetNullableInt_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            var result = parser.GetNullableInt("value");

            Assert.IsNull(result);
        }

        [Test]
        public void GetNullableBool_ExistingKey_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"flag\": true}");
            var result = parser.GetNullableBool("flag");

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Value);
        }

        [Test]
        public void GetNullableBool_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            var result = parser.GetNullableBool("flag");

            Assert.IsNull(result);
        }

        #endregion

        private static ToolResult ResumeRunTests(
            RunTestsHandler handler,
            string runId,
            long runStartTime,
            long elapsedMs,
            long timeoutMs)
        {
            return ((IDomainReloadResumableTool)handler).ResumeAfterDomainReload(
                JsonUtility.ToJson(new RunTestsHandler.RunTestsWaitState
                {
                    run_id = runId,
                    run_start_time = runStartTime
                }),
                new DomainReloadResumeContext(runStartTime, runStartTime + elapsedMs, timeoutMs));
        }
    }
}
