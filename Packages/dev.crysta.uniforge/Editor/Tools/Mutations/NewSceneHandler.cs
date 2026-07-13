using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// 新しいシーンを作成するツール
    /// </summary>
    [Tool("new-scene",
        Description = "Create a new scene",
        Title = "New Scene",
        Category = ToolCategory.Scene,
        Kind = ToolKind.Mutation,
        Destructive = true,
        Idempotent = false)]
    public class NewSceneHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("New scene setup", Enum = "EmptyScene,DefaultGameObjects", Default = "DefaultGameObjects")]
            public string setup;

            [ToolParameter("Save current scene before creating new", Default = true)]
            public bool save_current;

            [ToolParameter("Explicitly discard unsaved changes (required when save_current=false and scene has changes)", Default = false)]
            public bool discard_unsaved;

            [ToolParameter("New scene mode", Enum = "Single,Additive", Default = "Single")]
            public string mode;

            [ToolParameter("Save path for new scene (e.g., 'Assets/Scenes/NewScene.unity'). If specified, the scene is saved immediately after creation.")]
            public string save_path;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string scene_name;
            public string scene_path;
            public string message;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var setupStr = args.GetString("setup", "DefaultGameObjects");
            var saveCurrent = args.GetBool("save_current", true);
            var discardUnsaved = args.GetBool("discard_unsaved", false);
            var modeStr = args.GetString("mode", "Single");

            // 現在のシーンを保存
            if (saveCurrent)
            {
                // 未保存の変更がある場合、ダイアログを出さずに自動保存する
                if (SceneHelper.HasUnsavedSceneChanges(out _))
                {
                    if (!EditorSceneManager.SaveOpenScenes())
                    {
                        return ToolResult.Fail("Failed to save current scene(s). Check for errors and try again.");
                    }
                }
            }
            else
            {
                // save_current=false の場合、未保存変更があれば明示的な discard_unsaved が必要
                if (SceneHelper.HasUnsavedSceneChanges(out var dirtyScenes) && !discardUnsaved)
                {
                    return ToolResult.Fail($"Scene(s) have unsaved changes: {dirtyScenes}. Set save_current=true to save, or set discard_unsaved=true to explicitly discard changes.");
                }
            }

            // セットアップ
            NewSceneSetup setup;
            switch (setupStr.ToLowerInvariant())
            {
                case "emptyscene":
                case "empty":
                    setup = NewSceneSetup.EmptyScene;
                    break;
                case "defaultgameobjects":
                case "default":
                default:
                    setup = NewSceneSetup.DefaultGameObjects;
                    break;
            }

            // モード
            NewSceneMode mode;
            switch (modeStr.ToLowerInvariant())
            {
                case "additive":
                    mode = NewSceneMode.Additive;
                    break;
                case "single":
                default:
                    mode = NewSceneMode.Single;
                    break;
            }

            // 新規シーン作成
            var scene = EditorSceneManager.NewScene(setup, mode);
            if (!scene.IsValid())
            {
                return ToolResult.Fail("Failed to create new scene");
            }

            // save_path が指定されている場合は即座に保存
            var savePath = args.GetString("save_path");
            var scenePath = scene.path;

            if (!string.IsNullOrEmpty(savePath))
            {
                if (!savePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    savePath += ".unity";
                }

                // 親ディレクトリを確保（CreateDirectory は冪等）
                var dir = System.IO.Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                if (!EditorSceneManager.SaveScene(scene, savePath))
                {
                    return ToolResult.Fail($"Scene created but failed to save at: {savePath}");
                }
                scenePath = savePath;
            }

            return ToolResult.Ok(new Output
            {
                success = true,
                scene_name = scene.name,
                scene_path = scenePath,
                message = $"Created new scene: {scene.name} (setup: {setupStr}, mode: {modeStr})"
            });
        }
    }
}
