using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// editor-state ツールの出力
    /// </summary>
    public class GetEditorStateOutput
    {
        public string unityVersion;
        public string platform;
        public string targetPlatform;
        public bool isPlaying;
        public bool isPaused;
        public bool isCompiling;
        public string projectPath;
        public string projectName;
        public ActiveSceneInfo activeScene;
        public List<LoadedSceneInfo> loadedScenes;
        public PrefabStageInfo prefabStage;
        public string scriptingBackend;
        public string apiCompatibility;
        public double timeSinceStartup;
    }

    /// <summary>
    /// Prefab Stage 情報
    /// </summary>
    public class PrefabStageInfo
    {
        public string assetPath;
        public string rootName;
        public int rootInstanceId;
    }

    /// <summary>
    /// アクティブシーン情報
    /// </summary>
    public class ActiveSceneInfo
    {
        public string name;
        public string path;
        public bool isDirty;
        public int buildIndex;
    }

    /// <summary>
    /// ロード済みシーン情報
    /// </summary>
    public class LoadedSceneInfo
    {
        public string name;
        public string path;
        public bool isLoaded;
        public bool isDirty;
        public int buildIndex;
    }

    /// <summary>
    /// エディタ状態取得ツール
    /// </summary>
    [Tool("editor-state",
        Description = "Get the current Unity Editor state including version, platform, scenes, and project info",
        Title = "Get Editor State",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Query,
        Idempotent = true)]
    [ToolOutput(typeof(GetEditorStateOutput))]
    public class GetEditorStateHandler : QueryHandler
    {
        private static readonly ToolDefinition _definition = new()
        {
            name = "editor-state",
            description = "Get the current Unity Editor state including version, platform, scenes, and project info",
            inputSchema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object>() },
                { "required", new List<string>() }
            },
            annotations = ToolAnnotations.ForQuery("Get Editor State")
        };

        public override ToolDefinition Definition => _definition;

        internal static GetEditorStateOutput CaptureState()
        {
            var activeScene = SceneManager.GetActiveScene();
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var buildTargetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            var namedBuildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);

            // ロード済みシーン一覧
            var loadedScenes = new List<LoadedSceneInfo>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                loadedScenes.Add(new LoadedSceneInfo
                {
                    name = scene.name,
                    path = scene.path,
                    isLoaded = scene.isLoaded,
                    isDirty = scene.isDirty,
                    buildIndex = scene.buildIndex
                });
            }

            // プロジェクトパス
            var dataPath = Application.dataPath;
            var projectPath = dataPath.Substring(0, dataPath.Length - "/Assets".Length);
            var projectName = System.IO.Path.GetFileName(projectPath);

            // スクリプティングバックエンド
            string scriptingBackend;
            try
            {
                scriptingBackend = PlayerSettings.GetScriptingBackend(namedBuildTarget).ToString();
            }
            catch
            {
                scriptingBackend = "Unknown";
            }

            // API 互換性レベル
            string apiCompatibility;
            try
            {
                apiCompatibility = PlayerSettings.GetApiCompatibilityLevel(namedBuildTarget).ToString();
            }
            catch
            {
                apiCompatibility = "Unknown";
            }

            // Prefab Stage 検出
            PrefabStageInfo prefabStageInfo = null;
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                prefabStageInfo = new PrefabStageInfo
                {
                    assetPath = prefabStage.assetPath,
                    rootName = prefabStage.prefabContentsRoot != null ? prefabStage.prefabContentsRoot.name : null,
                    rootInstanceId = prefabStage.prefabContentsRoot != null ? prefabStage.prefabContentsRoot.GetInstanceID() : 0
                };
            }

            return new GetEditorStateOutput
            {
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                targetPlatform = buildTarget.ToString(),
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                projectPath = projectPath,
                projectName = projectName,
                activeScene = new ActiveSceneInfo
                {
                    name = activeScene.name,
                    path = activeScene.path,
                    isDirty = activeScene.isDirty,
                    buildIndex = activeScene.buildIndex
                },
                loadedScenes = loadedScenes,
                prefabStage = prefabStageInfo,
                scriptingBackend = scriptingBackend,
                apiCompatibility = apiCompatibility,
                timeSinceStartup = EditorApplication.timeSinceStartup
            };
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            var output = CaptureState();

            return ToolResult.Ok(output);
        }
    }
}
