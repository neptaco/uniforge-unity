using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// get-hierarchy ツールの出力
    /// </summary>
    public class GetHierarchyOutput
    {
        public SceneInfo scene;
        public string tree;
        public List<HierarchyObject> objects;
        public int totalCount;
    }

    /// <summary>
    /// シーン情報
    /// </summary>
    public class SceneInfo
    {
        public string name;
        public string path;
        public int rootCount;
    }

    /// <summary>
    /// ヒエラルキーオブジェクト
    /// </summary>
    public class HierarchyObject
    {
        public string name;
        public int instanceId;
        public int? parentId;
        public bool active;
    }

    /// <summary>
    /// シーンヒエラルキー取得ツール（ハイブリッド形式）
    /// </summary>
    [Tool("hierarchy",
        Description = "Get scene hierarchy as text tree and flat object list",
        Title = "Get Hierarchy",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Query,
        Idempotent = true)]
    [ToolOutput(typeof(GetHierarchyOutput))]
    public class GetHierarchyHandler : QueryHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Scene name or path (defaults to active scene)")]
            public string scene;

            [ToolParameter("Root object name to start from")]
            public string root;

            [ToolParameter("Maximum depth to traverse (-1 for unlimited)", Default = -1)]
            public int depth;

            [ToolParameter("Include inactive GameObjects", Default = true)]
            public bool include_inactive;

            [ToolParameter("Name filter (regex pattern)")]
            public string name_filter;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);

            var sceneName = args.GetString("scene");
            var rootName = args.GetString("root");
            var maxDepth = args.GetInt("depth", -1);
            var includeInactive = args.GetBool("include_inactive", true);
            var nameFilter = args.GetString("name_filter");

            // シーン取得（Prefab Stage が開いていればそちらを優先）
            if (!SceneHelper.TryResolveScene(sceneName, includePrefabStage: true, out var scene, out var sceneError))
            {
                return ToolResult.Fail(sceneError);
            }

            // 名前フィルタ正規表現
            Regex nameRegex = null;
            if (!string.IsNullOrEmpty(nameFilter))
            {
                try
                {
                    nameRegex = new Regex(nameFilter, RegexOptions.IgnoreCase);
                }
                catch
                {
                    nameRegex = new Regex(Regex.Escape(nameFilter), RegexOptions.IgnoreCase);
                }
            }

            var rootObjects = scene.GetRootGameObjects();
            var treeBuilder = new StringBuilder();
            var objectsList = new List<HierarchyObject>();

            foreach (var rootObj in rootObjects)
            {
                // ルート名フィルタ
                if (!string.IsNullOrEmpty(rootName) && rootObj.name != rootName)
                {
                    continue;
                }

                TraverseHierarchy(
                    rootObj.transform,
                    null, // parentId
                    0,    // currentDepth
                    maxDepth,
                    includeInactive,
                    nameRegex,
                    treeBuilder,
                    objectsList
                );
            }

            var output = new GetHierarchyOutput
            {
                scene = new SceneInfo
                {
                    name = scene.name,
                    path = scene.path,
                    rootCount = rootObjects.Length
                },
                tree = treeBuilder.ToString().TrimEnd('\n'),
                objects = objectsList,
                totalCount = objectsList.Count
            };

            return ToolResult.Ok(output);
        }

        /// <summary>
        /// ヒエラルキーを走査し、マッチしたオブジェクトとその祖先を含める。
        /// 戻り値: このノードまたは子孫にマッチがあった場合 true。
        /// </summary>
        private bool TraverseHierarchy(
            Transform transform,
            int? parentId,
            int currentDepth,
            int maxDepth,
            bool includeInactive,
            Regex nameRegex,
            StringBuilder treeBuilder,
            List<HierarchyObject> objectsList)
        {
            var go = transform.gameObject;

            // 非アクティブフィルタ
            if (!includeInactive && !go.activeInHierarchy)
            {
                return false;
            }

            var instanceId = go.GetInstanceID();
            bool selfMatches = nameRegex == null || nameRegex.IsMatch(go.name);

            // フィルタなし: 従来通りそのまま追加
            if (nameRegex == null)
            {
                AppendNode(treeBuilder, objectsList, go, instanceId, parentId, currentDepth);

                if (maxDepth < 0 || currentDepth < maxDepth)
                {
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        TraverseHierarchy(
                            transform.GetChild(i), instanceId,
                            currentDepth + 1, maxDepth, includeInactive, null,
                            treeBuilder, objectsList);
                    }
                }
                return true;
            }

            // フィルタあり: 子孫を先にバッファに走査し、マッチ有無を判定
            bool anyDescendantMatches = false;
            var childTree = new StringBuilder();
            var childObjects = new List<HierarchyObject>();

            if (maxDepth < 0 || currentDepth < maxDepth)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    if (TraverseHierarchy(
                        transform.GetChild(i), instanceId,
                        currentDepth + 1, maxDepth, includeInactive, nameRegex,
                        childTree, childObjects))
                    {
                        anyDescendantMatches = true;
                    }
                }
            }

            // 自身がマッチ、または子孫にマッチがあれば含める
            if (selfMatches || anyDescendantMatches)
            {
                AppendNode(treeBuilder, objectsList, go, instanceId, parentId, currentDepth);
                treeBuilder.Append(childTree);
                objectsList.AddRange(childObjects);
                return true;
            }

            return false;
        }

        private static void AppendNode(
            StringBuilder treeBuilder, List<HierarchyObject> objectsList,
            GameObject go, int instanceId, int? parentId, int depth)
        {
            for (int i = 0; i < depth; i++)
                treeBuilder.Append("  ");
            treeBuilder.Append(go.name);
            treeBuilder.Append(" [");
            treeBuilder.Append(instanceId);
            treeBuilder.Append("]\n");

            objectsList.Add(new HierarchyObject
            {
                name = go.name,
                instanceId = instanceId,
                parentId = parentId,
                active = go.activeInHierarchy
            });
        }
    }
}
