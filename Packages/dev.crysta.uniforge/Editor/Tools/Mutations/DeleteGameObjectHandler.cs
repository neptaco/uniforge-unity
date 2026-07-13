using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// GameObject を削除するツール（バッチ対応）
    /// </summary>
    [Tool("delete-gameobject",
        Description = "Delete a GameObject from the scene",
        Title = "Delete GameObject",
        Category = ToolCategory.GameObject,
        Kind = ToolKind.Mutation,
        Destructive = true,
        Idempotent = true)]
    public class DeleteGameObjectHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Array of targets to delete", Required = true)]
            public Target[] targets;
        }

        /// <summary>個別削除結果</summary>
        public class DeleteResult
        {
            public bool success;
            public string name;
            public int? instance_id;
            public int children_deleted;
            public string error;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var targets = args.GetObjectArray<Target>("targets");

            if (targets == null || targets.Length == 0)
            {
                return ToolResult.Fail("Parameter 'targets' is required and must be a non-empty array");
            }

            var builder = new BatchResultBuilder<DeleteResult>();

            foreach (var target in targets)
            {
                try
                {
                    var result = GameObjectResolver.ResolveFromTarget(target);

                    if (!result.Success)
                    {
                        builder.AddFailure(new DeleteResult
                        {
                            success = false,
                            error = result.Error
                        });
                        continue;
                    }

                    var go = result.GameObject;
                    var goName = go.name;
                    var goInstanceId = go.GetInstanceID();

                    // プレハブインスタンスの子は削除できない（DestroyImmediate が例外を投げるため事前チェック）
                    if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                    {
                        builder.AddFailure(new DeleteResult
                        {
                            success = false,
                            name = goName,
                            instance_id = goInstanceId,
                            error = $"Cannot delete '{goName}': it is a child of a prefab instance. " +
                                "Delete the outermost prefab instance root, or open the prefab in Prefab Stage to edit its contents."
                        });
                        continue;
                    }

                    var childCount = CountAllChildren(go.transform);

                    // Undo 対応で削除
                    Undo.DestroyObjectImmediate(go);

                    builder.AddSuccess(new DeleteResult
                    {
                        success = true,
                        name = goName,
                        instance_id = goInstanceId,
                        children_deleted = childCount
                    });
                }
                catch (System.Exception ex)
                {
                    // 個別のエラーでバッチ全体を中断しない
                    builder.AddFailure(new DeleteResult
                    {
                        success = false,
                        error = $"Unexpected error: {ex.Message}"
                    });
                }
            }

            return ToolResult.Ok(builder.Build("deletion"));
        }

        private int CountAllChildren(Transform parent)
        {
            int count = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                count++; // 直接の子
                count += CountAllChildren(parent.GetChild(i)); // 孫以下
            }
            return count;
        }
    }
}
