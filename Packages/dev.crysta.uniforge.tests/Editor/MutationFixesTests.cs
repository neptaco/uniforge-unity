using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UniForge.Tools;
using UniForge.Tools.Mutations;
using UniForge.Tools.Queries;

namespace UniForge.Tests
{
    /// <summary>
    /// Mutation ツールのバグ修正・共通化に対するテスト
    /// （シーン解決、マテリアルインデックス検証、2Dプリミティブ永続化、
    /// プレハブ子削除、Apply/Revert、ComponentLookup、ComponentEnabledUtility）
    /// </summary>
    [TestFixture]
    public class MutationFixesTests
    {
        private ToolRuntimeStateScope _runtimeStateScope;

        [SetUp]
        public void SetUp()
        {
            _runtimeStateScope = new ToolRuntimeStateScope();
        }

        [TearDown]
        public void TearDown()
        {
            // Prefab Stage が開いたままならメインステージへ戻す
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                StageUtility.GoToMainStage();
            }

            // テスト中に作成された可能性のあるオブジェクトをクリーンアップ
            var testObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in testObjects)
            {
                if (obj != null && obj.name.StartsWith("MCP_Test_"))
                {
                    Object.DestroyImmediate(obj);
                }
            }

            _runtimeStateScope?.Dispose();
            _runtimeStateScope = null;
        }

        #region SceneHelper.TryResolveScene Tests

        [Test]
        public void TryResolveScene_UnknownSceneName_Fails()
        {
            var resolved = SceneHelper.TryResolveScene(
                "MCP_Test_NoSuchScene", includePrefabStage: true, out _, out var error);

            Assert.IsFalse(resolved);
            Assert.That(error, Does.Contain("Scene not found"));
        }

        [Test]
        public void TryResolveScene_EmptyName_ReturnsActiveScene()
        {
            var resolved = SceneHelper.TryResolveScene(
                null, includePrefabStage: false, out var scene, out var error);

            Assert.IsTrue(resolved);
            Assert.IsNull(error);
            Assert.AreEqual(EditorSceneManager.GetActiveScene(), scene);
        }

        [Test]
        public void GameObjectResolver_PrefabStageOpen_ResolvesAgainstStageScene()
        {
            var prefabAssetPath = "Assets/MCP_Test_StageRoot.prefab";

            // メインシーンに同名のオブジェクトを置き、Stage 側が優先されることを確認する
            var mainSceneDecoy = new GameObject("MCP_Test_StageRoot");
            GameObject prefabSource = null;

            try
            {
                prefabSource = new GameObject("MCP_Test_StageRoot_Source");
                prefabSource.name = "MCP_Test_StageRoot";
                // 同名オブジェクトが2つあると FindByPath がどちらを返すか不定になるため、
                // プレハブ保存後にソースは破棄する
                var asset = PrefabUtility.SaveAsPrefabAsset(prefabSource, prefabAssetPath);
                Assert.IsNotNull(asset);
                Object.DestroyImmediate(prefabSource);
                prefabSource = null;

                var stage = PrefabStageUtility.OpenPrefab(prefabAssetPath);
                Assert.IsNotNull(stage, "Prefab Stage should open");

                var result = GameObjectResolver.Resolve("MCP_Test_StageRoot", null);

                Assert.IsTrue(result.Success, result.Error);
                Assert.AreEqual(stage.scene, result.GameObject.scene,
                    "Path-based resolution should hit the Prefab Stage scene, not the main scene");
                Assert.AreNotEqual(mainSceneDecoy, result.GameObject);
            }
            finally
            {
                if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                {
                    StageUtility.GoToMainStage();
                }
                if (prefabSource != null)
                {
                    Object.DestroyImmediate(prefabSource);
                }
                Object.DestroyImmediate(mainSceneDecoy);
                AssetDatabase.DeleteAsset(prefabAssetPath);
            }
        }

        #endregion

        #region SetMaterialHandler Tests

        [Test]
        public void SetMaterialHandler_OutOfRangeMaterialIndex_FailsWithoutMutating()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "MCP_Test_SetMaterial";
            var renderer = go.GetComponent<Renderer>();
            var originalMaterial = renderer.sharedMaterial;
            var newMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));

            try
            {
                var handler = new SetMaterialHandler();
                var result = handler.Execute(
                    $"{{\"operations\": [{{\"instance_id\": {go.GetInstanceID()}, " +
                    $"\"material_instance_id\": {newMaterial.GetInstanceID()}, \"material_index\": 5}}]}}");

                Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
                Assert.That(result.ResultText, Does.Contain("material_index 5 out of range"));
                Assert.That(result.ResultText, Does.Contain("\"success\":false"));
                Assert.AreEqual(originalMaterial, renderer.sharedMaterial,
                    "Out-of-range index must not mutate any material slot");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(newMaterial);
            }
        }

        [Test]
        public void SetMaterialHandler_UnspecifiedIndex_SetsFirstSlot()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "MCP_Test_SetMaterialDefault";
            var renderer = go.GetComponent<Renderer>();
            var newMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));

            try
            {
                var handler = new SetMaterialHandler();
                var result = handler.Execute(
                    $"{{\"operations\": [{{\"instance_id\": {go.GetInstanceID()}, " +
                    $"\"material_instance_id\": {newMaterial.GetInstanceID()}}}]}}");

                Assert.IsTrue(result.Success);
                Assert.That(result.ResultText, Does.Contain("\"success\":true"));
                Assert.AreEqual(newMaterial, renderer.sharedMaterial);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(newMaterial);
            }
        }

        #endregion

        #region CreateGameObjectHandler 2D Primitive Tests

        [Test]
        public void CreateGameObjectHandler_2DPrimitive_UsesPersistedSpriteAsset()
        {
            GameObject created = null;
            var before = GeneratedAssetsSnapshot.Capture();

            try
            {
                var handler = new CreateGameObjectHandler();
                var result = handler.Execute(
                    "{\"objects\": [{\"name\": \"MCP_Test_Sprite2D\", \"primitive\": \"Sprite_Square\"}]}");

                Assert.IsTrue(result.Success);
                Assert.That(result.ResultText, Does.Contain("\"success\":true"));

                created = GameObject.Find("MCP_Test_Sprite2D");
                Assert.IsNotNull(created);

                var sprite = created.GetComponent<SpriteRenderer>().sprite;
                Assert.IsNotNull(sprite, "Sprite should be assigned");
                Assert.IsTrue(EditorUtility.IsPersistent(sprite),
                    "Sprite must be a persisted asset so the reference survives scene save/reload");
                Assert.AreEqual(
                    CreateGameObjectHandler.SpritePrimitiveCache.SquareSpriteAssetPath,
                    AssetDatabase.GetAssetPath(sprite));
            }
            finally
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
                DeleteGeneratedSpriteAssets(before);
            }
        }

        [Test]
        public void CreateGameObjectHandler_2DPrimitive_ReusesExistingSpriteAsset()
        {
            GameObject first = null;
            GameObject second = null;
            var before = GeneratedAssetsSnapshot.Capture();

            try
            {
                var handler = new CreateGameObjectHandler();
                handler.Execute("{\"objects\": [{\"name\": \"MCP_Test_Sprite2D_A\", \"primitive\": \"Sprite_Circle\"}]}");
                handler.Execute("{\"objects\": [{\"name\": \"MCP_Test_Sprite2D_B\", \"primitive\": \"Sprite_Circle\"}]}");

                first = GameObject.Find("MCP_Test_Sprite2D_A");
                second = GameObject.Find("MCP_Test_Sprite2D_B");
                Assert.IsNotNull(first);
                Assert.IsNotNull(second);

                var spriteA = first.GetComponent<SpriteRenderer>().sprite;
                var spriteB = second.GetComponent<SpriteRenderer>().sprite;
                Assert.IsNotNull(spriteA);
                Assert.AreEqual(spriteA, spriteB, "Same asset should be reused (stable file name)");
            }
            finally
            {
                if (first != null) Object.DestroyImmediate(first);
                if (second != null) Object.DestroyImmediate(second);
                DeleteGeneratedSpriteAssets(before);
            }
        }

        /// <summary>生成アセット・フォルダのテスト開始前の存在状態</summary>
        private readonly struct GeneratedAssetsSnapshot
        {
            public readonly bool SquareExisted;
            public readonly bool CircleExisted;
            public readonly bool GeneratedFolderExisted;
            public readonly bool UniForgeFolderExisted;

            private GeneratedAssetsSnapshot(bool square, bool circle, bool generatedFolder, bool uniforgeFolder)
            {
                SquareExisted = square;
                CircleExisted = circle;
                GeneratedFolderExisted = generatedFolder;
                UniForgeFolderExisted = uniforgeFolder;
            }

            public static GeneratedAssetsSnapshot Capture()
            {
                // AssetDatabase は未インポートのファイルを認識しないため、物理ファイルの存在も併せて確認する
                // （Auto Refresh 無効時に既存ファイルを「事前存在なし」と誤記録して削除しないように）
                var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
                bool ExistsOnDiskOrInDatabase(string assetPath) =>
                    AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null ||
                    System.IO.File.Exists(System.IO.Path.Combine(projectRoot, assetPath));
                bool FolderExists(string folderPath) =>
                    AssetDatabase.IsValidFolder(folderPath) ||
                    System.IO.Directory.Exists(System.IO.Path.Combine(projectRoot, folderPath));

                return new GeneratedAssetsSnapshot(
                    ExistsOnDiskOrInDatabase(CreateGameObjectHandler.SpritePrimitiveCache.SquareSpriteAssetPath),
                    ExistsOnDiskOrInDatabase(CreateGameObjectHandler.SpritePrimitiveCache.CircleSpriteAssetPath),
                    FolderExists("Assets/UniForge/Generated"),
                    FolderExists("Assets/UniForge"));
            }
        }

        /// <summary>
        /// テスト中に生成されたスプライトアセットと空になったフォルダを削除する。
        /// テスト開始前から存在していたアセット・フォルダはプロジェクト側の所有物なので削除しない
        /// （シーンが参照している可能性があるため）。
        /// </summary>
        private static void DeleteGeneratedSpriteAssets(GeneratedAssetsSnapshot before)
        {
            if (!before.SquareExisted)
                AssetDatabase.DeleteAsset(CreateGameObjectHandler.SpritePrimitiveCache.SquareSpriteAssetPath);
            if (!before.CircleExisted)
                AssetDatabase.DeleteAsset(CreateGameObjectHandler.SpritePrimitiveCache.CircleSpriteAssetPath);

            // テストが作った生成フォルダが空なら削除（テストがプロジェクトを汚さないように）
            if (!before.GeneratedFolderExisted &&
                AssetDatabase.IsValidFolder("Assets/UniForge/Generated") &&
                AssetDatabase.FindAssets(string.Empty, new[] { "Assets/UniForge/Generated" }).Length == 0)
            {
                AssetDatabase.DeleteAsset("Assets/UniForge/Generated");
            }
            if (!before.UniForgeFolderExisted &&
                AssetDatabase.IsValidFolder("Assets/UniForge") &&
                AssetDatabase.FindAssets(string.Empty, new[] { "Assets/UniForge" }).Length == 0)
            {
                AssetDatabase.DeleteAsset("Assets/UniForge");
            }
        }

        #endregion

        #region DeleteGameObjectHandler Tests

        [Test]
        public void DeleteGameObjectHandler_PrefabInstanceChild_PerItemErrorAndBatchContinues()
        {
            var prefabAssetPath = "Assets/MCP_Test_DelRoot.prefab";
            GameObject instance = null;
            GameObject other = null;

            try
            {
                // 子を持つプレハブを作成してシーンに配置
                var source = new GameObject("MCP_Test_DelRoot");
                var child = new GameObject("Child");
                child.transform.SetParent(source.transform);
                PrefabUtility.SaveAsPrefabAsset(source, prefabAssetPath);
                Object.DestroyImmediate(source);

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);

                other = new GameObject("MCP_Test_DelOther");

                var handler = new DeleteGameObjectHandler();
                var result = handler.Execute(
                    $"{{\"targets\": [{{\"path\": \"MCP_Test_DelRoot/Child\"}}, " +
                    $"{{\"instance_id\": {other.GetInstanceID()}}}]}}");

                Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
                Assert.That(result.ResultText, Does.Contain("prefab instance"));
                Assert.That(result.ResultText, Does.Contain("\"success\":false"));
                Assert.That(result.ResultText, Does.Contain("\"success\":true"),
                    "Batch must continue past the prefab-child failure");

                Assert.IsNotNull(instance.transform.Find("Child"), "Prefab child must not be deleted");
                Assert.IsTrue(other == null, "Second target should still be deleted");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                if (other != null) Object.DestroyImmediate(other);
                AssetDatabase.DeleteAsset(prefabAssetPath);
            }
        }

        [Test]
        public void DeleteGameObjectHandler_PrefabInstanceRoot_Succeeds()
        {
            var prefabAssetPath = "Assets/MCP_Test_DelRootOk.prefab";
            GameObject instance = null;

            try
            {
                var source = new GameObject("MCP_Test_DelRootOk");
                PrefabUtility.SaveAsPrefabAsset(source, prefabAssetPath);
                Object.DestroyImmediate(source);

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);

                var handler = new DeleteGameObjectHandler();
                var result = handler.Execute(
                    $"{{\"targets\": [{{\"instance_id\": {instance.GetInstanceID()}}}]}}");

                Assert.IsTrue(result.Success);
                Assert.That(result.ResultText, Does.Contain("\"success\":true"));
                Assert.IsTrue(instance == null, "Outermost prefab root should be deletable");
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(prefabAssetPath);
            }
        }

        #endregion

        #region PrefabStageHandler Tests

        [Test]
        public void PrefabStageHandler_Open_ReturnsRootInstanceId()
        {
            var prefabAssetPath = "Assets/MCP_Test_StageOpen.prefab";

            try
            {
                var source = new GameObject("MCP_Test_StageOpen");
                PrefabUtility.SaveAsPrefabAsset(source, prefabAssetPath);
                Object.DestroyImmediate(source);

                var handler = new PrefabStageHandler();
                var result = handler.Execute($"{{\"action\": \"open\", \"prefab_path\": \"{prefabAssetPath}\"}}");

                // 修正の契約: success を返すのは Stage が実際に開いた場合のみ
                if (result.Success)
                {
                    Assert.IsNotNull(PrefabStageUtility.GetCurrentPrefabStage(),
                        "Success must imply the Prefab Stage is actually open");
                    Assert.That(result.ResultText, Does.Contain("\"root_instance_id\""));
                }
                else
                {
                    // バッチモードなど UI なし環境で Stage が開かない場合はエラーになること
                    Assert.That(result.Error, Does.Contain("Prefab Stage did not open"));
                }
            }
            finally
            {
                if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                {
                    StageUtility.GoToMainStage();
                }
                AssetDatabase.DeleteAsset(prefabAssetPath);
            }
        }

        #endregion

        #region Apply/Revert Prefab Tests

        [Test]
        public void ApplyPrefabHandler_AppliesInstanceChangesToAsset()
        {
            var prefabAssetPath = "Assets/MCP_Test_Apply.prefab";
            GameObject instance = null;

            try
            {
                var source = new GameObject("MCP_Test_Apply");
                source.AddComponent<BoxCollider>();
                PrefabUtility.SaveAsPrefabAsset(source, prefabAssetPath);
                Object.DestroyImmediate(source);

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                // ルート Transform の position はプレハブオーバーライド扱いされないため、
                // コンポーネントプロパティの変更で Apply を検証する
                instance.GetComponent<BoxCollider>().size = new Vector3(2f, 3f, 4f);

                var handler = new ApplyPrefabHandler();
                var result = handler.Execute($"{{\"instance_id\": {instance.GetInstanceID()}}}");

                Assert.IsTrue(result.Success, result.Error);
                Assert.That(result.ResultText, Does.Contain("Applied changes to prefab"));
                Assert.That(result.ResultText, Does.Contain(prefabAssetPath));

                var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
                Assert.AreEqual(new Vector3(2f, 3f, 4f), reloaded.GetComponent<BoxCollider>().size);
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(prefabAssetPath);
            }
        }

        [Test]
        public void RevertPrefabHandler_RevertsInstanceToAsset()
        {
            var prefabAssetPath = "Assets/MCP_Test_Revert.prefab";
            GameObject instance = null;

            try
            {
                var source = new GameObject("MCP_Test_Revert");
                source.AddComponent<BoxCollider>();
                PrefabUtility.SaveAsPrefabAsset(source, prefabAssetPath);
                Object.DestroyImmediate(source);

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                instance.GetComponent<BoxCollider>().size = new Vector3(5f, 6f, 7f);

                var handler = new RevertPrefabHandler();
                var result = handler.Execute($"{{\"instance_id\": {instance.GetInstanceID()}}}");

                Assert.IsTrue(result.Success, result.Error);
                Assert.That(result.ResultText, Does.Contain("Reverted instance to prefab"));
                Assert.AreEqual(Vector3.one, instance.GetComponent<BoxCollider>().size);
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
                AssetDatabase.DeleteAsset(prefabAssetPath);
            }
        }

        [Test]
        public void ApplyPrefabHandler_NotPrefabInstance_Fails()
        {
            var go = new GameObject("MCP_Test_NotPrefab");

            try
            {
                var handler = new ApplyPrefabHandler();
                var result = handler.Execute($"{{\"instance_id\": {go.GetInstanceID()}}}");

                Assert.IsFalse(result.Success);
                Assert.That(result.Error, Does.Contain("is not a prefab instance"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region ComponentLookup Tests

        [Test]
        public void ComponentLookup_FindComponent_ByShortNameIgnoreCase()
        {
            var go = new GameObject("MCP_Test_Lookup");
            go.AddComponent<BoxCollider>();

            try
            {
                var found = ComponentLookup.FindComponent(go, "boxcollider");
                Assert.IsNotNull(found);
                Assert.IsInstanceOf<BoxCollider>(found);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComponentLookup_FindComponent_ByFullName()
        {
            var go = new GameObject("MCP_Test_Lookup");
            go.AddComponent<BoxCollider>();

            try
            {
                var found = ComponentLookup.FindComponent(go, "UnityEngine.BoxCollider");
                Assert.IsNotNull(found);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComponentLookup_FindComponent_NotFound_ReturnsNull()
        {
            var go = new GameObject("MCP_Test_Lookup");

            try
            {
                Assert.IsNull(ComponentLookup.FindComponent(go, "SphereCollider"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComponentLookup_FindComponents_ReturnsAllMatches()
        {
            var go = new GameObject("MCP_Test_Lookup");
            go.AddComponent<BoxCollider>();
            go.AddComponent<BoxCollider>();

            try
            {
                var matches = ComponentLookup.FindComponents(go, "BoxCollider");
                Assert.AreEqual(2, matches.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RemoveComponentHandler_RemoveAll_RemovesAllMatches()
        {
            var go = new GameObject("MCP_Test_RemoveAll");
            go.AddComponent<BoxCollider>();
            go.AddComponent<BoxCollider>();

            try
            {
                var handler = new RemoveComponentHandler();
                var result = handler.Execute(
                    $"{{\"operations\": [{{\"instance_id\": {go.GetInstanceID()}, " +
                    "\"component_type\": \"BoxCollider\", \"remove_all\": true}]}");

                Assert.IsTrue(result.Success);
                Assert.That(result.ResultText, Does.Contain("\"removed_count\":2"));
                Assert.AreEqual(0, go.GetComponents<BoxCollider>().Length);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion

        #region ComponentEnabledUtility Tests

        [Test]
        public void ComponentEnabledUtility_LODGroup_GetAndSet()
        {
            var go = new GameObject("MCP_Test_Enabled");
            var lodGroup = go.AddComponent<LODGroup>();

            try
            {
                Assert.IsTrue(ComponentEnabledUtility.TrySetEnabled(lodGroup, false));
                Assert.IsTrue(ComponentEnabledUtility.TryGetEnabled(lodGroup, out var enabled));
                Assert.IsFalse(enabled);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ComponentEnabledUtility_Transform_NotSupported()
        {
            var go = new GameObject("MCP_Test_Enabled");

            try
            {
                Assert.IsFalse(ComponentEnabledUtility.TryGetEnabled(go.transform, out _));
                Assert.IsFalse(ComponentEnabledUtility.TrySetEnabled(go.transform, false));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void GetGameObjectHandler_DisabledLODGroup_ReportsEnabledFalse()
        {
            var go = new GameObject("MCP_Test_LODGroupRead");
            var lodGroup = go.AddComponent<LODGroup>();
            lodGroup.enabled = false;

            try
            {
                var handler = new GetGameObjectHandler();
                var result = handler.Execute($"{{\"instance_id\": {go.GetInstanceID()}}}");

                Assert.IsTrue(result.Success, result.Error);
                Assert.That(result.ResultText, Does.Contain("\"type\":\"LODGroup\""));
                Assert.That(result.ResultText, Does.Contain("\"enabled\":false"),
                    "A disabled LODGroup must not be reported as enabled");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetComponentEnabledHandler_LODGroup_Disables()
        {
            var go = new GameObject("MCP_Test_LODGroupWrite");
            var lodGroup = go.AddComponent<LODGroup>();

            try
            {
                var handler = new SetComponentEnabledHandler();
                var result = handler.Execute(
                    $"{{\"operations\": [{{\"instance_id\": {go.GetInstanceID()}, " +
                    "\"component_type\": \"LODGroup\", \"enabled\": false}]}");

                Assert.IsTrue(result.Success);
                Assert.That(result.ResultText, Does.Contain("\"success\":true"));
                Assert.IsFalse(lodGroup.enabled);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SetComponentEnabledHandler_Transform_FailsWithError()
        {
            var go = new GameObject("MCP_Test_TransformEnable");

            try
            {
                var handler = new SetComponentEnabledHandler();
                var result = handler.Execute(
                    $"{{\"operations\": [{{\"instance_id\": {go.GetInstanceID()}, " +
                    "\"component_type\": \"Transform\", \"enabled\": false}]}");

                Assert.IsTrue(result.Success); // バッチ操作は常に成功を返す
                Assert.That(result.ResultText, Does.Contain("does not have an 'enabled' property"));
                Assert.That(result.ResultText, Does.Contain("\"success\":false"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        #endregion
    }
}
