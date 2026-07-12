using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniForge.Tools;

namespace UniForge
{
    public partial class ToolListWindow
    {
        private void DrawHeader()
        {
            var registry = UniForgeService.instance.ToolRegistry;
            if (registry == null || _toolInfoCache == null)
            {
                EditorGUILayout.LabelField("Tool Registry not initialized", _headerStyle);
                return;
            }

            int totalTools = _toolInfoCache.Count;
            int enabledCount = _toolInfoCache.Count(t => t.IsEnabled);
            int disabledCount = totalTools - enabledCount;
            int totalContext = _toolInfoCache.Where(t => t.IsEnabled).Sum(t => t.ContextSize);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("UniForge Tools", _headerStyle, GUILayout.Width(100));

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Enable All", GUILayout.Width(70)))
                {
                    ToolSettings.instance.EnableAllTools();
                    NotifyToolsChanged();
                }
                if (GUILayout.Button("Disable All", GUILayout.Width(70)))
                {
                    var allNames = _toolInfoCache.Select(t => t.Definition.name);
                    ToolSettings.instance.DisableAllTools(allNames);
                    NotifyToolsChanged();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"Total: {totalTools} | Enabled: {enabledCount} | Disabled: {disabledCount}",
                    EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                var contextLabel = FormatContextSize(totalContext);
                EditorGUILayout.LabelField($"Context: ~{contextLabel}", _contextSizeStyle, GUILayout.Width(120));
            }
        }

        private void DrawFilterSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Filter:", GUILayout.Width(40));
                _filterText = EditorGUILayout.TextField(_filterText);

                if (GUILayout.Button("Clear", GUILayout.Width(50)) && !string.IsNullOrEmpty(_filterText))
                {
                    _filterText = "";
                    GUI.FocusControl(null);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _showQueries = GUILayout.Toggle(_showQueries, "Queries", EditorStyles.miniButton);
                _showMutations = GUILayout.Toggle(_showMutations, "Mutations", EditorStyles.miniButton);
                GUILayout.Space(10);
                _showEnabledOnly = GUILayout.Toggle(_showEnabledOnly, "Enabled", EditorStyles.miniButton);
                _showDisabledOnly = GUILayout.Toggle(_showDisabledOnly, "Disabled", EditorStyles.miniButton);
                GUILayout.Space(10);
                _groupByCategory = GUILayout.Toggle(_groupByCategory, "Group", EditorStyles.miniButton);

                if (_showEnabledOnly && _showDisabledOnly && Event.current.type == EventType.MouseUp)
                {
                    _showEnabledOnly = false;
                }
            }
        }

