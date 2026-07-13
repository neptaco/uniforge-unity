using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UniForge.Tools.Mutations;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// get-gameobject ツールの出力
    /// </summary>
    public class GetGameObjectOutput
    {
        public string name;
        public int instanceId;
        public string path;
        public bool active;
        public bool activeSelf;
        public bool isStatic;
        public int layer;
        public string layerName;
        public string tag;
        public TransformInfo transform;
        public PrefabInfo prefab;
        public List<ComponentInfo> components;
    }

    /// <summary>
    /// Transform 情報
    /// </summary>
    public class TransformInfo
    {
        public float[] localPosition;
        public float[] localRotation;
        public float[] localScale;
        public float[] position;
        public float[] rotation;
    }

    /// <summary>
    /// プレハブ情報
    /// </summary>
    public class PrefabInfo
    {
        public bool isPrefabInstance;
        public string prefabAssetPath;
        public bool isPartOfPrefabAsset;
        public bool hasOverrides;
        public bool isVariant;
        public string basePrefabPath;
    }

    /// <summary>
    /// コンポーネント情報
    /// </summary>
    public class ComponentInfo
    {
        public string type;
        public bool enabled;
    }

    /// <summary>
    /// GameObject 詳細情報取得ツール
    /// </summary>
    [Tool("gameobject",
        Description = "Get detailed information about a GameObject by path or instance ID",
        Title = "Get GameObject",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Query,
        Idempotent = true)]
    [ToolOutput(typeof(GetGameObjectOutput))]
    public class GetGameObjectHandler : QueryHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Hierarchy path (e.g., 'Canvas/Panel')")]
            public string path;

            [ToolParameter("GameObject instance ID")]
            public int? instance_id;

            [ToolParameter("Scene name or path (for path search)")]
            public string scene;

            [ToolParameter("Include component list", Default = true)]
            public bool include_components;

            [ToolParameter("Include prefab information", Default = true)]
            public bool include_prefab_info;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);

            var pathArg = args.GetString("path");
            var instanceIdArg = args.HasKey("instance_id") ? (int?)args.GetInt("instance_id") : null;
            var sceneArg = args.GetString("scene");
            var includeComponents = args.GetBool("include_components", true);
            var includePrefabInfo = args.GetBool("include_prefab_info", true);

            var resolveResult = GameObjectResolver.Resolve(pathArg, instanceIdArg, sceneArg);
            if (!resolveResult.Success)
            {
                return ToolResult.Fail(resolveResult.Error);
            }

            return BuildResult(resolveResult.GameObject, includeComponents, includePrefabInfo);
        }

        private ToolResult BuildResult(GameObject go, bool includeComponents, bool includePrefabInfo)
        {
            var t = go.transform;

            var output = new GetGameObjectOutput
            {
                name = go.name,
                instanceId = go.GetInstanceID(),
                path = GameObjectResolver.GetHierarchyPath(go),
                active = go.activeInHierarchy,
                activeSelf = go.activeSelf,
                isStatic = go.isStatic,
                layer = go.layer,
                layerName = LayerMask.LayerToName(go.layer),
                tag = go.tag,
                transform = new TransformInfo
                {
                    localPosition = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
                    localRotation = new[] { t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w },
                    localScale = new[] { t.localScale.x, t.localScale.y, t.localScale.z },
                    position = new[] { t.position.x, t.position.y, t.position.z },
                    rotation = new[] { t.eulerAngles.x, t.eulerAngles.y, t.eulerAngles.z }
                }
            };

            // プレハブ情報
            if (includePrefabInfo)
            {
                var isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(go);
                var isVariant = false;
                string basePrefabPath = null;

                if (isPrefabInstance)
                {
                    var assetType = PrefabUtility.GetPrefabAssetType(go);
                    isVariant = assetType == PrefabAssetType.Variant;
                    if (isVariant)
                    {
                        var originalSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
                        if (originalSource != null)
                        {
                            basePrefabPath = AssetDatabase.GetAssetPath(originalSource);
                        }
                    }
                }

                // Prefab Stage 内ではルートGOがベースPrefabのインスタンスとして扱われるため、
                // Stage のアセット自体の型を確認する
                if (!isVariant)
                {
                    var stage = PrefabStageUtility.GetCurrentPrefabStage();
                    if (stage != null)
                    {
                        var stageAsset = AssetDatabase.LoadAssetAtPath<GameObject>(stage.assetPath);
                        if (stageAsset != null)
                        {
                            var stageAssetType = PrefabUtility.GetPrefabAssetType(stageAsset);
                            isVariant = stageAssetType == PrefabAssetType.Variant;
                            if (isVariant)
                            {
                                var originalSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(stageAsset);
                                if (originalSource != null)
                                {
                                    basePrefabPath = AssetDatabase.GetAssetPath(originalSource);
                                }
                            }
                        }
                    }
                }

                output.prefab = new PrefabInfo
                {
                    isPrefabInstance = isPrefabInstance,
                    prefabAssetPath = isPrefabInstance ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) : null,
                    isPartOfPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(go),
                    hasOverrides = isPrefabInstance && PrefabUtility.HasPrefabInstanceAnyOverrides(go, false),
                    isVariant = isVariant,
                    basePrefabPath = basePrefabPath
                };
            }

            // コンポーネント一覧
            if (includeComponents)
            {
                var components = new List<ComponentInfo>();
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue; // Missing script

                    var compInfo = new ComponentInfo
                    {
                        type = comp.GetType().Name
                    };

                    // enabled プロパティ（対応型は ComponentEnabledUtility に集約、書き込み側と対応を揃える）
                    compInfo.enabled = ComponentEnabledUtility.TryGetEnabled(comp, out var compEnabled)
                        ? compEnabled
                        : true; // Transform など enabled がないものは true

                    components.Add(compInfo);
                }
                output.components = components;
            }

            return ToolResult.Ok(output);
        }
    }
}
