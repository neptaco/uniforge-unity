using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Prefab Stage のライフサイクル管理ツール（open / save / close）
    /// </summary>
    [Tool("prefab-stage",
        Description = "Manage Prefab Stage: open a prefab for editing, save changes, or close and return to the scene.",
        Title = "Prefab Stage",
        Category = ToolCategory.Prefab,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = false)]
    public class PrefabStageHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Action: 'open', 'save', or 'close'", Required = true, Enum = "open,save,close")]
            public string action;

            [ToolParameter("Prefab asset path (for 'open' action)")]
            public string prefab_path;

            [ToolParameter("GameObject path in hierarchy to open its source prefab (for 'open' action)")]
            public string instance_path;

            [ToolParameter("GameObject instance ID to open its source prefab (for 'open' action)")]
            public int? instance_id;

            [ToolParameter("Save before closing (for 'close' action)", Default = false)]
            public bool save;

            [ToolParameter("Discard unsaved changes when closing (for 'close' action)", Default = false)]
            public bool discard;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string prefab_path;
            public int? root_instance_id;
            public string message;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var action = args.GetString("action");

            if (string.IsNullOrEmpty(action))
            {
                return ToolResult.Fail("Parameter 'action' is required (open, save, close)");
            }

            switch (action.ToLowerInvariant())
            {
                case "open":
                    return ExecuteOpen(args);
                case "save":
                    return ExecuteSave();
                case "close":
                    return ExecuteClose(args);
                default:
                    return ToolResult.Fail($"Unknown action: {action}. Use 'open', 'save', or 'close'.");
            }
        }

        private static ToolResult ExecuteOpen(ToolArgsParser args)
        {
            var prefabPath = args.GetString("prefab_path");
            var instancePath = args.GetString("instance_path");
            var instanceId = args.GetNullableInt("instance_id");

            string assetPath;

            if (!string.IsNullOrEmpty(prefabPath))
            {
                assetPath = prefabPath.Replace('\\', '/');
                if (!assetPath.StartsWith("Assets/"))
                {
                    assetPath = "Assets/" + assetPath;
                }
                if (!assetPath.EndsWith(".prefab"))
                {
                    assetPath += ".prefab";
                }

                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefabAsset == null)
                {
                    return ToolResult.Fail($"Prefab not found at path: {assetPath}");
                }
            }
            else if (!string.IsNullOrEmpty(instancePath) || instanceId.HasValue)
            {
                var resolveResult = GameObjectResolver.Resolve(instancePath, instanceId, null);
                if (!resolveResult.Success)
                {
                    return ToolResult.Fail(resolveResult.Error);
                }

                var go = resolveResult.GameObject;
                if (!PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    return ToolResult.Fail($"GameObject '{go.name}' is not a prefab instance");
                }

                assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (string.IsNullOrEmpty(assetPath))
                {
                    return ToolResult.Fail($"Could not find prefab asset path for '{go.name}'");
                }
            }
            else
            {
                return ToolResult.Fail("'prefab_path' or 'instance_path'/'instance_id' is required for 'open' action");
            }

            try
            {
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                AssetDatabase.OpenAsset(prefabAsset);

                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null)
                {
                    // OpenAsset が成功しても Stage が開かないケースがあるため必ず検証する
                    return ToolResult.Fail($"Prefab Stage did not open for: {assetPath}");
                }

                int? rootId = null;
                if (stage.prefabContentsRoot != null)
                {
                    rootId = stage.prefabContentsRoot.GetInstanceID();
                }

                return ToolResult.Ok(new Output
                {
                    success = true,
                    prefab_path = assetPath,
                    root_instance_id = rootId,
                    message = $"Opened prefab: {assetPath}"
                });
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to open prefab: {ex.Message}");
            }
        }

        private static ToolResult ExecuteSave()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return ToolResult.Fail("No Prefab Stage is currently open");
            }

            var root = stage.prefabContentsRoot;
            if (root == null)
            {
                return ToolResult.Fail("Prefab Stage has no root object");
            }

            var assetPath = stage.assetPath;
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);

            return ToolResult.Ok(new Output
            {
                success = true,
                prefab_path = assetPath,
                message = $"Prefab saved: {assetPath}"
            });
        }

        private static ToolResult ExecuteClose(ToolArgsParser args)
        {
            var save = args.GetBool("save", false);
            var discard = args.GetBool("discard", false);

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return ToolResult.Fail("No Prefab Stage is currently open");
            }

            var assetPath = stage.assetPath;

            if (stage.scene.isDirty)
            {
                if (save)
                {
                    var root = stage.prefabContentsRoot;
                    if (root != null)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                    }
                }
                else if (!discard)
                {
                    return ToolResult.Fail(
                        $"Prefab '{assetPath}' has unsaved changes. Use save: true to save and close, or discard: true to discard changes.");
                }
            }

            StageUtility.GoToMainStage();

            return ToolResult.Ok(new Output
            {
                success = true,
                prefab_path = assetPath,
                message = save ? $"Saved and closed: {assetPath}" : $"Closed: {assetPath}"
            });
        }
    }
}
