using UnityEditor;
using UnityEngine;

namespace UniForge.Tools
{
    /// <summary>
    /// ツールハンドラーの基底インターフェイス（非同期ベース）
    /// </summary>
    public interface IToolHandler
    {
        /// <summary>ツール定義</summary>
        ToolDefinition Definition { get; }

        /// <summary>ツールを非同期実行（ToolDispatcher のエントリポイント）</summary>
        Awaitable<ToolResult> ExecuteAsync(string argsJson);
    }

    /// <summary>
    /// クエリハンドラー（読み取り専用操作）
    /// </summary>
    public abstract class QueryHandler : IToolHandler
    {
        public abstract ToolDefinition Definition { get; }

        Awaitable<ToolResult> IToolHandler.ExecuteAsync(string argsJson)
            => ExecuteAsync(argsJson);

        /// <summary>非同期実行。デフォルトでは同期 Execute を呼ぶ。非同期ツールはこちらを override。</summary>
#pragma warning disable CS1998
        protected internal virtual async Awaitable<ToolResult> ExecuteAsync(string argsJson)
            => Execute(argsJson);
#pragma warning restore CS1998

        /// <summary>同期ツールはこちらを override する。</summary>
        protected internal virtual ToolResult Execute(string argsJson)
            => ToolResult.Fail($"{Definition.name}: override Execute or ExecuteAsync");
    }

    /// <summary>
    /// ミューテーションハンドラー（変更操作・Undo自動対応）
    /// </summary>
    public abstract class MutationHandler : IToolHandler
    {
        public abstract ToolDefinition Definition { get; }

        /// <summary>IToolHandler エントリポイント — Undo ラップ付きで ExecuteAsync を呼ぶ</summary>
        async Awaitable<ToolResult> IToolHandler.ExecuteAsync(string argsJson)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(Definition.Title);

            try
            {
                return await ExecuteAsync(argsJson);
            }
            finally
            {
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            }
        }

        /// <summary>非同期ツールはこちらを override する。デフォルトでは同期 Execute を呼ぶ。</summary>
#pragma warning disable CS1998
        protected internal virtual async Awaitable<ToolResult> ExecuteAsync(string argsJson)
            => Execute(argsJson);
#pragma warning restore CS1998

        /// <summary>同期ツールはこちらを override する。</summary>
        protected internal virtual ToolResult Execute(string argsJson)
            => ToolResult.Fail($"{Definition.name}: override Execute or ExecuteAsync");
    }
}
