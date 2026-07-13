using System;
using UnityEditor;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// プレハブインスタンスに対する Apply / Revert 操作の共通処理。
    /// GameObject 解決 → プレハブインスタンス検証 → ルート取得 → アセットパス検証までを共通化し、
    /// 実際の PrefabUtility 呼び出しだけを操作種別で切り替える。
    /// </summary>
    internal static class PrefabInstanceOperation
    {
        /// <summary>操作の種類</summary>
        internal enum Kind
        {
            Apply,
            Revert
        }

        /// <summary>出力定義（ApplyPrefabHandler.Output / RevertPrefabHandler.Output と同一のワイヤ形式）</summary>
        private class Output
        {
            public bool success;
            public string prefab_path;
            public string instance_path;
            public string message;
        }

        /// <summary>
        /// Apply / Revert 操作を実行する
        /// </summary>
        public static ToolResult Execute(string argsJson, Kind kind)
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

            // 操作を実行
            try
            {
                if (kind == Kind.Apply)
                {
                    PrefabUtility.ApplyPrefabInstance(prefabRoot, InteractionMode.AutomatedAction);
                }
                else
                {
                    PrefabUtility.RevertPrefabInstance(prefabRoot, InteractionMode.AutomatedAction);
                }
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(kind == Kind.Apply
                    ? $"Failed to apply prefab changes: {ex.Message}"
                    : $"Failed to revert prefab instance: {ex.Message}");
            }

            return ToolResult.Ok(new Output
            {
                success = true,
                prefab_path = prefabAssetPath,
                instance_path = instancePath,
                message = kind == Kind.Apply
                    ? $"Applied changes to prefab: {prefabAssetPath}"
                    : $"Reverted instance to prefab: {prefabAssetPath}"
            });
        }
    }
}
