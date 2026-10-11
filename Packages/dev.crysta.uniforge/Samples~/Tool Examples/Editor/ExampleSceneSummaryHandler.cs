using System;
using System.Collections.Generic;
using UniForge.Tools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UniForge.Samples.ToolExamples
{
    /// <summary>
    /// Shows a read-only query with optional arguments and structured output.
    /// Counts GameObjects in loaded scenes using only Unity's public scene APIs.
    /// </summary>
    [Tool("example-scene-summary", Title = "Example: Scene Summary",
        Kind = ToolKind.Query, Idempotent = true, Category = "Examples",
        Description = "Summarize loaded scenes and count GameObjects with optional filters")]
    [ToolOutput(typeof(Output))]
    public class ExampleSceneSummaryHandler : QueryHandler
    {
        public class Args
        {
            [ToolParameter("Include inactive GameObjects in the count", Default = false)]
            public bool include_inactive;

            [ToolParameter("Count only names containing this text, ignoring case")]
            public string name_contains;
        }

        public class Output
        {
            public List<SceneSummary> scenes;
            public int total_gameobjects;
        }

        public class SceneSummary
        {
            public string name;
            public string path;
            public bool is_dirty;
            public int root_count;
            public int gameobject_count;
        }

        // Outside the package assembly, protected internal is overridden as protected.
        protected override ToolResult Execute(string argsJson)
        {
            var args = new ToolArgsParser(argsJson);
            bool includeInactive = args.GetBool("include_inactive", false);
            string nameContains = args.GetString("name_contains");
            var output = new Output { scenes = new List<SceneSummary>() };

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                var roots = scene.GetRootGameObjects();
                int count = 0;
                foreach (var root in roots)
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(includeInactive))
                    {
                        // Called on an inactive root, GetComponentsInChildren(false) still returns its hierarchy.
                        if (!includeInactive && !transform.gameObject.activeInHierarchy)
                            continue;

                        if (string.IsNullOrEmpty(nameContains) ||
                            transform.gameObject.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            count++;
                        }
                    }
                }

                output.scenes.Add(new SceneSummary
                {
                    name = scene.name,
                    path = scene.path,
                    is_dirty = scene.isDirty,
                    root_count = roots.Length,
                    gameobject_count = count
                });
                output.total_gameobjects += count;
            }

            return ToolResult.Ok(output);
        }
    }
}
