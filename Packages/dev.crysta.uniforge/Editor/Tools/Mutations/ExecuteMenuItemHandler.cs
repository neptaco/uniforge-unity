using UnityEditor;
using UnityEngine;

namespace UniForge.Tools.Mutations
{
    /// <summary>
    /// Execute a Unity Editor menu item command.
    /// Uses IToolHandler directly instead of MutationHandler because menu items
    /// (especially Edit/Undo and Edit/Redo) manage their own Undo operations
    /// and wrapping them in another Undo group would interfere with their behavior.
    /// </summary>
    [Tool("execute-menu-item",
        Description = "Execute a Unity Editor menu item by path (e.g., 'File/Save Project', 'Window/General/Console')",
        Title = "Execute Menu Item",
        Category = ToolCategory.Editor,
        Kind = ToolKind.Mutation,
        Idempotent = false)]
    public class ExecuteMenuItemHandler : IToolHandler
    {
        public class Args
        {
            [ToolParameter("Menu item path (e.g., 'File/Save Project', 'Edit/Undo')", Required = true)]
            public string menu_path;
        }

        public class Output
        {
            public bool success;
            public string menu_path;
            public string message;
        }

        private ToolDefinition _definition;

        public ToolDefinition Definition
        {
            get
            {
                _definition ??= ToolDefinitionBuilder.FromHandler<ExecuteMenuItemHandler>();
                return _definition;
            }
        }

#pragma warning disable CS1998
        public async Awaitable<ToolResult> ExecuteAsync(string argsJson)
            => Execute(argsJson);
#pragma warning restore CS1998

        private ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            var menuPath = args.GetString("menu_path", null);

            if (string.IsNullOrEmpty(menuPath))
            {
                return ToolResult.Fail("menu_path is required");
            }

            // Validate menu item exists
            if (!EditorApplication.ExecuteMenuItem(menuPath))
            {
                return ToolResult.Ok(new Output
                {
                    success = false,
                    menu_path = menuPath,
                    message = $"Menu item not found or failed to execute: {menuPath}"
                });
            }

            Debug.Log($"[UniForge] Executed menu item: {menuPath}");

            return ToolResult.Ok(new Output
            {
                success = true,
                menu_path = menuPath,
                message = $"Successfully executed: {menuPath}"
            });
        }
    }
}
