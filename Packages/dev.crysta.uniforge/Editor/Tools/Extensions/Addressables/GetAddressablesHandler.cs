#if ADDRESSABLES_INSTALLED
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UniForge.Tools;

namespace UniForge.Tools.Addressables
{
    /// <summary>
    /// get-addressables ツールの出力
    /// </summary>
    public class GetAddressablesOutput
    {
        public bool installed;
        public List<AddressableGroupInfo> groups;
        public int totalEntries;
    }

    /// <summary>
    /// Addressable グループ情報
    /// </summary>
    public class AddressableGroupInfo
    {
        public string name;
        public List<AddressableEntryInfo> entries;
    }

    /// <summary>
    /// Addressable エントリ情報
    /// </summary>
    public class AddressableEntryInfo
    {
        public string address;
        public string assetPath;
        public string guid;
        public List<string> labels;
    }

    /// <summary>
    /// Addressables 情報取得ツール
    /// </summary>
    [Tool("addressables",
        Description = "Get Addressables configuration including groups, entries, and labels",
        Title = "Get Addressables",
        Category = ToolCategory.Addressables,
        Kind = ToolKind.Query,
        Idempotent = true)]
    [ToolOutput(typeof(GetAddressablesOutput))]
    public class GetAddressablesHandler : QueryHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Filter by group name")]
            public string group;

            [ToolParameter("Filter by label")]
            public string label;

            [ToolParameter("Filter by address pattern (regex)")]
            public string address;

            [ToolParameter("Maximum number of entries to return", Default = 100)]
            public int limit;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);

            var groupFilter = args.GetString("group");
            var labelFilter = args.GetString("label");
            var addressPattern = args.GetString("address");
            var limit = args.GetInt("limit", 100);

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                return ToolResult.Ok(new GetAddressablesOutput
                {
                    installed = true,
                    groups = new List<AddressableGroupInfo>(),
                    totalEntries = 0
                });
            }

            // アドレスパターン正規表現
            Regex addressRegex = null;
            if (!string.IsNullOrEmpty(addressPattern))
            {
                try
                {
                    addressRegex = new Regex(addressPattern, RegexOptions.IgnoreCase);
                }
                catch
                {
                    addressRegex = new Regex(Regex.Escape(addressPattern), RegexOptions.IgnoreCase);
                }
            }

            var groups = new List<AddressableGroupInfo>();
            var totalEntries = 0;
            var entriesCollected = 0;

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                // グループ名フィルタ
                if (!string.IsNullOrEmpty(groupFilter) && group.Name != groupFilter)
                {
                    continue;
                }

                var entries = new List<AddressableEntryInfo>();

                foreach (var entry in group.entries)
                {
                    // ラベルフィルタ
                    if (!string.IsNullOrEmpty(labelFilter))
                    {
                        if (!entry.labels.Contains(labelFilter))
                        {
                            continue;
                        }
                    }

                    // アドレスパターンフィルタ
                    if (addressRegex != null && !addressRegex.IsMatch(entry.address))
                    {
                        continue;
                    }

                    totalEntries++;

                    if (entriesCollected >= limit)
                    {
                        continue; // カウントは続ける
                    }

                    entries.Add(new AddressableEntryInfo
                    {
                        address = entry.address,
                        assetPath = entry.AssetPath,
                        guid = entry.guid,
                        labels = new List<string>(entry.labels)
                    });

                    entriesCollected++;
                }

                if (entries.Count > 0 || string.IsNullOrEmpty(groupFilter))
                {
                    groups.Add(new AddressableGroupInfo
                    {
                        name = group.Name,
                        entries = entries
                    });
                }
            }

            return ToolResult.Ok(new GetAddressablesOutput
            {
                installed = true,
                groups = groups,
                totalEntries = totalEntries
            });
        }
    }
}
#endif
