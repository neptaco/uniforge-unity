namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// シーンインスタンスをプレハブの状態に戻すツール
    /// </summary>
    [Tool("revert-prefab",
        Description = "Revert a prefab instance in the scene to match the source prefab asset",
        Title = "Revert Prefab",
        Category = ToolCategory.Prefab,
        Kind = ToolKind.Mutation,
        Destructive = true,
        Idempotent = true)]
    public class RevertPrefabHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("GameObject path in hierarchy", Required = false)]
            public string path;

            [ToolParameter("GameObject instance ID", Required = false)]
            public int? instance_id;

            [ToolParameter("Scene name (optional)", Required = false)]
            public string scene;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string prefab_path;
            public string instance_path;
            public string message;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            // 共通処理は PrefabInstanceOperation に集約
            return PrefabInstanceOperation.Execute(argsJson, PrefabInstanceOperation.Kind.Revert);
        }
    }
}
