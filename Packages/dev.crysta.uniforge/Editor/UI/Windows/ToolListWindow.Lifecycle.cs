using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniForge.Tools;

namespace UniForge
{
    public partial class ToolListWindow
    {
        private void OnEnable()
        {
            _needsRefresh = true;
            ToolSettings.instance.OnSettingsChanged -= OnSettingsChanged;
            ToolSettings.instance.OnSettingsChanged += OnSettingsChanged;
            CompilationWatcher.Instance.OnCompilationFinished -= OnCompilationFinished;
            CompilationWatcher.Instance.OnCompilationFinished += OnCompilationFinished;
        }

        private void OnDisable()
        {
            ToolSettings.instance.OnSettingsChanged -= OnSettingsChanged;
            CompilationWatcher.Instance.OnCompilationFinished -= OnCompilationFinished;
        }

        private void OnSettingsChanged()
        {
            _needsRefresh = true;
            Repaint();
        }

        private void OnCompilationFinished(bool success)
        {
            if (success)
            {
                ToolSettings.ClearContextSizeCache();
                _needsRefresh = true;
                Repaint();
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _descriptionStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = false,
                fontSize = 10,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };

            _contextSizeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 9,
                normal = { textColor = new Color(0.5f, 0.7f, 0.5f) }
            };

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };

            _toolNameStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Normal
            };

            _kindLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(2, 2, 0, 0)
            };

            _categoryHeaderStyle = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold
            };
        }

        private void RefreshToolCache()
        {
            if (!_needsRefresh) return;
            _needsRefresh = false;

            var registry = UniForgeService.instance.ToolRegistry;
            if (registry == null)
            {
                _toolInfoCache = new List<ToolInfo>();
                return;
            }

            _toolInfoCache = registry.GetAllDefinitions()
                .Select(def => new ToolInfo
                {
                    Definition = def,
                    IsEnabled = ToolSettings.instance.IsToolEnabled(def.name),
                    ContextSize = ToolSettings.EstimateContextSize(def)
                })
                .OrderBy(t => t.Definition.category)
                .ThenBy(t => t.Definition.name)
                .ToList();

            foreach (var category in _toolInfoCache.Select(t => t.Definition.category).Distinct())
            {
                if (!_categoryFoldouts.ContainsKey(category))
                {
                    _categoryFoldouts[category] = true;
                }
            }
        }

        private void OnGUI()
        {
            InitStyles();
            RefreshToolCache();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(5);
            DrawFilterSection();
            EditorGUILayout.Space(5);
            DrawToolList();
        }

        private void NotifyToolsChanged()
        {
            _needsRefresh = true;

            if (UniForgeService.instance.IsConnected)
            {
                EditorApplication.delayCall += () =>
                {
                    UniForgeService.instance.NotifyToolSettingsChanged();
                };
            }
        }
    }
}
