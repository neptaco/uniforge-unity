using UnityEditor;
using UnityEditor.SceneManagement;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// シーンをロードするツール
    /// </summary>
    [Tool("load-scene",
        Description = "Load a scene in the editor",
        Title = "Load Scene",
        Category = ToolCategory.Scene,
        Kind = ToolKind.Mutation,
        Destructive = true,
        Idempotent = true)]
    public class LoadSceneHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Scene path (e.g., 'Assets/Scenes/MyScene.unity')", Required = true)]
            public string scene_path;

            [ToolParameter("Load mode", Enum = "Single,Additive", Default = "Single")]
            public string mode;

            [ToolParameter("Save current scene before loading", Default = true)]
            public bool save_current;

            [ToolParameter("Explicitly discard unsaved changes (required when save_current=false and scene has changes)", Default = false)]
            public bool discard_unsaved;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string scene_name;
            public string scene_path;
            public string mode;
            public string message;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            // Prefab Stage 中はシーンロードを拒否（ダイアログ回避）
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return ToolResult.Fail("Cannot load scene while Prefab Stage is open. Use prefab-stage close first.");
            }

            var args = new ToolArgsParser(argsJson);
            var scenePath = args.GetString("scene_path");
            var modeStr = args.GetString("mode", "Single");
            var saveCurrent = args.GetBool("save_current", true);
            var discardUnsaved = args.GetBool("discard_unsaved", false);

            if (string.IsNullOrEmpty(scenePath))
            {
                return ToolResult.Fail("Parameter 'scene_path' is required");
            }

            // シーンファイルの存在確認
            if (!System.IO.File.Exists(scenePath))
            {
                return ToolResult.Fail($"Scene file not found: {scenePath}");
            }

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

            // ロードモード
            OpenSceneMode mode;
            switch (modeStr.ToLowerInvariant())
            {
                case "additive":
                    mode = OpenSceneMode.Additive;
                    break;
                case "single":
                default:
                    mode = OpenSceneMode.Single;
                    break;
            }

            // シーンロード
            var scene = EditorSceneManager.OpenScene(scenePath, mode);
            if (!scene.IsValid())
            {
                return ToolResult.Fail($"Failed to load scene: {scenePath}");
            }

            return ToolResult.Ok(new Output
            {
                success = true,
                scene_name = scene.name,
                scene_path = scene.path,
                mode = modeStr,
                message = $"Loaded scene: {scene.name} ({modeStr})"
            });
        }
    }
}
