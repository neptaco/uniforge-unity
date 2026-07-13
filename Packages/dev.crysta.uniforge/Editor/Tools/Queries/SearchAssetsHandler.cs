using System;
using System.Collections.Generic;
using UnityEditor;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// search-assets ツールの出力
    /// </summary>
    public class SearchAssetsOutput
    {
        public List<AssetInfo> assets;
        public int count;
        public bool hasMore;
    }

    /// <summary>
    /// アセット情報
    /// </summary>
    public class AssetInfo
    {
        public string path;
        public string name;
        public string type;
        public string guid;
        public string[] labels;
    }

    /// <summary>
    /// AssetDatabase 検索ツール
    /// </summary>
    [Tool("search-assets",
        Description = "Search Unity AssetDatabase with filters for type, path, and labels",
        Title = "Search Assets",
        Category = ToolCategory.Asset,
        Kind = ToolKind.Query,
        Idempotent = true)]
    [ToolOutput(typeof(SearchAssetsOutput))]
    public class SearchAssetsHandler : QueryHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Unity search syntax (t:Prefab, l:Label, etc.)", Default = "")]
            public string query;

            [ToolParameter("Search path", Default = "Assets")]
            public string path;

            [ToolParameter("Type filter (Prefab, Script, Material, etc.)")]
            public string type;

            [ToolParameter("Extension filter (.cs, .prefab, etc.)")]
            public string extension;

            [ToolParameter("Maximum number of results", Default = 100)]
            public int limit;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);

            var query = args.GetString("query", "");
            var searchPath = args.GetString("path", "Assets");
            var typeFilter = args.GetString("type");
            var extensionFilter = args.GetString("extension");
            var limit = args.GetInt("limit", 100);

            // クエリ構築
            var filter = query;
            if (!string.IsNullOrEmpty(typeFilter))
            {
                filter = $"t:{typeFilter} {filter}".Trim();
            }

            // 検索実行
            var searchFolders = new[] { searchPath };
            var guids = AssetDatabase.FindAssets(filter, searchFolders);

            var assets = new List<AssetInfo>();
            var totalCount = 0;

            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);

                // 拡張子フィルタ
                if (!string.IsNullOrEmpty(extensionFilter))
                {
                    var ext = System.IO.Path.GetExtension(assetPath);
                    if (!ext.Equals(extensionFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                totalCount++;

                if (assets.Count >= limit)
                {
                    continue; // カウントは続ける
                }

                var assetName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                var assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                var typeName = assetType != null ? assetType.Name : "Unknown";

                // ラベル取得
                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                var labels = asset != null ? AssetDatabase.GetLabels(asset) : Array.Empty<string>();

                assets.Add(new AssetInfo
                {
                    path = assetPath,
                    name = assetName,
                    type = typeName,
                    guid = guid,
                    labels = labels
                });
            }

            return ToolResult.Ok(new SearchAssetsOutput
            {
                assets = assets,
                count = assets.Count,
                hasMore = totalCount > assets.Count
            });
        }
    }
}
