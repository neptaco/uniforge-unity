using System;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// シーンインスタンスをプレハブの状態に戻すツール
    /// </summary>
    [Tool("revert-prefab",
        Description = "Revert a prefab instance in the scene to match the source prefab asset",
        Title = "Revert Prefab",
        Category = ToolCategory.Prefab,
        Kind = ToolKind.Mutation,
        Destructive = true,
        Idempotent = true)]
    public partial class RevertPrefabHandler : MutationHandler
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
            => _definition ??= ToolDefinitionBuilder.FromHandler<RevertPrefabHandler>();

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

            var instancePath = GameObjectResolver.GetHierarchyPath(prefabRoot);

            // 変更を元に戻す
            try
            {
                PrefabUtility.RevertPrefabInstance(prefabRoot, InteractionMode.AutomatedAction);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to revert prefab instance: {ex.Message}");
            }

            return ToolResult.Ok(new Output
            {
                success = true,
                prefab_path = prefabAssetPath,
                instance_path = instancePath,
                message = $"Reverted instance to prefab: {prefabAssetPath}"
            });
        }

    }
}
