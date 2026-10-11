using System.Collections.Generic;
using UniForge.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniForge.Samples.ToolExamples
{
    /// <summary>
    /// Shows a mutation with required arguments, validation, and Undo recording.
    /// Finds a GameObject by hierarchy path, including inactive objects, and renames it.
    /// </summary>
    [Tool("example-rename-gameobject", Title = "Example: Rename GameObject",
        Kind = ToolKind.Mutation, Destructive = false, Idempotent = true, Category = "Examples",
        Description = "Rename a GameObject by hierarchy path with Undo support")]
    [ToolOutput(typeof(Output))]
    public class ExampleRenameGameObjectHandler : MutationHandler
    {
        public class Args
        {
            [ToolParameter("Hierarchy path, for example Parent/Child", Required = true)]
            public string path;

            [ToolParameter("New name without a slash", Required = true)]
            public string new_name;
        }

        public class Output
        {
            public string old_name;
            public string new_name;
            public string path;
            public int instance_id;
        }

        // Outside the package assembly, protected internal is overridden as protected.
        protected override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            string path = args.GetString("path");
            string newName = args.GetString("new_name");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName))
                return ToolResult.Fail("path and new_name must not be empty");
            if (newName.Contains("/"))
                return ToolResult.Fail("new_name must not contain '/'");

            var go = FindGameObject(path);
            if (go == null)
                return ToolResult.Fail($"GameObject not found at path '{path}'");

            string oldName = go.name;
            // MutationHandler groups this call under its Title; recording enables one-step Undo.
            Undo.RecordObject(go, "Example: Rename GameObject");
            go.name = newName;
            EditorSceneManager.MarkSceneDirty(go.scene);

            return ToolResult.Ok(new Output
            {
                old_name = oldName,
                new_name = go.name,
                path = GetHierarchyPath(go.transform),
                instance_id = go.GetInstanceID()
            });
        }

        private static GameObject FindGameObject(string path)
        {
            int separator = path.IndexOf('/');
            string rootName = separator < 0 ? path : path.Substring(0, separator);
            string childPath = separator < 0 ? null : path.Substring(separator + 1);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != rootName)
                        continue;
                    if (childPath == null)
                        return root;

                    var child = root.transform.Find(childPath);
                    if (child != null)
                        return child.gameObject;
                }
            }

            return null;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
