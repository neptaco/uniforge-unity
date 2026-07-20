using UnityEditor;
using UnityEngine;

namespace UniForge
{
    /// <summary>
    /// UI Window for UniForge.
    /// Connection and tool execution is handled by UniForgeService.
    /// </summary>
    public class UniForgeWindow : EditorWindow
    {
        private const string AutoConnectPrefKey = "UniForge_AutoConnect";
        private static UniForgeWindow _instance;
        private bool _autoConnect = true;
        private Vector2 _scrollPosition;
        private double _lastRepaintTime;

        [MenuItem("Window/UniForge/Connection")]
        public static void ShowWindow()
        {
            _instance = GetWindow<UniForgeWindow>("UniForge");
        }

        private void OnEnable()
        {
            _instance = this;

            // Load settings for display
            _autoConnect = EditorPrefs.GetBool(AutoConnectPrefKey, true);

            EditorApplication.update -= RepaintIfNeeded;
            EditorApplication.update += RepaintIfNeeded;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintIfNeeded;
        }

        private void RepaintIfNeeded()
        {
            // Repaint every second so connection status stays up to date
            if (EditorApplication.timeSinceStartup - _lastRepaintTime >= 1.0)
            {
                _lastRepaintTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            // Connection settings
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);

            _autoConnect = EditorGUILayout.Toggle("Auto Connect", _autoConnect);
            if (GUI.changed)
            {
                EditorPrefs.SetBool(AutoConnectPrefKey, _autoConnect);
            }

            // Status
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            var service = UniForgeService.instance;
            bool isConnected = service.IsConnected;
            bool isConnecting = service.IsConnecting;

            // Connection status with indicator
            using (new EditorGUILayout.HorizontalScope())
            {
                string statusText;
                Color statusColor;

                if (isConnected)
                {
                    statusText = "Online";
                    statusColor = Color.green;
                }
                else if (isConnecting)
                {
                    statusText = "Connecting...";
                    statusColor = Color.yellow;
                }
                else if (service.ReconnectAttempts > 0)
                {
                    statusText = $"Reconnecting ({service.ReconnectAttempts}/{service.MaxReconnects})";
                    statusColor = Color.yellow;
                }
                else
                {
                    statusText = "Offline";
                    statusColor = Color.gray;
                }

                EditorGUILayout.LabelField("Status:", statusText);

                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = statusColor;
                GUILayout.Box("", GUILayout.Width(20), GUILayout.Height(20));
                GUI.backgroundColor = oldColor;
            }

            if (!isConnected && !string.IsNullOrEmpty(service.LastError))
            {
                EditorGUILayout.HelpBox(service.LastError, MessageType.Info);
            }

            var packageUpdateState = PackageUpdateState.instance;
            if (packageUpdateState.IsUpdateAvailable)
            {
                EditorGUILayout.LabelField(
                    $"Update available: v{packageUpdateState.CurrentPackageVersion} -> v{packageUpdateState.LatestPackageVersion}",
                    EditorStyles.miniLabel);
            }

            // Connect/Disconnect button
            EditorGUILayout.Space();
            if (isConnected)
            {
                if (GUILayout.Button("Disconnect"))
                {
                    service.Disconnect();
                }
            }
            else
            {
                if (GUILayout.Button("Connect"))
                {
                    service.Connect();
                }
            }

            // Project info
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Project", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("ID:", ProjectIdentifier.GetProjectId());
            EditorGUILayout.LabelField("Name:", ProjectIdentifier.GetProjectName());

            // Compilation status
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compilation", EditorStyles.boldLabel);

            var compileStatus = CompilationWatcher.Instance.GetStatus();
            EditorGUILayout.LabelField("Status:",
                compileStatus.isCompiling ? "Compiling..." :
                compileStatus.success ? "Success" : "Failed");
            EditorGUILayout.LabelField("Errors:", compileStatus.errors.Count.ToString());
            EditorGUILayout.LabelField("Warnings:", compileStatus.warnings.Count.ToString());

            if (GUILayout.Button("Request Compilation"))
            {
                CompilationWatcher.Instance.RequestCompilation();
            }

            // Tools
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Exposed Tools", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Open Tool List", GUILayout.Width(100)))
                {
                    ToolListWindow.ShowWindow();
                }
            }

            var toolRegistry = UniForgeService.instance.ToolRegistry;
            if (toolRegistry != null)
            {
                int totalCount = toolRegistry.Count;
                int enabledCount = toolRegistry.EnabledCount;
                int disabledCount = totalCount - enabledCount;

                EditorGUILayout.LabelField($"  Total: {totalCount} | Enabled: {enabledCount} | Disabled: {disabledCount}");

                // Context size estimate
                int contextSize = ToolSettings.instance.CalculateTotalEnabledContextSize(toolRegistry);
                string contextLabel = contextSize >= 1000
                    ? $"~{contextSize / 1000f:F1}k tokens"
                    : $"~{contextSize} tokens";
                EditorGUILayout.LabelField($"  Estimated context: {contextLabel}");
            }
        }
    }

    // Note: Initializer moved to UniForgeService.cs for headless operation

}
