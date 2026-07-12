using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UniForge
{
    /// <summary>
    /// Console log capture that survives domain reload using ScriptableSingleton.
    /// Uses CircularBuffer for O(1) operations.
    /// </summary>
    public partial class ConsoleLogCapture : ScriptableSingleton<ConsoleLogCapture>
    {
        private const int DefaultMaxLogs = 1000;
        private const int MinLogs = 100;
        private const int PersistedLogsCount = 50;

        [NonSerialized]
        private CircularBuffer<LogEntry> _logBuffer;

        [SerializeField]
        private List<LogEntry> _persistedLogs = new List<LogEntry>();

        [SerializeField]
        private int _maxLogs = DefaultMaxLogs;

        private readonly object _lock = new object();
        private bool _isSubscribed;

        private CircularBuffer<LogEntry> LogBuffer
        {
            get
            {
                if (_logBuffer == null)
                {
                    _logBuffer = new CircularBuffer<LogEntry>(_maxLogs);
                    RestorePersistedLogs();
                }
                return _logBuffer;
            }
        }

        public event Action<LogEntry> OnLogReceived;

        /// <summary>
        /// 最大ログ件数（デフォルト: 1000）
        /// </summary>
        public int MaxLogs
        {
            get => _maxLogs;
            set
            {
                var newMax = Mathf.Max(MinLogs, value);
                if (newMax != _maxLogs)
                {
                    _maxLogs = newMax;
                    RebuildBuffer();
                }
            }
        }
    }

    /// <summary>
    /// Initializes ConsoleLogCapture on domain reload
    /// </summary>
    [InitializeOnLoad]
    internal static class ConsoleLogCaptureInitializer
    {
        static ConsoleLogCaptureInitializer()
        {
            // Domain Reload 後にログキャプチャを再登録
            ConsoleLogCapture.instance.EnsureSubscribed();
        }
    }
}
