using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace UniForge.Tools
{
    /// <summary>
    /// シーン操作のヘルパーユーティリティ
    /// </summary>
    public static class SceneHelper
    {
        /// <summary>
        /// シーン名またはパスからシーンを解決する。
        /// </summary>
        /// <param name="sceneName">シーン名またはシーンパス（空の場合はアクティブシーン）</param>
        /// <param name="includePrefabStage">
        /// sceneName が空で Prefab Stage が開いている場合に Stage のシーンを返すかどうか。
        /// GameObject 解決やヒエラルキー取得では true（Prefab 編集中の操作対象を Stage に向ける）、
        /// シーン保存では false（Prefab Stage の保存は prefab-stage ツールの責務）を指定する。
        /// </param>
        /// <param name="scene">解決されたシーン</param>
        /// <param name="error">失敗時のエラーメッセージ</param>
        /// <returns>解決に成功した場合 true</returns>
        public static bool TryResolveScene(string sceneName, bool includePrefabStage, out Scene scene, out string error)
        {
            error = null;

            if (!string.IsNullOrEmpty(sceneName))
            {
                scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid())
                {
                    scene = SceneManager.GetSceneByPath(sceneName);
                }
                if (!scene.IsValid())
                {
                    error = $"Scene not found: {sceneName}";
                    return false;
                }
            }
            else
            {
                var prefabStage = includePrefabStage ? PrefabStageUtility.GetCurrentPrefabStage() : null;
                scene = prefabStage != null ? prefabStage.scene : SceneManager.GetActiveScene();
            }

            if (!scene.isLoaded)
            {
                error = $"Scene is not loaded: {scene.name}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 未保存のシーン変更があるかチェック
        /// </summary>
        /// <param name="dirtySceneNames">未保存シーン名のカンマ区切りリスト</param>
        /// <returns>未保存変更があれば true</returns>
        public static bool HasUnsavedSceneChanges(out string dirtySceneNames)
        {
            var dirtyScenes = new List<string>();
            var sceneCount = EditorSceneManager.sceneCount;

            for (int i = 0; i < sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    dirtyScenes.Add(scene.name);
                }
            }

            dirtySceneNames = string.Join(", ", dirtyScenes);
            return dirtyScenes.Count > 0;
        }
    }
}
