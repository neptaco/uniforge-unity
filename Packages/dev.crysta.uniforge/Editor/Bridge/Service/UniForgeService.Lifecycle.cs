using System;
using UnityEditor;
using UnityEngine;
using UniForge.Tools;
using UniForge.TestRunner;

namespace UniForge
{
    public partial class UniForgeService
    {
        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            try
            {
                DomainReloadTracker.instance.MarkDomainReload();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UniForge] MarkDomainReload error: {ex}");
            }

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            _initializationPending = true;
            EditorApplication.update -= OnEditorUpdateInit;
            EditorApplication.update += OnEditorUpdateInit;
        }

        private static void OnEditorUpdateInit()
        {
            if (!_initializationPending) return;
            _initializationPending = false;
            EditorApplication.update -= OnEditorUpdateInit;

            try
            {
                instance.Initialize();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UniForge] Initialize error: {ex}");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    instance.Disconnect();
                    instance._lastPollTime = double.NegativeInfinity;
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    instance._initialized = false;
                    instance._lastPollTime = double.NegativeInfinity;
                    _initializationPending = true;
                    EditorApplication.update -= OnEditorUpdateInit;
                    EditorApplication.update += OnEditorUpdateInit;
                    break;

                case PlayModeStateChange.EnteredEditMode:
                    instance._initialized = false;
                    instance._lastPollTime = double.NegativeInfinity;
                    _initializationPending = true;
                    EditorApplication.update -= OnEditorUpdateInit;
                    EditorApplication.update += OnEditorUpdateInit;
                    break;
            }
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var autoConnect = EditorPrefs.GetBool("UniForge_AutoConnect", true);

            InitializeToolRegistry();
            _ = CompilationWatcher.Instance;
            InitializeTestRunnerCache();

            if (!_editorUpdateRegistered)
            {
                EditorApplication.update += OnEditorUpdate;
                _editorUpdateRegistered = true;
            }

            if (autoConnect)
            {
                Connect();
            }

            Debug.Log("[UniForge] Service initialized (headless mode)");
        }

        private void InitializeToolRegistry()
        {
            _toolRegistry = new ToolRegistry();
            _toolRegistry.RegisterAllToolHandlers();
            _toolDispatcher = new ToolDispatcher(_toolRegistry);
            Debug.Log($"[UniForge] Registered {_toolRegistry.Count} tools");

            CompilationWatcher.Instance.OnCompilationFinished -= OnCompilationFinishedForToolUpdate;
            CompilationWatcher.Instance.OnCompilationFinished += OnCompilationFinishedForToolUpdate;
        }

        private void OnCompilationFinishedForToolUpdate(bool success)
        {
            if (!success) return;
            if (_toolUpdatePending) return;
            _toolUpdatePending = true;

            EditorApplication.delayCall += () =>
            {
                try
                {
                    var previousCount = _toolRegistry?.Count ?? 0;
                    var previousTools = _toolRegistry?.ToRegistrationList();

                    _toolRegistry = new ToolRegistry();
                    _toolRegistry.RegisterAllToolHandlers();
                    _toolDispatcher = new ToolDispatcher(_toolRegistry);

                    var newCount = _toolRegistry.Count;
                    var newTools = _toolRegistry.ToRegistrationList();

                    bool changed = previousCount != newCount
                        || !UniForgeToolDefinitionComparer.AreEqual(previousTools, newTools);

                    if (changed && _transport?.IsConnected == true)
                    {
                        Debug.Log($"[UniForge] Tools changed after compilation ({previousCount} -> {newCount}), sending update");
                        SendToolsUpdate();
                    }
                }
                finally
                {
                    _toolUpdatePending = false;
                }
            };
        }

        private void SendToolsUpdate()
        {
            if (_transport == null || !_transport.IsConnected) return;

            var tools = _toolRegistry.ToEnabledRegistrationList();
            var updateMsg = UniForgeProtocolMessages.BuildUnityToolsUpdateNotification(
                ProjectIdentifier.GetProjectId(),
                tools);
            _transport.Send(updateMsg);
        }

        /// <summary>
        /// Notify that tool settings have changed (enable/disable)
        /// </summary>
        public void NotifyToolSettingsChanged()
        {
            SendToolsUpdate();
        }

        private void InitializeTestRunnerCache()
        {
#if UNITY_INCLUDE_TESTS
            var service = TestRunnerService.instance;
            if (!service.IsCacheReady)
            {
                service.RefreshCache();
            }
            CompilationWatcher.Instance.OnCompilationFinished -= OnCompilationFinishedForTestCache;
            CompilationWatcher.Instance.OnCompilationFinished += OnCompilationFinishedForTestCache;
#endif
        }

#if UNITY_INCLUDE_TESTS
        private void OnCompilationFinishedForTestCache(bool success)
        {
            if (success)
            {
                EditorApplication.delayCall += () =>
                {
                    TestRunnerService.instance.RefreshCache();
                };
            }
        }
#endif

        private void OnEditorUpdate()
        {
            try
            {
                _transport?.ProcessMainThreadQueue();

                var currentTime = EditorApplication.timeSinceStartup;
                if (currentTime - _lastPollTime >= PollIntervalSeconds)
                {
                    _lastPollTime = currentTime;
                    PendingDomainReloadToolRequestProcessor.ProcessPendingRequests(_toolDispatcher, _transport);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UniForge] OnEditorUpdate error: {ex}");
            }
        }

        private void OnDisable()
        {
            Disconnect();

            _toolRegistry = null;
            _toolDispatcher = null;

            _initializationPending = false;
            _toolUpdatePending = false;
            EditorApplication.update -= OnEditorUpdateInit;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            _initialized = false;
            _editorUpdateRegistered = false;
        }
    }
}
