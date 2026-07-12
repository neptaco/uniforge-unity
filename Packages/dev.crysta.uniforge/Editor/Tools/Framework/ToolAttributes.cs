using System;

namespace UniForge.Tools
{
    /// <summary>
    /// ツールの種類
    /// </summary>
    public enum ToolKind
    {
        Query,
        Mutation
    }

    /// <summary>
    /// ツールのカテゴリ
    /// </summary>
    public static class ToolCategory
    {
        public const string GameObject = "GameObject";
        public const string Prefab = "Prefab";
        public const string Scene = "Scene";
        public const string Material = "Material";
        public const string Asset = "Asset";
        public const string Editor = "Editor";
        public const string Compilation = "Compilation";
        public const string Logs = "Logs";
        public const string Test = "Test";
        public const string Input = "Input";
        public const string Addressables = "Addressables";
        public const string Other = "Other";

        /// <summary>
        /// カテゴリをアルファベット順で比較（Other は末尾）
        /// </summary>
        public static int CompareOrder(string a, string b)
        {
            bool aIsOther = string.IsNullOrEmpty(a) || a == Other;
            bool bIsOther = string.IsNullOrEmpty(b) || b == Other;

            if (aIsOther && bIsOther) return 0;
            if (aIsOther) return 1;
            if (bIsOther) return -1;

            return string.Compare(a, b, System.StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ツール定義用の属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ToolAttribute : Attribute
    {
        /// <summary>ツール名（kebab-case）</summary>
        public string Name { get; }

        /// <summary>説明文</summary>
        public string Description { get; set; }

        /// <summary>表示用タイトル</summary>
        public string Title { get; set; }

        /// <summary>ツールの種類</summary>
        public ToolKind Kind { get; set; } = ToolKind.Query;

        /// <summary>破壊的操作かどうか（Mutationのみ）</summary>
        public bool Destructive { get; set; } = false;

        /// <summary>冪等かどうか</summary>
        public bool Idempotent { get; set; } = false;

        /// <summary>カテゴリ（ToolCategoryの定数を使用）</summary>
        public string Category { get; set; } = ToolCategory.Other;

        /// <summary>自動登録対象かどうか</summary>
        public bool Register { get; set; } = true;

        public ToolAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// ツールパラメータ定義用の属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class ToolParameterAttribute : Attribute
    {
        /// <summary>パラメータ名（省略時はフィールド名をsnake_caseに変換）</summary>
        public string Name { get; set; }

        /// <summary>説明文</summary>
        public string Description { get; set; }

        /// <summary>必須かどうか</summary>
        public bool Required { get; set; } = false;

        /// <summary>デフォルト値（JSON形式）</summary>
        public object Default { get; set; }

        /// <summary>列挙値（カンマ区切り）</summary>
        public string Enum { get; set; }

        public ToolParameterAttribute() { }

        public ToolParameterAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// ツール出力スキーマ定義用の属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class ToolOutputAttribute : Attribute
    {
        /// <summary>出力の型</summary>
        public Type OutputType { get; }

        public ToolOutputAttribute(Type outputType)
        {
            OutputType = outputType;
        }
    }
}
