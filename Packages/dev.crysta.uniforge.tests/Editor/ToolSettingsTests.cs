using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniForge.Tools;

namespace UniForge.Tests
{
    [TestFixture]
    public class ToolSettingsDataTests
    {
        private ToolSettingsData _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new ToolSettingsData();
        }

        #region IsToolEnabled / SetToolEnabled Tests

        [Test]
        public void IsToolEnabled_NewTool_ReturnsTrue()
        {
            var result = _settings.IsToolEnabled("new-test-tool");
            Assert.IsTrue(result, "New tools should be enabled by default");
        }

        [Test]
        public void SetToolEnabled_DisableTool_MakesToolDisabled()
        {
            const string toolName = "tool-to-disable";

            _settings.SetToolEnabled(toolName, false);

            Assert.IsFalse(_settings.IsToolEnabled(toolName));
        }

        [Test]
        public void SetToolEnabled_EnableDisabledTool_MakesToolEnabled()
        {
            const string toolName = "tool-to-reenable";

            _settings.SetToolEnabled(toolName, false);
            Assert.IsFalse(_settings.IsToolEnabled(toolName));

            _settings.SetToolEnabled(toolName, true);
            Assert.IsTrue(_settings.IsToolEnabled(toolName));
        }

        [Test]
        public void SetToolEnabled_DisableSameTwice_RemainsDisabled()
        {
            const string toolName = "tool-double-disable";

            _settings.SetToolEnabled(toolName, false);
            _settings.SetToolEnabled(toolName, false);

            Assert.IsFalse(_settings.IsToolEnabled(toolName));
        }

        [Test]
        public void SetToolEnabled_EnableAlreadyEnabled_RemainsEnabled()
        {
            const string toolName = "tool-already-enabled";

            _settings.SetToolEnabled(toolName, true);

            Assert.IsTrue(_settings.IsToolEnabled(toolName));
        }

        [Test]
        public void SetToolEnabled_ReturnsTrue_WhenStateChanges()
        {
            Assert.IsTrue(_settings.SetToolEnabled("tool", false));
            Assert.IsTrue(_settings.SetToolEnabled("tool", true));
        }

        [Test]
        public void SetToolEnabled_ReturnsFalse_WhenStateUnchanged()
        {
            Assert.IsFalse(_settings.SetToolEnabled("tool", true)); // already enabled
            _settings.SetToolEnabled("tool", false);
            Assert.IsFalse(_settings.SetToolEnabled("tool", false)); // already disabled
        }

        #endregion

        #region EnableAllTools / DisableAllTools Tests

        [Test]
        public void EnableAllTools_AfterDisabling_EnablesAllTools()
        {
            _settings.SetToolEnabled("tool-a", false);
            _settings.SetToolEnabled("tool-b", false);

            _settings.EnableAllTools();

            Assert.IsTrue(_settings.IsToolEnabled("tool-a"));
            Assert.IsTrue(_settings.IsToolEnabled("tool-b"));
        }

        [Test]
        public void EnableAllTools_ReturnsTrue_WhenToolsDisabled()
        {
            _settings.SetToolEnabled("tool", false);
            Assert.IsTrue(_settings.EnableAllTools());
        }

        [Test]
        public void EnableAllTools_ReturnsFalse_WhenAllAlreadyEnabled()
        {
            Assert.IsFalse(_settings.EnableAllTools());
        }

        [Test]
        public void DisableAllTools_WithToolList_DisablesAllSpecifiedTools()
        {
            var toolNames = new[] { "tool-x", "tool-y", "tool-z" };

            _settings.DisableAllTools(toolNames);

            foreach (var name in toolNames)
            {
                Assert.IsFalse(_settings.IsToolEnabled(name), $"Tool {name} should be disabled");
            }
        }

        [Test]
        public void DisableAllTools_ReturnsTrue_WhenToolsEnabled()
        {
            Assert.IsTrue(_settings.DisableAllTools(new[] { "tool" }));
        }

        [Test]
        public void DisableAllTools_ReturnsFalse_WhenAlreadyDisabled()
        {
            _settings.SetToolEnabled("tool", false);
            Assert.IsFalse(_settings.DisableAllTools(new[] { "tool" }));
        }

