using System;
using System.Collections.Generic;

namespace UniForge.Tools
{
    /// <summary>
    /// MCP Tool Definition (2025-06-18 spec)
    /// </summary>
    [Serializable]
    public class ToolDefinition
    {
        /// <summary>ツール識別子（kebab-case推奨）</summary>
        public string name;

        /// <summary>説明文（LLMへのヒント）</summary>
        public string description;

        /// <summary>入力パラメータのJSON Schema</summary>
        public Dictionary<string, object> inputSchema;

        /// <summary>出力のJSON Schema（structuredContent用）</summary>
        public Dictionary<string, object> outputSchema;

        /// <summary>ツールの振る舞いに関するヒント</summary>
        public ToolAnnotations annotations;

        /// <summary>任意のメタデータ</summary>
        public Dictionary<string, object> meta;

        /// <summary>カテゴリ（UI表示用、MCPには送信されない）</summary>
        public string category = ToolCategory.Other;

        // =============================================
        // ヘルパープロパティ
        // =============================================

        /// <summary>クエリツールかどうか</summary>
        public bool IsQuery => annotations?.readOnlyHint ?? false;

        /// <summary>ミューテーションツールかどうか</summary>
        public bool IsMutation => !IsQuery;

        /// <summary>破壊的ツールかどうか</summary>
        public bool IsDestructive => !IsQuery && (annotations?.destructiveHint ?? true);

        /// <summary>冪等ツールかどうか</summary>
        public bool IsIdempotent => annotations?.idempotentHint ?? false;

        /// <summary>表示用タイトル（annotations.title ?? name）</summary>
        public string Title => annotations?.title ?? name;

        // =============================================
        // シリアライズ
        // =============================================

        /// <summary>デーモン登録用のオブジェクトに変換</summary>
        public Dictionary<string, object> ToRegistrationObject()
        {
            var result = new Dictionary<string, object>
            {
                { "name", name },
                { "description", description },
                { "inputSchema", inputSchema }
            };

            if (outputSchema != null)
            {
                result["outputSchema"] = outputSchema;
            }

            if (annotations != null)
            {
                result["annotations"] = annotations.ToDictionary();
            }

            return result;
        }
    }
}