        private void DrawToolList()
        {
            if (_toolInfoCache == null || _toolInfoCache.Count == 0)
            {
                EditorGUILayout.HelpBox("No tools registered.", MessageType.Info);
                return;
            }

            var filteredTools = GetFilteredTools();

            if (filteredTools.Count == 0)
            {
                EditorGUILayout.HelpBox("No tools match the current filter.", MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (_groupByCategory)
            {
                DrawToolListGrouped(filteredTools);
            }
            else
            {
                foreach (var tool in filteredTools)
                {
                    DrawToolItem(tool);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolListGrouped(List<ToolInfo> filteredTools)
        {
            var groupedTools = filteredTools
                .GroupBy(t => t.Definition.category)
                .OrderBy(g => g.Key, Comparer<string>.Create(ToolCategory.CompareOrder));

            foreach (var group in groupedTools)
            {
                var category = group.Key;
                var tools = group.ToList();
                var enabledInCategory = tools.Count(t => t.IsEnabled);
                var categoryContext = tools.Where(t => t.IsEnabled).Sum(t => t.ContextSize);

                if (!_categoryFoldouts.ContainsKey(category))
                {
                    _categoryFoldouts[category] = true;
                }

                EditorGUILayout.BeginHorizontal();

                _categoryFoldouts[category] = EditorGUILayout.Foldout(
                    _categoryFoldouts[category],
                    $"{category} ({enabledInCategory}/{tools.Count})",
                    true,
                    _categoryHeaderStyle);

                GUILayout.FlexibleSpace();

                EditorGUILayout.LabelField($"~{FormatContextSize(categoryContext)}", _contextSizeStyle, GUILayout.Width(80));

                if (GUILayout.Button("All", GUILayout.Width(30)))
                {
                    foreach (var tool in tools)
                    {
                        ToolSettings.instance.SetToolEnabled(tool.Definition.name, true);
                        tool.IsEnabled = true;
                    }
                    NotifyToolsChanged();
                }
                if (GUILayout.Button("None", GUILayout.Width(40)))
                {
                    foreach (var tool in tools)
                    {
                        ToolSettings.instance.SetToolEnabled(tool.Definition.name, false);
                        tool.IsEnabled = false;
                    }
                    NotifyToolsChanged();
                }

                EditorGUILayout.EndHorizontal();

                if (_categoryFoldouts[category])
                {
                    EditorGUI.indentLevel++;
                    foreach (var tool in tools)
                    {
                        DrawToolItem(tool);
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(2);
            }
        }

        private List<ToolInfo> GetFilteredTools()
        {
            return _toolInfoCache
                .Where(t =>
                {
                    if (t.Definition.IsQuery && !_showQueries) return false;
                    if (t.Definition.IsMutation && !_showMutations) return false;
                    if (_showEnabledOnly && !t.IsEnabled) return false;
                    if (_showDisabledOnly && t.IsEnabled) return false;

                    if (!string.IsNullOrEmpty(_filterText))
                    {
                        var filter = _filterText.ToLowerInvariant();
                        var nameMatch = t.Definition.name.ToLowerInvariant().Contains(filter);
                        var descMatch = t.Definition.description?.ToLowerInvariant().Contains(filter) ?? false;
                        var catMatch = t.Definition.category?.ToLowerInvariant().Contains(filter) ?? false;
                        if (!nameMatch && !descMatch && !catMatch) return false;
                    }

                    return true;
                })
                .ToList();
        }

        private void DrawToolItem(ToolInfo tool)
        {
            var def = tool.Definition;
            var isEnabled = tool.IsEnabled;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var newEnabled = EditorGUILayout.Toggle(isEnabled, GUILayout.Width(20));
                if (newEnabled != isEnabled)
                {
                    ToolSettings.instance.SetToolEnabled(def.name, newEnabled);
                    tool.IsEnabled = newEnabled;
                    NotifyToolsChanged();
                }

                var kindLabel = def.IsQuery ? "Q" : "M";
                var kindColor = def.IsQuery ? new Color(0.4f, 0.6f, 0.9f) : new Color(0.9f, 0.6f, 0.4f);
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = kindColor;
                GUILayout.Label(kindLabel, _kindLabelStyle, GUILayout.Width(18), GUILayout.Height(16));
                GUI.backgroundColor = oldBg;

                using (new EditorGUILayout.VerticalScope())
                {
                    var nameStyle = isEnabled ? _toolNameStyle : EditorStyles.miniLabel;
                    if (!isEnabled)
                    {
                        GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    }
                    EditorGUILayout.LabelField(def.name, nameStyle);
                    GUI.color = Color.white;

                    var desc = def.description ?? "";
                    if (desc.Length > 80)
                    {
                        desc = desc.Substring(0, 77) + "...";
                    }
                    EditorGUILayout.LabelField(desc, _descriptionStyle);
                }

                var contextLabel = FormatContextSize(tool.ContextSize);
                EditorGUILayout.LabelField($"~{contextLabel}", _contextSizeStyle, GUILayout.Width(60));
            }
        }

        private string FormatContextSize(int tokens)
        {
            if (tokens >= 1000)
            {
                return $"{tokens / 1000f:F1}k";
            }
            return $"{tokens}";
        }
    }
}
