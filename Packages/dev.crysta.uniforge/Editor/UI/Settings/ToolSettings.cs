using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniForge.Tools;

namespace UniForge
{
    /// <summary>
    /// Pure data class for tool enable/disable state. Testable without Unity dependencies.
    /// </summary>
    public class ToolSettingsData
    {
        private readonly HashSet<string> _disabledTools;

        public event Action OnSettingsChanged;

        public ToolSettingsData() : this(Array.Empty<string>()) { }

        public ToolSettingsData(IEnumerable<string> disabledTools)
        {
            _disabledTools = new HashSet<string>(disabledTools ?? Array.Empty<string>());
        }

        public bool IsToolEnabled(string toolName)
        {
            return !_disabledTools.Contains(toolName);
        }

        public bool SetToolEnabled(string toolName, bool enabled)
        {
            bool changed = enabled
                ? _disabledTools.Remove(toolName)
                : _disabledTools.Add(toolName);

            if (changed)
            {
                OnSettingsChanged?.Invoke();
            }
            return changed;
        }

        public IReadOnlyCollection<string> GetDisabledTools()
        {
            return _disabledTools;
        }

        public bool EnableAllTools()
        {
            if (_disabledTools.Count > 0)
            {
                _disabledTools.Clear();
                OnSettingsChanged?.Invoke();
                return true;
            }
            return false;
        }

        public bool DisableAllTools(IEnumerable<string> toolNames)
        {
            bool changed = false;
            foreach (var name in toolNames)
            {
                if (_disabledTools.Add(name))
                {
                    changed = true;
                }
            }
            if (changed)
            {
                OnSettingsChanged?.Invoke();
            }
            return changed;
        }

        public string[] ToArray()
        {
            return _disabledTools.ToArray();
        }
    }

    /// <summary>
    /// Manages tool enable/disable settings.
    /// Uses ScriptableSingleton for proper Unity lifecycle management.
    /// </summary>
    [FilePath("Library/UniForge/ToolSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class ToolSettings : ScriptableSingleton<ToolSettings>
    {
        // Serialized to Library/UniForge/ (per-project, not committed to git)
        [SerializeField] private string[] _disabledToolsArray = Array.Empty<string>();

        [NonSerialized] private ToolSettingsData _data;
        [NonSerialized] private bool _loaded;

        // Cache for context size calculations (tool name -> token count)
        // Using ConcurrentDictionary for thread safety
        [NonSerialized] private static ConcurrentDictionary<string, int> _contextSizeCache = new ConcurrentDictionary<string, int>();

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Clear cache on Editor startup/domain reload to ensure fresh calculations
            ClearContextSizeCache();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Reset loaded flag when entering or exiting play mode
            // to ensure settings are reloaded from EditorPrefs
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                instance._loaded = false;
            }
        }

        /// <summary>Event raised when tool settings change</summary>
        public event Action OnSettingsChanged;

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            _data = new ToolSettingsData(_disabledToolsArray);
            _data.OnSettingsChanged += () =>
            {
                SaveSettings();
                OnSettingsChanged?.Invoke();
            };
        }

        public bool IsToolEnabled(string toolName)
        {
            EnsureLoaded();
            return _data.IsToolEnabled(toolName);
        }

        public void SetToolEnabled(string toolName, bool enabled)
        {
            EnsureLoaded();
            _data.SetToolEnabled(toolName, enabled);
        }

        public IReadOnlyCollection<string> GetDisabledTools()
        {
            EnsureLoaded();
            return _data.GetDisabledTools();
        }

        public void EnableAllTools()
        {
            EnsureLoaded();
            _data.EnableAllTools();
        }

        public void DisableAllTools(IEnumerable<string> toolNames)
        {
            EnsureLoaded();
            _data.DisableAllTools(toolNames);
        }

        private void SaveSettings()
        {
            _disabledToolsArray = _data.ToArray();
            Save(true);
        }

        /// <summary>
        /// Calculate context size for a tool definition (approximate token count).
        /// Results are cached until ClearContextSizeCache() is called.
        /// Thread-safe via ConcurrentDictionary.
        /// </summary>
        public static int EstimateContextSize(ToolDefinition definition)
        {
            if (definition == null) return 0;

            return _contextSizeCache.GetOrAdd(definition.name, _ =>
            {
                var regObj = definition.ToRegistrationObject();
                var json = SimpleJson.Serialize(regObj);
                return json.Length / 4;
            });
        }

        public static void ClearContextSizeCache()
        {
            _contextSizeCache.Clear();
        }

        public int CalculateTotalEnabledContextSize(ToolRegistry registry)
        {
            if (registry == null) return 0;

            int total = 0;
            foreach (var def in registry.GetAllDefinitions())
            {
                if (IsToolEnabled(def.name))
                {
                    total += EstimateContextSize(def);
                }
            }
            return total;
        }
    }
}
