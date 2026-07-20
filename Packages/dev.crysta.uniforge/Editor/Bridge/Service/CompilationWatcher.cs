using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UniForge
{
    /// <summary>
    /// Monitors Unity script compilation and provides status/error information.
    /// </summary>
    [Serializable]
    public class CompilerError
    {
        public string message;
        public string file;
        public int line;
        public int column;
        public string type; // "error" or "warning"

        public CompilerError(CompilerMessage msg)
        {
            message = msg.message;
            file = msg.file;
            line = msg.line;
            column = msg.column;
            type = msg.type == CompilerMessageType.Error ? "error" : "warning";
        }

        internal CompilerError(string message, string file, int line, int column, string type)
        {
            this.message = message;
            this.file = file;
            this.line = line;
            this.column = column;
            this.type = type;
        }
    }

    [Serializable]
    public class CompileStatus
    {
        public bool isCompiling;
        public long lastCompileTime;
        public List<CompilerError> errors = new List<CompilerError>();
        public List<CompilerError> warnings = new List<CompilerError>();
        public bool success;
    }

    public class CompilationWatcher
    {
        private static CompilationWatcher _instance;
        public static CompilationWatcher Instance => _instance ??= new CompilationWatcher();

        private bool _isCompiling;
        private long _lastCompileStartTime;
        private long _lastCompileEndTime;
        private readonly List<CompilerError> _errors = new List<CompilerError>();
        private readonly List<CompilerError> _warnings = new List<CompilerError>();
        private bool _lastCompileSuccess = true;

        public event Action OnCompilationStarted;
        public event Action<bool> OnCompilationFinished; // bool = success

        public bool IsCompiling => _isCompiling || EditorApplication.isCompiling;

        private CompilationWatcher()
        {
            // Subscribe to compilation events
            CompilationPipeline.compilationStarted += OnCompilationStartedInternal;
            CompilationPipeline.compilationFinished += OnCompilationFinishedInternal;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        ~CompilationWatcher()
        {
            CompilationPipeline.compilationStarted -= OnCompilationStartedInternal;
            CompilationPipeline.compilationFinished -= OnCompilationFinishedInternal;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
        }

        private void OnCompilationStartedInternal(object context)
        {
            _isCompiling = true;
            _lastCompileStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _errors.Clear();
            _warnings.Clear();

            Debug.Log("[UniForge] Compilation started");
            OnCompilationStarted?.Invoke();
        }

        private void OnCompilationFinishedInternal(object context)
        {
            _isCompiling = false;
            _lastCompileEndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _lastCompileSuccess = _errors.Count == 0;

            Debug.Log($"[UniForge] Compilation finished - Success: {_lastCompileSuccess}, Errors: {_errors.Count}, Warnings: {_warnings.Count}");
            OnCompilationFinished?.Invoke(_lastCompileSuccess);
        }

        private void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var msg in messages)
            {
                var error = new CompilerError(msg);
                if (msg.type == CompilerMessageType.Error)
                {
                    _errors.Add(error);
                }
                else if (msg.type == CompilerMessageType.Warning)
                {
                    _warnings.Add(error);
                }
            }
        }

        /// <summary>
        /// Request script compilation
        /// </summary>
        public void RequestCompilation()
        {
            Debug.Log("[UniForge] Requesting script compilation");
            // Refresh asset database to pick up new/changed files
            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation();
        }

        /// <summary>
        /// Get current compilation status
        /// </summary>
        public CompileStatus GetStatus()
        {
            return new CompileStatus
            {
                isCompiling = IsCompiling,
                lastCompileTime = _lastCompileEndTime,
                errors = new List<CompilerError>(_errors),
                warnings = new List<CompilerError>(_warnings),
                success = _lastCompileSuccess
            };
        }

        /// <summary>
        /// Get errors only
        /// </summary>
        public List<CompilerError> GetErrors()
        {
            return new List<CompilerError>(_errors);
        }

        /// <summary>
        /// Get warnings only
        /// </summary>
        public List<CompilerError> GetWarnings()
        {
            return new List<CompilerError>(_warnings);
        }

        internal void SeedForTest(
            bool isCompiling,
            long lastCompileEndTime,
            bool success,
            List<CompilerError> errors,
            List<CompilerError> warnings)
        {
            _isCompiling = isCompiling;
            _lastCompileEndTime = lastCompileEndTime;
            _lastCompileSuccess = success;
            _errors.Clear();
            _errors.AddRange(errors);
            _warnings.Clear();
            _warnings.AddRange(warnings);
        }

        internal void ResetForTest()
        {
            _isCompiling = false;
            _lastCompileStartTime = 0;
            _lastCompileEndTime = 0;
            _lastCompileSuccess = true;
            _errors.Clear();
            _warnings.Clear();
        }
    }
}
