using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UniForge.Tools;

namespace UniForge
{
    /// <summary>
    /// Editor window for viewing and managing MCP tools.
    /// Allows filtering, enabling/disabling tools, and viewing context size.
    /// </summary>
    public partial class ToolListWindow : EditorWindow
    {
        private string _filterText = "";
        private Vector2 _scrollPosition;
        private bool _showQueries = true;
        private bool _showMutations = true;
        private bool _showEnabledOnly = false;
        private bool _showDisabledOnly = false;
        private bool _groupByCategory = true;

        // Cache for tool info
        private List<ToolInfo> _toolInfoCache;
        private bool _needsRefresh = true;

        // Category foldout states
        private Dictionary<string, bool> _categoryFoldouts = new Dictionary<string, bool>();

        // Styles
        private GUIStyle _descriptionStyle;
        private GUIStyle _contextSizeStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _toolNameStyle;
        private GUIStyle _kindLabelStyle;
        private GUIStyle _categoryHeaderStyle;
        [NonSerialized] private bool _stylesInitialized;

        [MenuItem("Window/UniForge/Tool List")]
        public static void ShowWindow()
        {
            var window = GetWindow<ToolListWindow>("UniForge Tools");
            window.minSize = new Vector2(400, 300);
        }

        private class ToolInfo
        {
            public ToolDefinition Definition;
            public bool IsEnabled;
            public int ContextSize;
        }
    }
}
