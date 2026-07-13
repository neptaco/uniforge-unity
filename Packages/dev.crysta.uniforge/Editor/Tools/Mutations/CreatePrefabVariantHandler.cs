using System;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// 既存プレハブからプレハブバリアントを作成するツール
    /// </summary>
    [Tool("create-prefab-variant",
        Description = "Create a prefab variant from an existing prefab asset",
        Title = "Create Prefab Variant",
        Category = ToolCategory.Prefab,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public class CreatePrefabVariantHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Base prefab asset path (e.g., 'Assets/Prefabs/Player.prefab')", Required = true)]
            public string base_prefab_path;

            [ToolParameter("Variant save path (e.g., 'Assets/Prefabs/PlayerVariant.prefab')", Required = true)]
            public string variant_path;

            [ToolParameter("Overwrite existing prefab if it exists", Required = false, Default = false)]
            public bool overwrite;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string variant_path;
            public string base_prefab_path;
            public int? variant_instance_id;
            public bool overwritten;
            public string message;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var basePrefabPath = args.GetString("base_prefab_path");
            var variantPath = args.GetString("variant_path");
            var overwrite = args.GetBool("overwrite", false);

            if (string.IsNullOrEmpty(basePrefabPath))
            {
                return ToolResult.Fail("Parameter 'base_prefab_path' is required");
            }

            if (string.IsNullOrEmpty(variantPath))
            {
                return ToolResult.Fail("Parameter 'variant_path' is required");
            }

            // パスの正規化（ベースパスはスラッシュ統一のみ、バリアントパスは拡張子・プレフィックス補完も行う）
            basePrefabPath = basePrefabPath.Replace('\\', '/');
            variantPath = variantPath.Replace('\\', '/');
            if (!variantPath.EndsWith(".prefab"))
            {
                variantPath += ".prefab";
            }
            if (!variantPath.StartsWith("Assets/"))
            {
                variantPath = "Assets/" + variantPath;
            }
            if (!basePrefabPath.EndsWith(".prefab"))
            {
                return ToolResult.Fail($"Base prefab path must end with '.prefab': '{basePrefabPath}'");
            }

            // ベースプレハブをロード
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePrefabPath);
            if (basePrefab == null)
            {
                return ToolResult.Fail($"Base prefab not found at '{basePrefabPath}'");
            }

            // 既存のプレハブを確認
            bool exists = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(variantPath, AssetPathToGUIDOptions.OnlyExistingAssets));
            if (exists && !overwrite)
            {
                return ToolResult.Fail($"Prefab already exists at {variantPath}. Set overwrite=true to replace.");
            }

            // 親フォルダを作成
            var folderPath = System.IO.Path.GetDirectoryName(variantPath).Replace('\\', '/');
            AssetHelper.CreateFolderRecursive(folderPath);

            // 一時インスタンスを作成してバリアントとして保存
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            GameObject variant;
            try
            {
                variant = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    instance,
                    variantPath,
                    InteractionMode.AutomatedAction);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to create prefab variant: {ex.Message}");
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }

            if (variant == null)
            {
                return ToolResult.Fail("Failed to create prefab variant (unknown error)");
            }

            return ToolResult.Ok(new Output
            {
                success = true,
                variant_path = variantPath,
                base_prefab_path = basePrefabPath,
                variant_instance_id = variant.GetInstanceID(),
                overwritten = exists,
                message = exists
                    ? $"Prefab variant overwritten: {variantPath}"
                    : $"Prefab variant created: {variantPath} (base: {basePrefabPath})"
            });
        }
    }
}