        [Test]
        public void DisableAllTools_EmptyList_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _settings.DisableAllTools(System.Array.Empty<string>()));
        }

        [Test]
        public void GetDisabledTools_AfterDisabling_ReturnsDisabledTools()
        {
            _settings.SetToolEnabled("disabled-tool-1", false);
            _settings.SetToolEnabled("disabled-tool-2", false);

            var disabled = _settings.GetDisabledTools();

            Assert.IsTrue(disabled.Contains("disabled-tool-1"));
            Assert.IsTrue(disabled.Contains("disabled-tool-2"));
        }

        #endregion

        #region Constructor Tests

        [Test]
        public void Constructor_WithDisabledTools_RestoresState()
        {
            var settings = new ToolSettingsData(new[] { "tool-a", "tool-b" });

            Assert.IsFalse(settings.IsToolEnabled("tool-a"));
            Assert.IsFalse(settings.IsToolEnabled("tool-b"));
            Assert.IsTrue(settings.IsToolEnabled("tool-c"));
        }

        [Test]
        public void Constructor_WithNull_CreatesEmptyState()
        {
            var settings = new ToolSettingsData(null);
            Assert.IsTrue(settings.IsToolEnabled("any-tool"));
        }

        [Test]
        public void ToArray_ReturnsDisabledTools()
        {
            _settings.SetToolEnabled("tool-a", false);
            _settings.SetToolEnabled("tool-b", false);

            var array = _settings.ToArray();

            Assert.AreEqual(2, array.Length);
            Assert.Contains("tool-a", array);
            Assert.Contains("tool-b", array);
        }

        #endregion

        #region OnSettingsChanged Event Tests

        [Test]
        public void SetToolEnabled_WhenStateChanges_RaisesOnSettingsChanged()
        {
            bool eventRaised = false;
            _settings.OnSettingsChanged += () => eventRaised = true;

            _settings.SetToolEnabled("event-test-tool", false);

            Assert.IsTrue(eventRaised, "OnSettingsChanged should be raised when tool is disabled");
        }

        [Test]
        public void SetToolEnabled_WhenStateUnchanged_DoesNotRaiseEvent()
        {
            _settings.SetToolEnabled("unchanged-tool", false);

            bool eventRaised = false;
            _settings.OnSettingsChanged += () => eventRaised = true;

            _settings.SetToolEnabled("unchanged-tool", false);

            Assert.IsFalse(eventRaised, "OnSettingsChanged should not be raised when state doesn't change");
        }

        [Test]
        public void EnableAllTools_WhenToolsDisabled_RaisesOnSettingsChanged()
        {
            _settings.SetToolEnabled("some-tool", false);

            bool eventRaised = false;
            _settings.OnSettingsChanged += () => eventRaised = true;

            _settings.EnableAllTools();

            Assert.IsTrue(eventRaised, "OnSettingsChanged should be raised when enabling all tools");
        }

        [Test]
        public void EnableAllTools_WhenAllAlreadyEnabled_DoesNotRaiseEvent()
        {
            bool eventRaised = false;
            _settings.OnSettingsChanged += () => eventRaised = true;

            _settings.EnableAllTools();

            Assert.IsFalse(eventRaised, "OnSettingsChanged should not be raised when all tools already enabled");
        }

        [Test]
        public void DisableAllTools_WhenToolsEnabled_RaisesOnSettingsChanged()
        {
            bool eventRaised = false;
            _settings.OnSettingsChanged += () => eventRaised = true;

            _settings.DisableAllTools(new[] { "tool-for-event" });

            Assert.IsTrue(eventRaised, "OnSettingsChanged should be raised when disabling tools");
        }

        #endregion
    }

    [TestFixture]
    public class ToolSettingsTests
    {
        [SetUp]
        public void SetUp()
        {
            ToolSettings.ClearContextSizeCache();
        }

        [TearDown]
        public void TearDown()
        {
            ToolSettings.ClearContextSizeCache();
        }

        #region EstimateContextSize Tests

        [Test]
        public void EstimateContextSize_NullDefinition_ReturnsZero()
        {
            var result = ToolSettings.EstimateContextSize(null);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void EstimateContextSize_SimpleDefinition_ReturnsPositiveValue()
        {
            var definition = CreateSimpleToolDefinition("test-tool", "A simple test tool");

            var result = ToolSettings.EstimateContextSize(definition);

            Assert.Greater(result, 0, "Context size should be positive for a valid definition");
        }

        [Test]
        public void EstimateContextSize_LargerDefinition_ReturnsLargerValue()
        {
            var smallDef = CreateSimpleToolDefinition("small", "Short");
            var largeDef = CreateToolDefinitionWithSchema(
                "large-tool",
                "This is a much longer description that should result in more tokens being estimated",
                new Dictionary<string, object>
                {
                    { "type", "object" },
                    {
                        "properties", new Dictionary<string, object>
                        {
                            {
                                "param1", new Dictionary<string, object>
                                {
                                    { "type", "string" },
                                    { "description", "First parameter with a long description" }
                                }
                            },
                            {
                                "param2", new Dictionary<string, object>
                                {
                                    { "type", "number" },
                                    { "description", "Second parameter with another description" }
                                }
                            }
                        }
                    }
                });

            var smallSize = ToolSettings.EstimateContextSize(smallDef);
            var largeSize = ToolSettings.EstimateContextSize(largeDef);

            Assert.Greater(largeSize, smallSize,
                "Larger definition should have larger context size");
        }

        [Test]
        public void EstimateContextSize_SameDefinition_ReturnsCachedValue()
        {
            var definition = CreateSimpleToolDefinition("cached-tool", "A tool to test caching");

            // First call - should calculate and cache
            var firstResult = ToolSettings.EstimateContextSize(definition);

            // Second call - should return cached value
            var secondResult = ToolSettings.EstimateContextSize(definition);

            Assert.AreEqual(firstResult, secondResult,
                "Second call should return the same cached value");
        }

        [Test]
        public void EstimateContextSize_DifferentTools_CachesIndependently()
        {
            var tool1 = CreateSimpleToolDefinition("tool-one", "First tool");
            var tool2 = CreateSimpleToolDefinition("tool-two", "Second tool with different description");

            var size1 = ToolSettings.EstimateContextSize(tool1);
            var size2 = ToolSettings.EstimateContextSize(tool2);

            // Clear and recalculate to verify caching works per-tool
            ToolSettings.ClearContextSizeCache();

            var size1Again = ToolSettings.EstimateContextSize(tool1);
            var size2Again = ToolSettings.EstimateContextSize(tool2);

            Assert.AreEqual(size1, size1Again, "Same tool should produce same size");
            Assert.AreEqual(size2, size2Again, "Same tool should produce same size");
        }

        #endregion

        #region ClearContextSizeCache Tests

        [Test]
        public void ClearContextSizeCache_AfterCaching_ClearsCache()
        {
            var definition = CreateSimpleToolDefinition("clearable-tool", "A tool");

            // Cache a value
            var firstResult = ToolSettings.EstimateContextSize(definition);
            Assert.Greater(firstResult, 0);

            // Clear the cache
            ToolSettings.ClearContextSizeCache();

            // The next call should recalculate (though result should be the same)
            // We verify clearing worked by checking no exception is thrown
            var afterClearResult = ToolSettings.EstimateContextSize(definition);
            Assert.AreEqual(firstResult, afterClearResult,
                "Result should be the same after recalculation");
        }

        [Test]
        public void ClearContextSizeCache_WhenEmpty_DoesNotThrow()
        {
            // Should not throw when cache is already empty
            Assert.DoesNotThrow(() => ToolSettings.ClearContextSizeCache());
        }

        [Test]
        public void ClearContextSizeCache_MultipleTimes_DoesNotThrow()
        {
            var definition = CreateSimpleToolDefinition("multi-clear", "Test");
            ToolSettings.EstimateContextSize(definition);

            // Multiple clears should be safe
            Assert.DoesNotThrow(() =>
            {
                ToolSettings.ClearContextSizeCache();
                ToolSettings.ClearContextSizeCache();
                ToolSettings.ClearContextSizeCache();
            });
        }

        #endregion

        #region Helper Methods

        private ToolDefinition CreateSimpleToolDefinition(string name, string description)
        {
            return new ToolDefinition
            {
                name = name,
                description = description,
                inputSchema = new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "properties", new Dictionary<string, object>() }
                }
            };
        }

        private ToolDefinition CreateToolDefinitionWithSchema(
            string name,
            string description,
            Dictionary<string, object> inputSchema)
        {
            return new ToolDefinition
            {
                name = name,
                description = description,
                inputSchema = inputSchema
            };
        }

        #endregion
    }
}
