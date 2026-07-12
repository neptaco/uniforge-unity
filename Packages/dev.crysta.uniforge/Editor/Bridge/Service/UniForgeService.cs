using System;
using UnityEditor;
using UnityEngine;
using UniForge.Tools;

namespace UniForge
{
    /// <summary>
    /// Headless UniForge service that runs without a window.
    /// Handles daemon connection and tool execution in the background.
    /// Uses ScriptableSingleton to properly handle domain reload and play mode transitions.
    /// </summary>
    public partial class UniForgeService : ScriptableSingleton<UniForgeService>
    {
        [NonSerialized] private TcpTransportClient _transport;
        [NonSerialized] private ToolRegistry _toolRegistry;
        [NonSerialized] private ToolDispatcher _toolDispatcher;
        [NonSerialized] private bool _initialized;
        [NonSerialized] private bool _editorUpdateRegistered;

        public bool IsConnected => _transport?.IsConnected ?? false;
        public bool IsConnecting => _transport?.IsConnecting ?? false;
        public int ReconnectAttempts => _transport?.ReconnectAttempts ?? 0;
        public int MaxReconnects => TcpTransportClient.MaxReconnects;

        public string LastError => _transport?.LastError;
        public ToolRegistry ToolRegistry => _toolRegistry;

        [NonSerialized] private static bool _initializationPending;
        [NonSerialized] private static bool _toolUpdatePending;

        // Throttling for pending request polling (reduces CPU usage)
        // Note: EditorApplication.update is NOT 60 FPS - it depends on editor repaint rate
        // Initialize to negative infinity so first poll runs immediately
        [NonSerialized] private double _lastPollTime = double.NegativeInfinity;
        private const double PollIntervalSeconds = 0.1; // 10 times per second max

    }
}
