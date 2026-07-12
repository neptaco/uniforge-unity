using System;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// シーンインスタンスの変更をプレハブに適用するツール
    /// </summary>
    [Tool("apply-prefab",
        Description = "Apply changes from a prefab instance in the scene to the source prefab asset",
        Title = "Apply Prefab",
        Category = ToolCategory.Prefab,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public partial class ApplyPrefabHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("GameObject path in hierarchy", Required = false)]
            public string path;

            [ToolParameter("GameObject instance ID", Required = false)]
            public int? instance_id;

            [ToolParameter("Scene name (optional)", Required = false)]
            public string scene;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string prefab_path;
            public string instance_path;
            public string message;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<ApplyPrefabHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var path = args.GetString("path");
            var instanceId = args.GetNullableInt("instance_id");
            var scene = args.GetString("scene");

            // GameObject を解決
            var resolveResult = GameObjectResolver.Resolve(path, instanceId, scene);
            if (!resolveResult.Success)
            {
                return ToolResult.Fail(resolveResult.Error);
            }

            var go = resolveResult.GameObject;

            // プレハブインスタンスかどうかを確認
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
            {
                return ToolResult.Fail($"GameObject '{go.name}' is not a prefab instance");
            }

            // プレハブのルートを取得
            var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (prefabRoot == null)
            {
                return ToolResult.Fail($"Could not find prefab root for '{go.name}'");
            }

            // プレハブアセットのパスを取得
            var prefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabRoot);
            if (string.IsNullOrEmpty(prefabAssetPath))
            {
                return ToolResult.Fail($"Could not find prefab asset path for '{go.name}'");
            }

            // 変更を適用
            try
            {
                PrefabUtility.ApplyPrefabInstance(prefabRoot, InteractionMode.AutomatedAction);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to apply prefab changes: {ex.Message}");
            }

            return ToolResult.Ok(new Output
            {
                success = true,
                prefab_path = prefabAssetPath,
                instance_path = GameObjectResolver.GetHierarchyPath(prefabRoot),
                message = $"Applied changes to prefab: {prefabAssetPath}"
            });
        }

    }
}
