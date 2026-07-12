using System;

namespace UniForge.Tools
{
    /// <summary>
    /// MCP Tool Annotations (2025-06-18 spec)
    /// NOTE: すべてのプロパティは「ヒント」であり、実際の動作を保証しない
    /// </summary>
    [Serializable]
    public class ToolAnnotations
    {
        /// <summary>表示用タイトル</summary>
        public string title;

        /// <summary>読み取り専用（環境を変更しない）- default: false</summary>
        public bool readOnlyHint;

        /// <summary>破壊的更新を行う可能性がある - default: true</summary>
        public bool destructiveHint = true;

        /// <summary>冪等（同じ引数で繰り返し呼んでも追加効果なし）- default: false</summary>
        public bool idempotentHint;

        /// <summary>外部エンティティとやり取りする - default: true</summary>
        public bool openWorldHint = true;

        /// <summary>クエリツール用のアノテーションを作成</summary>
        public static ToolAnnotations ForQuery(string title = null) => new()
        {
            title = title,
            readOnlyHint = true,
            destructiveHint = false,
            idempotentHint = true,
            openWorldHint = false
        };

        /// <summary>ミューテーションツール用のアノテーションを作成</summary>
        public static ToolAnnotations ForMutation(
            string title = null,
            bool destructive = false,
            bool idempotent = false) => new()
        {
            title = title,
            readOnlyHint = false,
            destructiveHint = destructive,
            idempotentHint = idempotent,
            openWorldHint = false
        };

        /// <summary>シリアライズ用のDictionaryに変換</summary>
        public object ToDictionary()
        {
            return new System.Collections.Generic.Dictionary<string, object>
            {
                { "title", title },
                { "readOnlyHint", readOnlyHint },
                { "destructiveHint", destructiveHint },
                { "idempotentHint", idempotentHint },
                { "openWorldHint", openWorldHint }
            };
        }
    }
}
