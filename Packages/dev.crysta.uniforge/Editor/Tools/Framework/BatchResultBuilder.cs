using System.Collections.Generic;

namespace UniForge.Tools
{
    /// <summary>
    /// バッチ操作の結果サマリー
    /// </summary>
    public class BatchSummary
    {
        public int total;
        public int succeeded;
        public int failed;
    }

    /// <summary>
    /// バッチ操作の結果を集約するビルダー
    /// </summary>
    /// <typeparam name="TResult">個別操作の結果型</typeparam>
    public class BatchResultBuilder<TResult>
    {
        private readonly List<TResult> _results = new List<TResult>();
        private int _succeeded;

        /// <summary>
        /// 成功した操作の結果を追加
        /// </summary>
        public void AddSuccess(TResult result)
        {
            _results.Add(result);
            _succeeded++;
        }

        /// <summary>
        /// 失敗した操作の結果を追加
        /// </summary>
        public void AddFailure(TResult result)
        {
            _results.Add(result);
        }

        /// <summary>
        /// 結果を追加（成功/失敗を指定）
        /// </summary>
        public void Add(TResult result, bool success)
        {
            _results.Add(result);
            if (success) _succeeded++;
        }

        /// <summary>
        /// 処理済み件数
        /// </summary>
        public int Count => _results.Count;

        /// <summary>
        /// 成功件数
        /// </summary>
        public int SuccessCount => _succeeded;

        /// <summary>
        /// 失敗件数
        /// </summary>
        public int FailureCount => _results.Count - _succeeded;

        /// <summary>
        /// 全件成功したか
        /// </summary>
        public bool AllSucceeded => _succeeded == _results.Count && _results.Count > 0;

        /// <summary>
        /// 結果配列を取得
        /// </summary>
        public TResult[] GetResults() => _results.ToArray();

        /// <summary>
        /// サマリーを取得
        /// </summary>
        public BatchSummary GetSummary() => new BatchSummary
        {
            total = _results.Count,
            succeeded = _succeeded,
            failed = _results.Count - _succeeded
        };

        /// <summary>
        /// メッセージを生成
        /// </summary>
        public string GetMessage(string operationName = "operation")
        {
            if (AllSucceeded)
            {
                return $"All {_results.Count} {operationName}(s) succeeded";
            }
            return $"{_succeeded}/{_results.Count} {operationName}(s) succeeded";
        }

        /// <summary>
        /// バッチ出力オブジェクトを構築
        /// </summary>
        public BatchOutput<TResult> Build(string operationName = "operation")
        {
            return new BatchOutput<TResult>
            {
                success = AllSucceeded,
                summary = GetSummary(),
                results = GetResults(),
                message = GetMessage(operationName)
            };
        }
    }

    /// <summary>
    /// バッチ操作のサマリー（スキップ含む）
    /// </summary>
    public class BatchSummaryEx : BatchSummary
    {
        public int skipped;
    }

    /// <summary>
    /// バッチ操作の出力
    /// </summary>
    /// <typeparam name="TResult">個別操作の結果型</typeparam>
    public class BatchOutput<TResult>
    {
        /// <summary>全件成功したか</summary>
        public bool success;

        /// <summary>サマリー</summary>
        public BatchSummary summary;

        /// <summary>各操作の結果（入力順序に対応）</summary>
        public TResult[] results;

        /// <summary>メッセージ</summary>
        public string message;

        /// <summary>失敗した操作のリトライ用ペイロード（失敗時のみ）</summary>
        public object retry_payload;
    }
}
