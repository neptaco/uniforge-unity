using System.Collections.Generic;
using NUnit.Framework;
using UniForge.Tools;

namespace UniForge.Tests
{
    [TestFixture]
    public class UniForgeServiceTests
    {
        private UniForgeService _service;

        [SetUp]
        public void SetUp()
        {
            _service = UniForgeService.instance;
        }

        #region ExtractArgsJson Tests

        [Test]
        public void ExtractArgsJson_WithValidArgs_ReturnsArgsObject()
        {
            var message = "{\"tool\":\"test\",\"args\":{\"key\":\"value\"}}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{\"key\":\"value\"}", result);
        }

        [Test]
        public void ExtractArgsJson_WithNestedArgs_ReturnsFullNestedObject()
        {
            var message = "{\"tool\":\"test\",\"args\":{\"outer\":{\"inner\":123}}}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{\"outer\":{\"inner\":123}}", result);
        }

        [Test]
        public void ExtractArgsJson_WithEmptyArgs_ReturnsEmptyObject()
        {
            var message = "{\"tool\":\"test\",\"args\":{}}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{}", result);
        }

        [Test]
        public void ExtractArgsJson_WithNoArgs_ReturnsEmptyObject()
        {
            var message = "{\"tool\":\"test\"}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{}", result);
        }

        [Test]
        public void ExtractArgsJson_WithWhitespaceBeforeArgs_ReturnsArgsObject()
        {
            var message = "{\"tool\":\"test\",\"args\":  {\"key\":\"value\"}}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{\"key\":\"value\"}", result);
        }

        [Test]
        public void ExtractArgsJson_WithMultipleNestedBraces_ReturnsCorrectObject()
        {
            var message = "{\"args\":{\"a\":{\"b\":{\"c\":1}}}}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{\"a\":{\"b\":{\"c\":1}}}", result);
        }

        [Test]
        public void ExtractArgsJson_WithArrayInArgs_ReturnsArgsWithArray()
        {
            var message = "{\"args\":{\"items\":[1,2,3]}}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{\"items\":[1,2,3]}", result);
        }

        [Test]
        public void ExtractArgsJson_WithBracesInString_ParsesCorrectly()
        {
            var message = "{\"args\":{\"text\":\"hello {world}\"}}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{\"text\":\"hello {world}\"}", result);
        }

        [Test]
        public void ExtractArgsJson_ArgsNotObject_ReturnsEmptyObject()
        {
            var message = "{\"args\":\"not an object\"}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(message);
            Assert.AreEqual("{}", result);
        }

        [Test]
        public void ExtractArgsJson_EmptyMessage_ReturnsEmptyObject()
        {
            var result = UniForgeProtocolMessages.ExtractArgsJson("");
            Assert.AreEqual("{}", result);
        }

        #endregion

        #region ExtractParamsJson Tests

        [Test]
        public void ExtractParamsJson_WithValidParams_ReturnsParamsObject()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"daemon.executeTool\",\"params\":{\"tool\":\"test\",\"args\":{}}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(message);
            Assert.AreEqual("{\"tool\":\"test\",\"args\":{}}", result);
        }

        [Test]
        public void ExtractParamsJson_WithNoParams_ReturnsEmptyObject()
        {
            var message = "{\"jsonrpc\":\"2.0\",\"method\":\"daemon.ping\"}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(message);
            Assert.AreEqual("{}", result);
        }

        [Test]
        public void ExtractParamsJson_WithNestedParams_ReturnsFullObject()
        {
            var message = "{\"params\":{\"tool\":\"test\",\"args\":{\"key\":\"value\"}}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(message);
            Assert.AreEqual("{\"tool\":\"test\",\"args\":{\"key\":\"value\"}}", result);
        }

        #endregion

        #region ExtractStringField Tests

        [Test]
        public void ExtractStringField_WithValidField_ReturnsValue()
        {
            var json = "{\"tool\":\"hierarchy\",\"args\":{}}";
            var result = UniForgeProtocolMessages.ExtractStringField(json, "tool");
            Assert.AreEqual("hierarchy", result);
        }

        [Test]
        public void ExtractStringField_WithMissingField_ReturnsNull()
        {
            var json = "{\"args\":{}}";
            var result = UniForgeProtocolMessages.ExtractStringField(json, "tool");
            Assert.IsNull(result);
        }

        #endregion

        #region Builder Tests

        [Test]
        public void BuildUnityRegisterRequest_WithPendingRequestIds_IncludesPendingIds()
        {
            var request = UniForgeProtocolMessages.BuildUnityRegisterRequest(
                "req-1",
                "project-1",
                "Project One",
                "/repo/root",
                new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>
                    {
                        { "name", "compile" },
                        { "description", "Compile scripts" }
                    }
                },
                new List<string> { "pending-1", "pending-2" });

            StringAssert.Contains("\"method\":\"unity.register\"", request);
            StringAssert.Contains("\"gitRoot\":\"/repo/root\"", request);
            StringAssert.Contains("\"pendingRequestIds\":[\"pending-1\",\"pending-2\"]", request);
        }

        [Test]
        public void BuildUnityRegisterRequest_WithoutPendingRequestIds_OmitsPendingIds()
        {
            var request = UniForgeProtocolMessages.BuildUnityRegisterRequest(
                "req-1",
                "project-1",
                "Project One",
                null,
                new List<Dictionary<string, object>>(),
                new List<string>());

            StringAssert.DoesNotContain("\"pendingRequestIds\"", request);
            StringAssert.DoesNotContain("\"gitRoot\"", request);
            StringAssert.DoesNotContain("\"consoleLogPath\"", request);
            StringAssert.DoesNotContain("\"packageVersion\"", request);
        }

        [Test]
        public void BuildUnityRegisterRequest_WithConsoleLogPath_IncludesConsoleLogPath()
        {
            var request = UniForgeProtocolMessages.BuildUnityRegisterRequest(
                "req-1",
                "project-1",
                "Project One",
                null,
                new List<Dictionary<string, object>>(),
                new List<string>(),
                "/Users/me/Library/Logs/Unity/Editor.log");

            StringAssert.Contains(
                "\"consoleLogPath\":\"/Users/me/Library/Logs/Unity/Editor.log\"",
                request);
        }

        [Test]
        public void BuildUnityRegisterRequest_WithPackageVersion_IncludesPackageVersion()
        {
            var request = UniForgeProtocolMessages.BuildUnityRegisterRequest(
                "req-1",
                "project-1",
                "Project One",
                null,
                new List<Dictionary<string, object>>(),
                new List<string>(),
                packageVersion: "0.11.0");

            StringAssert.Contains("\"packageVersion\":\"0.11.0\"", request);
            StringAssert.DoesNotContain("\"consoleLogPath\"", request);
        }

        [Test]
        public void BuildToolResponse_WithPending_SetsPendingFlag()
        {
            var response = UniForgeProtocolMessages.BuildToolResponse(
                "req-1",
                true,
                new Dictionary<string, object> { { "status", "ok" } },
                null,
                true);

            StringAssert.Contains("\"pending\":true", response);
            StringAssert.Contains("\"status\":\"ok\"", response);
        }

        [Test]
        public void BuildToolResponse_EscapesErrorMessage()
        {
            var response = UniForgeProtocolMessages.BuildToolResponse(
                "req-1",
                false,
                new Dictionary<string, object> { { "status", "failed" } },
                "line1\n\"quoted\"");

            var parsed = SimpleJson.Parse(response) as Dictionary<string, object>;
            Assert.IsNotNull(parsed);

            var result = parsed["result"] as Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual("line1\n\"quoted\"", result["error"]);
        }

        #endregion

        #region ToolListsEqual Tests

        [Test]
        public void ToolListsEqual_BothNull_ReturnsTrue()
        {
            var result = UniForgeToolDefinitionComparer.AreEqual(null, null);
            Assert.IsTrue(result);
        }

        [Test]
        public void ToolListsEqual_OneNull_ReturnsFalse()
        {
            var list = new List<Dictionary<string, object>>();
            Assert.IsFalse(UniForgeToolDefinitionComparer.AreEqual(list, null));
            Assert.IsFalse(UniForgeToolDefinitionComparer.AreEqual(null, list));
        }

        [Test]
        public void ToolListsEqual_BothEmpty_ReturnsTrue()
        {
            var a = new List<Dictionary<string, object>>();
            var b = new List<Dictionary<string, object>>();
            Assert.IsTrue(UniForgeToolDefinitionComparer.AreEqual(a, b));
        }

        [Test]
        public void ToolListsEqual_DifferentCounts_ReturnsFalse()
        {
            var a = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "name", "tool1" } }
            };
            var b = new List<Dictionary<string, object>>();
            Assert.IsFalse(UniForgeToolDefinitionComparer.AreEqual(a, b));
        }

        [Test]
        public void ToolListsEqual_SameToolNames_ReturnsTrue()
        {
            var a = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "name", "tool1" } },
                new Dictionary<string, object> { { "name", "tool2" } }
            };
            var b = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "name", "tool1" } },
                new Dictionary<string, object> { { "name", "tool2" } }
            };
            Assert.IsTrue(UniForgeToolDefinitionComparer.AreEqual(a, b));
        }

        [Test]
        public void ToolListsEqual_SameToolNamesDifferentOrder_ReturnsTrue()
        {
            var a = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "name", "tool1" } },
                new Dictionary<string, object> { { "name", "tool2" } }
            };
            var b = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "name", "tool2" } },
                new Dictionary<string, object> { { "name", "tool1" } }
            };
            Assert.IsTrue(UniForgeToolDefinitionComparer.AreEqual(a, b));
        }

        [Test]
        public void ToolListsEqual_DifferentToolNames_ReturnsFalse()
        {
            var a = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "name", "tool1" } }
            };
            var b = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "name", "tool2" } }
            };
            Assert.IsFalse(UniForgeToolDefinitionComparer.AreEqual(a, b));
        }

        [Test]
        public void ToolListsEqual_MissingNameKey_HandlesGracefully()
        {
            var a = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "other", "value" } }
            };
            var b = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { { "other", "value" } }
            };
            // Both have no "name" key, so both sets are empty - should be equal
            Assert.IsTrue(UniForgeToolDefinitionComparer.AreEqual(a, b));
        }

        [Test]
        public void ToolListsEqual_DifferentDescription_ReturnsFalse()
        {
            var a = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "name", "tool1" },
                    { "description", "old description" }
                }
            };
            var b = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "name", "tool1" },
                    { "description", "new description" }
                }
            };
            Assert.IsFalse(UniForgeToolDefinitionComparer.AreEqual(a, b));
        }

        [Test]
        public void ToolListsEqual_SameSchemaDifferentDictionaryOrder_ReturnsTrue()
        {
            var a = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "name", "tool1" },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "first", new Dictionary<string, object> { { "type", "string" } } },
                                    { "second", new Dictionary<string, object> { { "type", "number" } } }
                                }
                            }
                        }
                    }
                }
            };
            var b = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "properties", new Dictionary<string, object>
                                {
                                    { "second", new Dictionary<string, object> { { "type", "number" } } },
                                    { "first", new Dictionary<string, object> { { "type", "string" } } }
                                }
                            },
                            { "type", "object" }
                        }
                    },
                    { "name", "tool1" }
                }
            };

            Assert.IsTrue(UniForgeToolDefinitionComparer.AreEqual(a, b));
        }

        #endregion

        #region Initial State Tests

        [Test]
        public void ToolRegistry_AfterInitialization_IsNotNull()
        {
            // The service initializes on editor load
            Assert.IsNotNull(_service.ToolRegistry);
        }

        [Test]
        public void ToolRegistry_AfterInitialization_HasRegisteredTools()
        {
            Assert.Greater(_service.ToolRegistry.Count, 0);
        }

        [Test]
        public void ToolRegistry_DoesNotRegisterExampleTools()
        {
            var registry = new ToolRegistry();
            registry.RegisterAllToolHandlers();

            Assert.IsFalse(registry.HasTool("example-echo"));
            Assert.IsFalse(registry.HasTool("example-log"));
        }

        [Test]
        public void ToolRegistry_RegistersSetSceneViewAndFrameObject()
        {
            var registry = new ToolRegistry();
            registry.RegisterAllToolHandlers();

            Assert.IsTrue(registry.HasTool("set-scene-view"));
            Assert.IsTrue(registry.HasTool("frame-object"));
        }

        #endregion
    }
}
