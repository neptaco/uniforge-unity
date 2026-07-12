using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Scene View をオブジェクトにフォーカスするツール（F キー相当）
    /// </summary>
    [Tool("frame-object",
        Description = "Frame a GameObject in the Scene View (equivalent to pressing F key). Centers the camera on the target object.",
        Title = "Frame Object",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public partial class FrameObjectHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Target GameObject path in hierarchy", Required = false)]
            public string path;

            [ToolParameter("Target GameObject instance ID", Required = false)]
            public int? instance_id;

            [ToolParameter("Scene name for disambiguation", Required = false)]
            public string scene;

            [ToolParameter("Padding multiplier for the framing (default: 1.0, larger = more zoom out)", Required = false)]
            public float? padding;
        }

        /// <summary>出力定義</summary>
        public class Output
        {
            public bool success;
            public string target;
            public int? instance_id;
            public string message;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
            => _definition ??= ToolDefinitionBuilder.FromHandler<FrameObjectHandler>();

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var path = args.GetString("path");
            var instanceId = args.GetNullableInt("instance_id");
            var scene = args.GetString("scene");
            var padding = args.HasKey("padding") ? args.GetFloat("padding") : 1.0f;

            // SceneView を取得
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return ToolResult.Fail("No active SceneView found");
            }

            // GameObject 解決
            var resolveResult = GameObjectResolver.Resolve(path, instanceId, scene);
            if (!resolveResult.Success)
            {
                return ToolResult.Fail(resolveResult.Error);
            }

            var go = resolveResult.GameObject;

            // 選択してフレーム
            Selection.activeGameObject = go;
            sceneView.FrameSelected();

            // パディング適用（1.0 以外の場合、size を調整）
            if (padding > 0f && Mathf.Abs(padding - 1.0f) > 0.001f)
            {
                sceneView.size *= padding;
            }

            sceneView.Repaint();

            return ToolResult.Ok(new Output
            {
                success = true,
                target = GameObjectResolver.GetHierarchyPath(go),
                instance_id = go.GetInstanceID(),
                message = $"Scene View framed on '{go.name}'"
            });
        }
    }
}
