using System.Collections.Generic;
using NUnit.Framework;

namespace UniForge.Tests
{
    [TestFixture]
    public class SimpleJsonTests
    {
        #region Parse Tests

        [Test]
        public void Parse_EmptyObject_ReturnsEmptyDictionary()
        {
            var result = SimpleJson.Parse("{}");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_SimpleString_ReturnsCorrectValue()
        {
            var result = SimpleJson.Parse("{\"name\": \"test\"}");
            Assert.AreEqual("test", result["name"]);
        }

        [Test]
        public void Parse_Integer_ReturnsLongValue()
        {
            var result = SimpleJson.Parse("{\"count\": 42}");
            Assert.AreEqual(42L, result["count"]);
        }

        [Test]
        public void Parse_NegativeInteger_ReturnsCorrectValue()
        {
            var result = SimpleJson.Parse("{\"value\": -123}");
            Assert.AreEqual(-123L, result["value"]);
        }

        [Test]
        public void Parse_FloatingPoint_ReturnsDoubleValue()
        {
            var result = SimpleJson.Parse("{\"pi\": 3.14}");
            Assert.AreEqual(3.14, result["pi"]);
        }

        [Test]
        public void Parse_ScientificNotation_ReturnsCorrectValue()
        {
            var result = SimpleJson.Parse("{\"value\": 1.5e2}");
            Assert.AreEqual(150.0, result["value"]);
        }

        [Test]
        public void Parse_BooleanTrue_ReturnsTrue()
        {
            var result = SimpleJson.Parse("{\"flag\": true}");
            Assert.AreEqual(true, result["flag"]);
        }

        [Test]
        public void Parse_BooleanFalse_ReturnsFalse()
        {
            var result = SimpleJson.Parse("{\"flag\": false}");
            Assert.AreEqual(false, result["flag"]);
        }

        [Test]
        public void Parse_Null_ReturnsNull()
        {
            var result = SimpleJson.Parse("{\"value\": null}");
            Assert.IsNull(result["value"]);
        }

        [Test]
        public void Parse_Array_ReturnsList()
        {
            var result = SimpleJson.Parse("{\"items\": [1, 2, 3]}");
            var items = result["items"] as List<object>;
            Assert.IsNotNull(items);
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual(1L, items[0]);
            Assert.AreEqual(2L, items[1]);
            Assert.AreEqual(3L, items[2]);
        }

        [Test]
        public void Parse_NestedObject_ReturnsDictionary()
        {
            var result = SimpleJson.Parse("{\"user\": {\"name\": \"Alice\", \"age\": 30}}");
            var user = result["user"] as Dictionary<string, object>;
            Assert.IsNotNull(user);
            Assert.AreEqual("Alice", user["name"]);
            Assert.AreEqual(30L, user["age"]);
        }

        [Test]
        public void Parse_EscapedQuotes_UnescapesCorrectly()
        {
            var result = SimpleJson.Parse("{\"text\": \"say \\\"hello\\\"\"}");
            Assert.AreEqual("say \"hello\"", result["text"]);
        }

        [Test]
        public void Parse_EscapedNewline_UnescapesCorrectly()
        {
            var result = SimpleJson.Parse("{\"text\": \"line1\\nline2\"}");
            Assert.AreEqual("line1\nline2", result["text"]);
        }

        [Test]
        public void Parse_EscapedTab_UnescapesCorrectly()
        {
            var result = SimpleJson.Parse("{\"text\": \"col1\\tcol2\"}");
            Assert.AreEqual("col1\tcol2", result["text"]);
        }

        [Test]
        public void Parse_EscapedBackslash_UnescapesCorrectly()
        {
            var result = SimpleJson.Parse("{\"path\": \"C:\\\\Users\\\\test\"}");
            Assert.AreEqual("C:\\Users\\test", result["path"]);
        }

        [Test]
        public void Parse_MultipleProperties_ReturnsAllValues()
        {
            var result = SimpleJson.Parse("{\"a\": 1, \"b\": 2, \"c\": 3}");
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(1L, result["a"]);
            Assert.AreEqual(2L, result["b"]);
            Assert.AreEqual(3L, result["c"]);
        }

        [Test]
        public void Parse_EmptyString_ReturnsEmptyString()
        {
            var result = SimpleJson.Parse("{\"text\": \"\"}");
            Assert.AreEqual("", result["text"]);
        }

        [Test]
        public void Parse_EmptyArray_ReturnsEmptyList()
        {
            var result = SimpleJson.Parse("{\"items\": []}");
            var items = result["items"] as List<object>;
            Assert.IsNotNull(items);
            Assert.AreEqual(0, items.Count);
        }

        [Test]
        public void Parse_InvalidJson_ReturnsEmptyDictionary()
        {

            var result = SimpleJson.Parse("{invalid}");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        #region Infinite Loop Prevention Tests

        [Test]
        public void Parse_MissingCommaInObject_DoesNotHang()
        {
            // Missing comma between properties - should not infinite loop

            var result = SimpleJson.Parse("{\"a\":1\"b\":2}");
            Assert.IsNotNull(result);
        }

        [Test]
        public void Parse_MissingCommaInArray_DoesNotHang()
        {
            // Missing comma between elements - should not infinite loop

            var result = SimpleJson.Parse("{\"arr\":[1 2 3]}");
            Assert.IsNotNull(result);
        }

        [Test]
        public void Parse_UnquotedKey_DoesNotHang()
        {
            // Key without quotes - should not infinite loop

            var result = SimpleJson.Parse("{key:\"value\"}");
            Assert.IsNotNull(result);
        }

        [Test]
        public void Parse_IncompleteObject_DoesNotHang()
        {
            // Incomplete object - should not infinite loop
            var result = SimpleJson.Parse("{\"key\"");
            Assert.IsNotNull(result);
        }

        [Test]
        public void Parse_IncompleteArray_DoesNotHang()
        {
            // Incomplete array - should not infinite loop
            var result = SimpleJson.Parse("{\"arr\":[1,2,3");
            Assert.IsNotNull(result);
        }

        [Test]
        public void Parse_GarbageAfterValue_DoesNotHang()
        {
            // Garbage characters after valid value - should not infinite loop

            var result = SimpleJson.Parse("{\"key\":\"value\"xxx}");
            Assert.IsNotNull(result);
        }

        #endregion

        [Test]
        public void Parse_NullInput_ReturnsEmptyDictionary()
        {
            var result = SimpleJson.Parse(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_EmptyInput_ReturnsEmptyDictionary()
        {
            var result = SimpleJson.Parse("");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_WhitespaceInput_ReturnsEmptyDictionary()
        {
            var result = SimpleJson.Parse("   ");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Parse_ObjectWithWhitespace_ParsesCorrectly()
        {
            var result = SimpleJson.Parse("{ \"name\" : \"test\" , \"value\" : 123 }");
            Assert.AreEqual("test", result["name"]);
            Assert.AreEqual(123L, result["value"]);
        }

        #endregion

        #region Serialize Tests

        [Test]
        public void Serialize_SimpleObject_ReturnsValidJson()
        {
            var obj = new TestObject { name = "test", value = 42 };
            var json = SimpleJson.Serialize(obj);
            Assert.IsTrue(json.Contains("\"name\":\"test\"") || json.Contains("\"name\": \"test\""));
            Assert.IsTrue(json.Contains("\"value\":42") || json.Contains("\"value\": 42"));
        }

        [Test]
        public void Serialize_Null_ReturnsNullString()
        {
            var json = SimpleJson.Serialize(null);
            Assert.AreEqual("null", json);
        }

        [Test]
        public void Serialize_Dictionary_ReturnsValidJson()
        {
            var dict = new Dictionary<string, object>
            {
                { "key1", "value1" },
                { "key2", 123 }
            };
            var json = SimpleJson.Serialize(dict);
            Assert.IsTrue(json.Contains("\"key1\""));
            Assert.IsTrue(json.Contains("\"value1\""));
            Assert.IsTrue(json.Contains("\"key2\""));
            Assert.IsTrue(json.Contains("123"));
        }

        #endregion

        #region JsonBuilder Tests

        [Test]
        public void Object_Add_String_AddsCorrectly()
        {
            var json = SimpleJson.Object()
                .Add("name", "test")
                .ToString();
            Assert.IsTrue(json.Contains("\"name\":\"test\""));
        }

        [Test]
        public void Object_Add_Integer_AddsCorrectly()
        {
            var json = SimpleJson.Object()
                .Add("count", 42)
                .ToString();
            Assert.IsTrue(json.Contains("\"count\":42"));
        }

        [Test]
        public void Object_Add_Boolean_AddsCorrectly()
        {
            var json = SimpleJson.Object()
                .Add("flag", true)
                .ToString();
            Assert.IsTrue(json.Contains("\"flag\":true"));
        }

        [Test]
        public void Object_Add_Null_AddsNull()
        {
            var json = SimpleJson.Object()
                .Add("value", (string)null)
                .ToString();
            Assert.IsTrue(json.Contains("\"value\":null"));
        }

        [Test]
        public void Object_AddRaw_InsertsRawJson()
        {
            var json = SimpleJson.Object()
                .AddRaw("nested", "{\"inner\":1}")
                .ToString();
            Assert.IsTrue(json.Contains("\"nested\":{\"inner\":1}"));
        }

        [Test]
        public void Object_MultipleAdds_CreatesValidJson()
        {
            var json = SimpleJson.Object()
                .Add("name", "test")
                .Add("count", 5)
                .Add("active", true)
                .ToString();

            // Verify it's valid JSON by parsing it back
            var parsed = SimpleJson.Parse(json);
            Assert.AreEqual("test", parsed["name"]);
            Assert.AreEqual(5L, parsed["count"]);
            Assert.AreEqual(true, parsed["active"]);
        }

        #endregion

        #region Helper Classes

        private class TestObject
        {
            public string name;
            public int value;
        }

        #endregion
    }
}
