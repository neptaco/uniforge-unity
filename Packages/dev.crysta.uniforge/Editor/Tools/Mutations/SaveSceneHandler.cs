using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#if UNITY_INCLUDE_TESTS
using System;
#endif

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// シーンを保存するツール
    /// </summary>
    [Tool("save-scene",
        Description = "Save the current scene or save as a new scene. Cannot be used during play mode.",
        Title = "Save Scene",
        Category = ToolCategory.Scene,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public class SaveSceneHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Scene name or path to save (defaults to active scene)")]
            public string scene;

            [ToolParameter("Save path for 'Save As' (e.g., 'Assets/Scenes/NewScene.unity')")]
            public string save_as_path;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string scene_name;
            public string scene_path;
            public string message;
            public bool active_scene_is_dirty_after_save;
            public bool[] loaded_scenes_dirty_after_save;
        }

#if UNITY_INCLUDE_TESTS
        internal static Func<bool> PlayModeActiveOverrideForTests;
#endif

        protected internal virtual bool IsPlayModeActive()
#if UNITY_INCLUDE_TESTS
            => PlayModeActiveOverrideForTests?.Invoke() ?? EditorApplication.isPlayingOrWillChangePlaymode;
#else
            => EditorApplication.isPlayingOrWillChangePlaymode;
#endif

        protected internal override ToolResult Execute(string argsJson)
        {
            if (IsPlayModeActive())
            {
                return ToolResult.Fail("Cannot save scene while play mode is active. Stop play mode first.");
            }

            // Prefab Stage 中はシーン保存を拒否
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return ToolResult.Fail("Cannot save scene while Prefab Stage is open. Use prefab-stage save/close instead.");
            }

            var args = new ToolArgsParser(argsJson);
            var sceneName = args.GetString("scene");
            var saveAsPath = args.GetString("save_as_path");

            // シーン取得（Prefab Stage の保存は prefab-stage ツールの責務なので Stage は見ない）
            if (!SceneHelper.TryResolveScene(sceneName, includePrefabStage: false, out var scene, out var sceneError))
            {
                return ToolResult.Fail(sceneError);
            }

            bool saved;
            string finalPath;

            if (!string.IsNullOrEmpty(saveAsPath))
            {
                // Save As
                saveAsPath = saveAsPath.Replace('\\', '/');
                if (!saveAsPath.StartsWith("Assets/"))
                {
                    saveAsPath = "Assets/" + saveAsPath;
                }
                if (!saveAsPath.EndsWith(".unity"))
                {
                    saveAsPath += ".unity";
                }
                var dir = System.IO.Path.GetDirectoryName(saveAsPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                {
                    AssetHelper.CreateFolderRecursive(dir);
                }
                saved = EditorSceneManager.SaveScene(scene, saveAsPath);
                finalPath = saveAsPath;
            }
            else
            {
                // 通常の保存
                if (string.IsNullOrEmpty(scene.path))
                {
                    return ToolResult.Fail("Scene has no path. Use 'save_as_path' to specify a save location.");
                }
                saved = EditorSceneManager.SaveScene(scene);
                finalPath = scene.path;
            }

            if (!saved)
            {
                return ToolResult.Fail($"Failed to save scene: {scene.name}");
            }

            var dirtyStates = new bool[SceneManager.sceneCount];
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                dirtyStates[i] = SceneManager.GetSceneAt(i).isDirty;
            }

            return ToolResult.Ok(new Output
            {
                success = true,
                scene_name = scene.name,
                scene_path = finalPath,
                message = $"Scene saved: {finalPath}",
                active_scene_is_dirty_after_save = SceneManager.GetActiveScene().isDirty,
                loaded_scenes_dirty_after_save = dirtyStates
            });
        }
    }
}
