using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Scene View のカメラ位置・回転・ズームを設定するツール
    /// </summary>
    [Tool("set-scene-view",
        Description = "Set Scene View camera position, rotation, zoom, focus target, and display options (2D mode, gizmos, grid, lighting).",
        Title = "Set Scene View",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public class SetSceneViewHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Camera pivot point (look-at target) as [x, y, z]")]
            public float[] pivot;

            [ToolParameter("Camera rotation as euler angles [x, y, z] or quaternion [x, y, z, w]")]
            public float[] rotation;

            [ToolParameter("Camera zoom distance from pivot (smaller = closer)")]
            public float? size;

            [ToolParameter("Enable orthographic view")]
            public bool? orthographic;

            [ToolParameter("Focus on GameObject by path (uses Frame selection)")]
            public string focus_path;

            [ToolParameter("Focus on GameObject by instance ID")]
            public int? focus_id;

            [ToolParameter("Padding multiplier when framing focus target (default: 1.0, larger = more zoom out)")]
            public float? focus_padding;

            [ToolParameter("Enable 2D mode")]
            public bool? in_2d_mode;

            [ToolParameter("Show/hide gizmos")]
            public bool? draw_gizmos;

            [ToolParameter("Enable scene lighting (false = unlit)")]
            public bool? scene_lighting;

            [ToolParameter("Show/hide grid")]
            public bool? show_grid;

            [ToolParameter("Camera mode: Textured, Wireframe, TexturedWire, ShadedWireframe, etc.")]
            public string camera_mode;

            [ToolParameter("Show/hide skybox")]
            public bool? show_skybox;

            [ToolParameter("Show/hide fog")]
            public bool? show_fog;

            [ToolParameter("Show/hide flares")]
            public bool? show_flares;

            [ToolParameter("Show/hide particle systems")]
            public bool? show_particle_systems;
        }

        /// <summary>結果</summary>
        public class SetSceneViewResult
        {
            public float[] pivot;
            public float[] rotation;
            public float size;
            public bool orthographic;
            public bool in_2d_mode;
            public bool draw_gizmos;
            public bool scene_lighting;
            public bool show_grid;
            public string camera_mode;
            public bool show_skybox;
            public bool show_fog;
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);

            // SceneView を取得
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return ToolResult.Fail("No active SceneView found");
            }

            // フォーカス処理 (他の設定より先に実行)
            var focusPath = args.GetString("focus_path");
            var focusId = args.GetInt("focus_id", 0);
            var focusPadding = args.GetFloat("focus_padding", 1.0f);

            if (!string.IsNullOrEmpty(focusPath) || focusId != 0)
            {
                var resolveResult = GameObjectResolver.Resolve(
                    focusPath,
                    focusId != 0 ? focusId : null,
                    null);
                if (!resolveResult.Success)
                {
                    return ToolResult.Fail(resolveResult.Error);
                }

                var target = resolveResult.GameObject;

                // 選択してフレーム
                Selection.activeGameObject = target;
                sceneView.FrameSelected();

                if (focusPadding > 0f && Mathf.Abs(focusPadding - 1.0f) > 0.001f)
                {
                    sceneView.size *= focusPadding;
                }
            }

            // Pivot (注視点) の設定
            var pivotArray = args.GetFloatArray("pivot");
            if (pivotArray != null && pivotArray.Length >= 3)
            {
                sceneView.pivot = new Vector3(pivotArray[0], pivotArray[1], pivotArray[2]);
            }

            // Rotation の設定
            var rotationArray = args.GetFloatArray("rotation");
            if (rotationArray != null)
            {
                if (rotationArray.Length >= 4)
                {
                    // Quaternion [x, y, z, w]
                    sceneView.rotation = new Quaternion(
                        rotationArray[0], rotationArray[1], rotationArray[2], rotationArray[3]);
                }
                else if (rotationArray.Length >= 3)
                {
                    // Euler angles [x, y, z]
                    sceneView.rotation = Quaternion.Euler(
                        rotationArray[0], rotationArray[1], rotationArray[2]);
                }
            }

            // Size (ズーム距離) の設定
            var size = args.GetFloat("size", 0);
            if (size > 0)
            {
                // 異常に大きい値を制限（パフォーマンス保護）
                const float maxSize = 10000f;
                sceneView.size = Mathf.Min(size, maxSize);
            }

            // Orthographic の設定
            if (args.HasKey("orthographic"))
            {
                sceneView.orthographic = args.GetBool("orthographic", false);
            }

            // 2D Mode の設定
            if (args.HasKey("in_2d_mode"))
            {
                bool enable2D = args.GetBool("in_2d_mode", false);
                sceneView.in2DMode = enable2D;

                if (enable2D)
                {
                    // 2D モードでは正面向き回転に自動設定（rotation 未指定時）
                    if (!args.HasKey("rotation"))
                    {
                        sceneView.rotation = Quaternion.identity;
                    }
                    // 2D モードでは常に orthographic
                    sceneView.orthographic = true;
                }
            }

            // Gizmos の設定
            if (args.HasKey("draw_gizmos"))
            {
                sceneView.drawGizmos = args.GetBool("draw_gizmos", true);
            }

            // Scene Lighting の設定
            if (args.HasKey("scene_lighting"))
            {
                sceneView.sceneLighting = args.GetBool("scene_lighting", true);
            }

            // Grid の設定
            if (args.HasKey("show_grid"))
            {
                sceneView.showGrid = args.GetBool("show_grid", true);
            }

            // Camera Mode の設定
            var cameraModeStr = args.GetString("camera_mode");
            if (!string.IsNullOrEmpty(cameraModeStr))
            {
                if (System.Enum.TryParse<DrawCameraMode>(cameraModeStr, true, out var mode))
                {
                    sceneView.cameraMode = SceneView.GetBuiltinCameraMode(mode);
                }
            }

            // SceneViewState の設定
            var state = sceneView.sceneViewState;
            if (args.HasKey("show_skybox"))
            {
                state.showSkybox = args.GetBool("show_skybox", true);
            }
            if (args.HasKey("show_fog"))
            {
                state.showFog = args.GetBool("show_fog", true);
            }
            if (args.HasKey("show_flares"))
            {
                state.showFlares = args.GetBool("show_flares", true);
            }
            if (args.HasKey("show_particle_systems"))
            {
                state.showParticleSystems = args.GetBool("show_particle_systems", true);
            }

            // 変更を反映
            sceneView.Repaint();

            // 現在の状態を返す
            var euler = sceneView.rotation.eulerAngles;
            return ToolResult.Ok(new SetSceneViewResult
            {
                pivot = new[] { sceneView.pivot.x, sceneView.pivot.y, sceneView.pivot.z },
                rotation = new[] { euler.x, euler.y, euler.z },
                size = sceneView.size,
                orthographic = sceneView.orthographic,
                in_2d_mode = sceneView.in2DMode,
                draw_gizmos = sceneView.drawGizmos,
                scene_lighting = sceneView.sceneLighting,
                show_grid = sceneView.showGrid,
                camera_mode = sceneView.cameraMode.drawMode.ToString(),
                show_skybox = state.showSkybox,
                show_fog = state.showFog
            });
        }
    }
}
