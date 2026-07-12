using UnityEditor;

namespace UniForge.Tools
{
    /// <summary>
    /// アセット操作のヘルパーユーティリティ
    /// </summary>
    public static class AssetHelper
    {
        /// <summary>
        /// Assets 配下のフォルダを再帰的に作成する。
        /// 既に存在するフォルダはスキップする。
        /// </summary>
        /// <param name="folderPath">Assets/ で始まるフォルダパス</param>
        public static void CreateFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parts = folderPath.Split('/');
            var currentPath = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                var parentPath = currentPath;
                currentPath = currentPath + "/" + parts[i];

                if (!AssetDatabase.IsValidFolder(currentPath))
                {
                    AssetDatabase.CreateFolder(parentPath, parts[i]);
                }
            }
        }
    }
}
