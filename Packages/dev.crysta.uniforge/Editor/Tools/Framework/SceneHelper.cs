using System.Collections.Generic;
using UnityEditor.SceneManagement;

namespace UniForge.Tools
{
    /// <summary>
    /// シーン操作のヘルパーユーティリティ
    /// </summary>
    public static class SceneHelper
    {
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
