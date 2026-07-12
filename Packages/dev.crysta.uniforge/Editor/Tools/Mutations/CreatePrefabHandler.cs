using System;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// GameObjectからプレハブを作成するツール
    /// </summary>
    [Tool("create-prefab",
        Description = "Create a prefab from a GameObject in the scene",
        Title = "Create Prefab",
        Category = ToolCategory.Prefab,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public partial class CreatePrefabHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("GameObject path in hierarchy", Required = false)]
            public string source_path;

            [ToolParameter("GameObject instance ID", Required = false)]
            public int? source_instance_id;

            [ToolParameter("Prefab save path (e.g., 'Assets/Prefabs/Player.prefab')", Required = true)]
            public string prefab_path;

            [ToolParameter("Overwrite existing prefab if it exists", Required = false, Default = false)]
            public bool overwrite;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string prefab_path;
            public int? prefab_instance_id;
            public bool overwritten;
            public string message;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<CreatePrefabHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var sourcePath = args.GetString("source_path");
            var sourceInstanceId = args.GetNullableInt("source_instance_id");
            var prefabPath = args.GetString("prefab_path");
            var overwrite = args.GetBool("overwrite", false);

            if (string.IsNullOrEmpty(prefabPath))
            {
                return ToolResult.Fail("Parameter 'prefab_path' is required");
            }

            // パスの正規化
            prefabPath = prefabPath.Replace('\\', '/');
            if (!prefabPath.EndsWith(".prefab"))
            {
                prefabPath += ".prefab";
            }
            if (!prefabPath.StartsWith("Assets/"))
            {
                prefabPath = "Assets/" + prefabPath;
            }

            // GameObject を解決
            var resolveResult = GameObjectResolver.Resolve(sourcePath, sourceInstanceId, null);
            if (!resolveResult.Success)
            {
                return ToolResult.Fail(resolveResult.Error);
            }

            var sourceGo = resolveResult.GameObject;

            // 既存のプレハブを確認
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            if (exists && !overwrite)
            {
                return ToolResult.Fail($"Prefab already exists at {prefabPath}. Set overwrite=true to replace.");
            }

            // 親フォルダを作成
            var folderPath = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            AssetHelper.CreateFolderRecursive(folderPath);

            // プレハブを作成
            GameObject prefab;
            try
            {
                prefab = PrefabUtility.SaveAsPrefabAsset(sourceGo, prefabPath);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to create prefab: {ex.Message}");
            }

            if (prefab == null)
            {
                return ToolResult.Fail("Failed to create prefab (unknown error)");
            }

            return ToolResult.Ok(new Output
            {
                success = true,
                prefab_path = prefabPath,
                prefab_instance_id = prefab.GetInstanceID(),
                overwritten = exists,
                message = exists ? $"Prefab overwritten: {prefabPath}" : $"Prefab created: {prefabPath}"
            });
        }

    }
}
