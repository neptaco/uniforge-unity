using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// 任意のエディタウィンドウのスクリーンショットを撮影するツール
    /// </summary>
    [Tool("capture-window",
        Description = "Capture a screenshot of an Editor window (Scene, Game, Inspector, Console, Hierarchy, Project, or custom) without focusing or activating it",
        Title = "Capture Window",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Mutation,
        Destructive = false,
        Idempotent = true)]
    public class CaptureWindowHandler : MutationHandler
    {
        /// <summary>引数定義</summary>
        public class Args
        {
            [ToolParameter("Window type: Scene, Game, Inspector, Console, Hierarchy, Project, or full type name")]
            public string window;

            [ToolParameter("Output file path (default: Temp/Screenshots/window_TIMESTAMP.png)")]
            public string path;

            [ToolParameter("For Game window: capture only the 3D render (excludes Canvas UI). Requires play mode. Default: false")]
            public bool? game_only;

            [ToolParameter("If true, include the captured PNG as base64 in the response. Default: false")]
            public bool? return_image;

        }

        /// <summary>結果</summary>
        public class CaptureResult
        {
            public bool success;
            public string path;
            public string window_type;
            public int width;
            public int height;
            public string capture_method;
            public string image_base64;
            public string image_mime_type;
            public string warning;
            public string error;
        }

        // よく使うウィンドウタイプのマッピング
        private static readonly Dictionary<string, string> WindowTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Scene", "UnityEditor.SceneView" },
            { "SceneView", "UnityEditor.SceneView" },
            { "Game", "UnityEditor.GameView" },
            { "GameView", "UnityEditor.GameView" },
            { "Inspector", "UnityEditor.InspectorWindow" },
            { "InspectorWindow", "UnityEditor.InspectorWindow" },
            { "Console", "UnityEditor.ConsoleWindow" },
            { "ConsoleWindow", "UnityEditor.ConsoleWindow" },
            { "Hierarchy", "UnityEditor.SceneHierarchyWindow" },
            { "HierarchyWindow", "UnityEditor.SceneHierarchyWindow" },
            { "Project", "UnityEditor.ProjectBrowser" },
            { "ProjectBrowser", "UnityEditor.ProjectBrowser" },
            { "Animation", "UnityEditor.AnimationWindow" },
            { "Animator", "UnityEditor.Graphs.AnimatorControllerTool" },
            { "Profiler", "UnityEditor.ProfilerWindow" },
        };

        private const string GameViewTypeName = "UnityEditor.GameView";

        private static string ResolveTypeName(string windowName)
            => WindowTypeMap.TryGetValue(windowName, out var mapped) ? mapped : windowName;

        protected internal override async Awaitable<ToolResult> ExecuteAsync(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var windowName = args.GetString("window");
            var gameOnly = args.GetBool("game_only", false);

            // Play 中の Game ビューは、撮る前にフレームを進めて絵を新しくする。
            // Editor が背面だと Play のフレームが止まり、その後に生成した Overlay の Canvas などが写らないため。
            string warning = null;
            if (!string.IsNullOrEmpty(windowName) && ResolveTypeName(windowName) == GameViewTypeName && !gameOnly)
            {
                var status = await PlayModeFrameSync.WaitForRenderedFramesAsync();
                warning = PlayModeFrameSync.ToCaptureWarning(status);
            }

            return Capture(argsJson, warning);
        }

        private ToolResult Capture(string argsJson, string warning)
        {
            var args = new ToolArgsParser(argsJson);
            var windowName = args.GetString("window");
            var outputPath = args.GetString("path");
            var gameOnly = args.GetBool("game_only", false);
            var returnImage = args.GetBool("return_image", false);

            if (string.IsNullOrEmpty(windowName))
            {
                return ToolResult.Fail("Window type is required. Use: Scene, Game, Inspector, Console, Hierarchy, Project, or full type name");
            }

            // ウィンドウタイプを解決
            var typeName = ResolveTypeName(windowName);

            // 型を検索
            Type windowType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                windowType = assembly.GetType(typeName);
                if (windowType != null) break;
            }

            if (windowType == null)
            {
                var availableTypes = string.Join(", ", WindowTypeMap.Keys.Take(6));
                return ToolResult.Fail($"Window type not found: {windowName}. Available shortcuts: {availableTypes}");
            }

            // ウィンドウを取得 (開いていない場合は開く)
            EditorWindow window;
            try
            {
                window = EditorWindow.GetWindow(windowType, false, null, false);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to get window: {ex.Message}");
            }

            if (window == null)
            {
                return ToolResult.Fail($"Could not get window of type: {typeName}");
            }

            // GrabPixels はフォーカス不要。
            window.Repaint();

            // デフォルトパス
            if (string.IsNullOrEmpty(outputPath))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var safeName = windowName.Replace(".", "_").Replace(" ", "_");
                var dir = "Temp/Screenshots";
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                outputPath = $"{dir}/{safeName}_{timestamp}.png";
            }
            else
            {
                // パストラバーサル対策: プロジェクトルート外への書き込みを防止
                var projectRoot = Path.GetFullPath(Application.dataPath + "/..");
                var fullPath = Path.GetFullPath(outputPath);

                if (!fullPath.StartsWith(projectRoot))
                {
                    return ToolResult.Fail("Output path must be within the project directory");
                }

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            try
            {
                int width, height;
                string captureMethod;

                // Game View の場合の特別処理
                if (typeName == "UnityEditor.GameView" && EditorApplication.isPlaying && gameOnly)
                {
                    // カメラから直接レンダリングしてゲーム画面のみをキャプチャ
                    if (!TryCaptureFromCamera(outputPath, out width, out height, out var errorMsg))
                    {
                        return ToolResult.Fail(errorMsg);
                    }
                    captureMethod = "CameraRender";
                }
                else
                {
                    // その他のウィンドウは GrabPixels を使用
                    var position = window.position;
                    width = (int)position.width;
                    height = (int)position.height;

                    // 最小サイズチェック
                    if (width < 10 || height < 10)
                    {
                        return ToolResult.Fail($"Window is too small: {width}x{height}");
                    }

                    // GUIView からキャプチャを試みる (内部 API)
                    Color[] pixels;
                    var captured = EditorWindowCapture.TryGrabPixels(window, out pixels, out width, out height);

                    if (!captured)
                    {
                        return ToolResult.Fail("Window capture not available. Try using capture-scene-view for Scene view.");
                    }

                    // Texture2D を作成してPNG保存
                    Texture2D texture = null;
                    try
                    {
                        texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                        texture.SetPixels(pixels);
                        texture.Apply();

                        var pngData = texture.EncodeToPNG();
                        File.WriteAllBytes(outputPath, pngData);
                    }
                    finally
                    {
                        if (texture != null)
                        {
                            UnityEngine.Object.DestroyImmediate(texture);
                        }
                    }
                    captureMethod = "GrabPixels";
                }

                // Assets フォルダに保存された場合のみ AssetDatabase を更新
                if (outputPath.StartsWith("Assets/"))
                {
                    AssetDatabase.Refresh();
                }

                return ToolResult.Ok(new CaptureResult
                {
                    success = true,
                    path = outputPath,
                    window_type = typeName,
                    width = width,
                    height = height,
                    capture_method = captureMethod,
                    warning = warning,
                    image_base64 = returnImage ? Convert.ToBase64String(File.ReadAllBytes(outputPath)) : null,
                    image_mime_type = returnImage ? "image/png" : null
                });
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Failed to capture window: {ex.Message}");
            }
        }

        /// <summary>
        /// メインカメラから直接レンダリングしてキャプチャ（エディタUIを含まない）
        /// </summary>
        private bool TryCaptureFromCamera(string outputPath, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;

            var camera = Camera.main;
            if (camera == null)
            {
                error = "No main camera found. Ensure a camera with 'MainCamera' tag exists.";
                return false;
            }

            RenderTexture rt = null;
            var prevTarget = camera.targetTexture;
            var prevActive = RenderTexture.active;
            Texture2D texture = null;

            try
            {
                // Game View のサイズを取得
                var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
                var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                var sizeInfo = GetGameViewSize(gameView);

                width = sizeInfo.width > 0 ? sizeInfo.width : Screen.width;
                height = sizeInfo.height > 0 ? sizeInfo.height : Screen.height;

                // RenderTexture を作成
                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 1;

                // カメラのターゲットを一時的に変更
                camera.targetTexture = rt;
                camera.Render();

                // RenderTexture から読み取り
                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                // PNG として保存
                var pngData = texture.EncodeToPNG();
                File.WriteAllBytes(outputPath, pngData);

                return true;
            }
            catch (Exception ex)
            {
                error = $"Camera capture error: {ex.Message}";
                return false;
            }
            finally
            {
                // rt を破棄する前に、例外時も含めて必ず元の状態へ復元する
                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        /// <summary>
        /// Game View のサイズを取得
        /// </summary>
        private (int width, int height) GetGameViewSize(EditorWindow gameView)
        {
            try
            {
                // Game View の現在のサイズを取得（リフレクション）
                var positionProp = gameView.GetType().GetProperty("position", BindingFlags.Public | BindingFlags.Instance);
                if (positionProp != null)
                {
                    var rect = (Rect)positionProp.GetValue(gameView);
                    // ツールバーの高さを引く（約17ピクセル + タブの高さ約19ピクセル）
                    var toolbarHeight = 36;
                    var pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                    return (
                        (int)(rect.width * pixelsPerPoint),
                        (int)((rect.height - toolbarHeight) * pixelsPerPoint)
                    );
                }
            }
            catch
            {
                // フォールバック
            }

            return (Screen.width, Screen.height);
        }

        /// <summary>
        /// Game View のゲーム画面のみをキャプチャ (ScreenCapture 使用)
        /// </summary>
        /// <returns>成功した場合は true</returns>
        private bool TryCaptureGameViewScreenshot(string outputPath, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = null;

            Texture2D screenshot = null;
            try
            {
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                if (screenshot == null)
                {
                    error = "ScreenCapture failed. Ensure Game View is visible and in play mode.";
                    return false;
                }

                width = screenshot.width;
                height = screenshot.height;

                var pngData = screenshot.EncodeToPNG();
                File.WriteAllBytes(outputPath, pngData);

                return true;
            }
            catch (Exception ex)
            {
                error = $"ScreenCapture error: {ex.Message}";
                return false;
            }
            finally
            {
                if (screenshot != null)
                {
                    UnityEngine.Object.DestroyImmediate(screenshot);
                }
            }
        }
    }
}
