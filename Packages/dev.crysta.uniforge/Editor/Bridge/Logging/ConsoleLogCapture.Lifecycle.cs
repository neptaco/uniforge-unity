using System;
using UnityEngine;

namespace UniForge
{
    public partial class ConsoleLogCapture
    {
        public void EnsureSubscribed()
        {
            if (_isSubscribed) return;

            Application.logMessageReceived += OnLogMessageReceived;
            _isSubscribed = true;
        }

        private void OnDisable()
        {
            if (_isSubscribed)
            {
                Application.logMessageReceived -= OnLogMessageReceived;
                _isSubscribed = false;
            }

            PersistImportantLogs();
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            var entry = new LogEntry(condition, stackTrace, type);

            lock (_lock)
            {
                LogBuffer.Add(entry);
            }

            OnLogReceived?.Invoke(entry);
        }

        private void PersistImportantLogs()
        {
            lock (_lock)
            {
                if (_logBuffer == null || _logBuffer.Count == 0) return;

                _persistedLogs.Clear();

                int errorCount = 0;
                int maxErrors = PersistedLogsCount / 2;
                for (int i = _logBuffer.Count - 1; i >= 0 && errorCount < maxErrors; i--)
                {
                    var log = _logBuffer[i];
                    if (log.type == "Error" || log.type == "Exception" || log.type == "Assert" || log.type == "Warning")
                    {
                        _persistedLogs.Add(log);
                        errorCount++;
                    }
                }

                int remaining = PersistedLogsCount - _persistedLogs.Count;
                int startIdx = Mathf.Max(0, _logBuffer.Count - remaining);
                for (int i = startIdx; i < _logBuffer.Count && _persistedLogs.Count < PersistedLogsCount; i++)
                {
                    var log = _logBuffer[i];
                    if (!_persistedLogs.Contains(log))
                    {
                        _persistedLogs.Add(log);
                    }
                }

                _persistedLogs.Sort((a, b) => a.timestamp.CompareTo(b.timestamp));
            }
        }

        private void RestorePersistedLogs()
        {
            if (_persistedLogs == null || _persistedLogs.Count == 0) return;

            foreach (var log in _persistedLogs)
            {
                _logBuffer.Add(log);
            }

            _persistedLogs.Clear();
        }

        private void RebuildBuffer()
        {
            lock (_lock)
            {
                if (_logBuffer == null)
                {
                    _logBuffer = new CircularBuffer<LogEntry>(_maxLogs);
                    return;
                }

                var oldLogs = _logBuffer.ToList();
                _logBuffer = new CircularBuffer<LogEntry>(_maxLogs);

                int startIdx = Mathf.Max(0, oldLogs.Count - _maxLogs);
                for (int i = startIdx; i < oldLogs.Count; i++)
                {
                    _logBuffer.Add(oldLogs[i]);
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                LogBuffer.Clear();
                _persistedLogs?.Clear();
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return LogBuffer.Count;
                }
            }
        }
    }
}
