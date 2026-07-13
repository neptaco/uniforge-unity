namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// シーンインスタンスの変更をプレハブに適用するツール
    /// </summary>
    [Tool("apply-prefab",
        Description = "Apply changes from a prefab instance in the scene to the source prefab asset",
        Title = "Apply Prefab",
        Category = ToolCategory.Prefab,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public class ApplyPrefabHandler : MutationHandler
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
            return PrefabInstanceOperation.Execute(argsJson, PrefabInstanceOperation.Kind.Apply);
        }
    }
}
