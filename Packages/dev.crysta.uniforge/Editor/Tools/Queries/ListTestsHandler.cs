using System.Collections.Generic;
using System.Text.RegularExpressions;
using UniForge.TestRunner;

namespace UniForge.Tools.Queries
{
    /// <summary>
    /// List available tests from Unity Test Runner
    /// </summary>
    [Tool("list-tests",
        Description = "List available tests from Unity Test Runner",
        Title = "List Tests",
        Category = ToolCategory.Test,
        Kind = ToolKind.Query,
        Idempotent = true)]
    public partial class ListTestsHandler : QueryHandler
    {
        public class Args
        {
            [ToolParameter("Test mode to list", Enum = "EditMode,PlayMode,Both", Default = "Both")]
            public string mode;

            [ToolParameter("Filter by test name pattern (regex)")]
            public string name_filter;

            [ToolParameter("Filter by assembly name")]
            public string assembly;

            [ToolParameter("Filter by category")]
            public string category;

            [ToolParameter("Maximum number of tests to return", Default = 1000)]
            public int limit;
        }

        public class Output
        {
            public List<TestInfo> tests;
            public int total_count;
            public int edit_mode_count;
            public int play_mode_count;
            public bool available;
            public string message;
        }

        private ToolDefinition _definition;

        public override ToolDefinition Definition
        {
            get
            {
                _definition ??= ToolDefinitionBuilder.FromHandler<ListTestsHandler>();
                return _definition;
            }
        }

        protected internal override ToolResult Execute(string argsJson)
        {
            if (!TestRunnerService.IsTestFrameworkAvailable)
            {
                return ToolResult.Ok(new Output
                {
                    tests = new List<TestInfo>(),
                    total_count = 0,
                    available = false,
                    message = "Test Framework package is not installed"
                });
            }

            var args = new ToolArgsParser(argsJson);
            var mode = args.GetString("mode", "Both");
            var nameFilter = args.GetString("name_filter", null);
            var assemblyFilter = args.GetString("assembly", null);
            var categoryFilter = args.GetString("category", null);
            var limit = args.GetInt("limit", 1000);

            // Prepare name filter regex
            Regex nameRegex = null;
            if (!string.IsNullOrEmpty(nameFilter))
            {
                try
                {
                    nameRegex = new Regex(nameFilter, RegexOptions.IgnoreCase);
                }
                catch
                {
                    nameRegex = new Regex(Regex.Escape(nameFilter), RegexOptions.IgnoreCase);
                }
            }

            var output = new Output
            {
                tests = new List<TestInfo>(),
                available = true
            };

            // Get tests from cache
            var service = TestRunnerService.instance;

            if (!service.IsCacheReady)
            {
                // Cache not ready - trigger refresh and return message
                service.RefreshCache();
                return ToolResult.Ok(new Output
                {
                    tests = new List<TestInfo>(),
                    total_count = 0,
                    available = true,
                    message = "Test cache is being initialized. Please try again in a moment."
                });
            }

            var allTests = service.GetTestsCached(mode) ?? new List<TestInfo>();

            // Apply filters
            foreach (var test in allTests)
            {
                // Name filter
                if (nameRegex != null && !nameRegex.IsMatch(test.fullName))
                {
                    continue;
                }

                // Assembly filter
                if (!string.IsNullOrEmpty(assemblyFilter))
                {
                    if (string.IsNullOrEmpty(test.assembly) ||
                        !test.assembly.Contains(assemblyFilter, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                // Category filter
                if (!string.IsNullOrEmpty(categoryFilter))
                {
                    bool hasCategory = false;
                    if (test.categories != null)
                    {
                        foreach (var cat in test.categories)
                        {
                            if (cat.Equals(categoryFilter, System.StringComparison.OrdinalIgnoreCase))
                            {
                                hasCategory = true;
                                break;
                            }
                        }
                    }
                    if (!hasCategory)
                    {
                        continue;
                    }
                }

                // Count by mode
                if (test.mode == "EditMode")
                {
                    output.edit_mode_count++;
                }
                else if (test.mode == "PlayMode")
                {
                    output.play_mode_count++;
                }

                // Add to results (respecting limit)
                if (output.tests.Count < limit)
                {
                    output.tests.Add(test);
                }
            }

            output.total_count = output.edit_mode_count + output.play_mode_count;

            return ToolResult.Ok(output);
        }
    }
}
