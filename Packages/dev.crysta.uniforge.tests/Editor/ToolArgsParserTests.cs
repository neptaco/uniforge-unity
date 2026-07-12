using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UniForge.Tools;

namespace UniForge.Tests
{
    [TestFixture]
    public class ToolArgsParserTests
    {
        #region Constructor / Parse Error Tests

        [Test]
        public void Constructor_NullJson_DoesNotThrow()
        {
            var parser = new ToolArgsParser(null);
            Assert.IsFalse(parser.HasParseError);
        }

        [Test]
        public void Constructor_EmptyJson_DoesNotThrow()
        {
            var parser = new ToolArgsParser("");
            Assert.IsFalse(parser.HasParseError);
        }

        [Test]
        public void Constructor_EmptyObject_DoesNotThrow()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsFalse(parser.HasParseError);
        }

        [Test]
        public void Constructor_MalformedJson_DoesNotThrow()
        {
            // SimpleJson のゆるいパーサーは不正な JSON でもクラッシュしない
            Assert.DoesNotThrow(() => new ToolArgsParser("{invalid}"));
        }

        [Test]
        public void Constructor_MalformedJson_StillUsable()
        {
            var parser = new ToolArgsParser("{invalid}");
            Assert.AreEqual("default", parser.GetString("key", "default"));
            Assert.AreEqual(0, parser.GetInt("key"));
            Assert.IsFalse(parser.HasKey("key"));
        }

        [Test]
        public void HasParseError_ValidJson_IsFalse()
        {
            var parser = new ToolArgsParser("{\"key\": \"value\"}");
            Assert.IsFalse(parser.HasParseError);
        }

        [Test]
        public void HasParseError_EmptyJson_IsFalse()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsFalse(parser.HasParseError);
        }

        #endregion

        #region GetString Tests

        [Test]
        public void GetString_ExistingKey_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"name\": \"test\"}");
            Assert.AreEqual("test", parser.GetString("name"));
        }

        [Test]
        public void GetString_NonExistingKey_ReturnsDefault()
        {
            var parser = new ToolArgsParser("{\"name\": \"test\"}");
            Assert.AreEqual("default", parser.GetString("missing", "default"));
        }

        [Test]
        public void GetString_NonExistingKey_WithoutDefault_ReturnsNull()
        {
            var parser = new ToolArgsParser("{\"name\": \"test\"}");
            Assert.IsNull(parser.GetString("missing"));
        }

        [Test]
        public void GetString_EmptyString_ReturnsEmptyString()
        {
            var parser = new ToolArgsParser("{\"name\": \"\"}");
            Assert.AreEqual("", parser.GetString("name"));
        }

        [Test]
        public void GetString_IntegerValue_ReturnsNull()
        {
            var parser = new ToolArgsParser("{\"count\": 42}");
            Assert.IsNull(parser.GetString("count"));
        }

        #endregion

        #region GetInt Tests

        [Test]
        public void GetInt_ExistingKey_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"count\": 42}");
            Assert.AreEqual(42, parser.GetInt("count"));
        }

        [Test]
        public void GetInt_NonExistingKey_ReturnsDefault()
        {
            var parser = new ToolArgsParser("{\"count\": 42}");
            Assert.AreEqual(100, parser.GetInt("missing", 100));
        }

        [Test]
        public void GetInt_NonExistingKey_WithoutDefault_ReturnsZero()
        {
            var parser = new ToolArgsParser("{\"count\": 42}");
            Assert.AreEqual(0, parser.GetInt("missing"));
        }

        [Test]
        public void GetInt_NegativeValue_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"value\": -123}");
            Assert.AreEqual(-123, parser.GetInt("value"));
        }

        [Test]
        public void GetInt_Zero_ReturnsZero()
        {
            var parser = new ToolArgsParser("{\"value\": 0}");
            Assert.AreEqual(0, parser.GetInt("value"));
        }

        [Test]
        public void GetInt_StringValue_ReturnsDefault()
        {
            var parser = new ToolArgsParser("{\"value\": \"not a number\"}");
            Assert.AreEqual(0, parser.GetInt("value"));
        }

        #endregion

        #region GetLong Tests

        [Test]
        public void GetLong_ExistingKey_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"timestamp\": 1234567890123}");
            Assert.AreEqual(1234567890123L, parser.GetLong("timestamp"));
        }

        [Test]
        public void GetLong_NonExistingKey_ReturnsDefault()
        {
            var parser = new ToolArgsParser("{}");
            Assert.AreEqual(999L, parser.GetLong("missing", 999L));
        }

        [Test]
        public void GetLong_SmallInteger_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"value\": 42}");
            Assert.AreEqual(42L, parser.GetLong("value"));
        }

        #endregion

        #region GetBool Tests

        [Test]
        public void GetBool_True_ReturnsTrue()
        {
            var parser = new ToolArgsParser("{\"flag\": true}");
            Assert.IsTrue(parser.GetBool("flag"));
        }

        [Test]
        public void GetBool_False_ReturnsFalse()
        {
            var parser = new ToolArgsParser("{\"flag\": false}");
            Assert.IsFalse(parser.GetBool("flag"));
        }

        [Test]
        public void GetBool_NonExistingKey_ReturnsDefault()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsTrue(parser.GetBool("missing", true));
            Assert.IsFalse(parser.GetBool("missing", false));
        }

        [Test]
        public void GetBool_NonExistingKey_WithoutDefault_ReturnsFalse()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsFalse(parser.GetBool("missing"));
        }

        [Test]
        public void GetBool_StringTrue_ParsesAsBoolean()
        {
            var parser = new ToolArgsParser("{\"flag\": \"true\"}");
            Assert.IsTrue(parser.GetBool("flag"));
        }

        [Test]
        public void GetBool_IntegerOne_ReturnsDefault()
        {
            var parser = new ToolArgsParser("{\"flag\": 1}");
            Assert.IsFalse(parser.GetBool("flag"));
        }

        #endregion

        #region GetFloat Tests

        [Test]
        public void GetFloat_DoubleValue_ReturnsFloat()
        {
            var parser = new ToolArgsParser("{\"value\": 3.14}");
            Assert.AreEqual(3.14f, parser.GetFloat("value"), 0.001f);
        }

        [Test]
        public void GetFloat_IntegerValue_ReturnsFloat()
        {
            var parser = new ToolArgsParser("{\"value\": 42}");
            Assert.AreEqual(42f, parser.GetFloat("value"), 0.001f);
        }

        [Test]
        public void GetFloat_NonExistingKey_ReturnsDefault()
        {
            var parser = new ToolArgsParser("{}");
            Assert.AreEqual(1.5f, parser.GetFloat("missing", 1.5f), 0.001f);
        }

        #endregion

        #region HasKey Tests

        [Test]
        public void HasKey_ExistingKey_ReturnsTrue()
        {
            var parser = new ToolArgsParser("{\"name\": \"test\"}");
            Assert.IsTrue(parser.HasKey("name"));
        }

        [Test]
        public void HasKey_NonExistingKey_ReturnsFalse()
        {
            var parser = new ToolArgsParser("{\"name\": \"test\"}");
            Assert.IsFalse(parser.HasKey("missing"));
        }

        [Test]
        public void HasKey_NullValue_ReturnsTrue()
        {
            var parser = new ToolArgsParser("{\"value\": null}");
            Assert.IsTrue(parser.HasKey("value"));
        }

        #endregion

        #region GetNullableInt Tests

        [Test]
        public void GetNullableInt_ExistingKey_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"value\": 42}");
            Assert.AreEqual(42, parser.GetNullableInt("value"));
        }

        [Test]
        public void GetNullableInt_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsNull(parser.GetNullableInt("missing"));
        }

        [Test]
        public void GetNullableInt_NullValue_ReturnsNull()
        {
            var parser = new ToolArgsParser("{\"value\": null}");
            Assert.IsNull(parser.GetNullableInt("value"));
        }

        #endregion

        #region GetNullableBool Tests

        [Test]
        public void GetNullableBool_ExistingKey_ReturnsValue()
        {
            var parser = new ToolArgsParser("{\"flag\": true}");
            Assert.AreEqual(true, parser.GetNullableBool("flag"));
        }

        [Test]
        public void GetNullableBool_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsNull(parser.GetNullableBool("missing"));
        }

        #endregion

        #region GetFloatArray Tests

        [Test]
        public void GetFloatArray_ValidArray_ReturnsFloats()
        {
            var parser = new ToolArgsParser("{\"values\": [1.0, 2.5, 3.0]}");
            var result = parser.GetFloatArray("values");
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(1.0f, result[0], 0.001f);
            Assert.AreEqual(2.5f, result[1], 0.001f);
            Assert.AreEqual(3.0f, result[2], 0.001f);
        }

        [Test]
        public void GetFloatArray_IntegerArray_ReturnsFloats()
        {
            var parser = new ToolArgsParser("{\"values\": [1, 2, 3]}");
            var result = parser.GetFloatArray("values");
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(1f, result[0], 0.001f);
        }

        [Test]
        public void GetFloatArray_EmptyArray_ReturnsEmpty()
        {
            var parser = new ToolArgsParser("{\"values\": []}");
            var result = parser.GetFloatArray("values");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void GetFloatArray_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsNull(parser.GetFloatArray("missing"));
        }

        [Test]
        public void GetFloatArray_InvalidElement_ReturnsNull()
        {
            var parser = new ToolArgsParser("{\"values\": [1.0, \"bad\", 3.0]}");
            var result = parser.GetFloatArray("values");
            Assert.IsNull(result);
        }

        #endregion

        #region GetStringArray Tests

        [Test]
        public void GetStringArray_ValidArray_ReturnsStrings()
        {
            var parser = new ToolArgsParser("{\"items\": [\"a\", \"b\", \"c\"]}");
            var result = parser.GetStringArray("items");
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("a", result[0]);
            Assert.AreEqual("b", result[1]);
            Assert.AreEqual("c", result[2]);
        }

        [Test]
        public void GetStringArray_MixedTypes_ConvertsToString()
        {
            var parser = new ToolArgsParser("{\"items\": [\"text\", 42, true]}");
            var result = parser.GetStringArray("items");
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("text", result[0]);
            Assert.AreEqual("42", result[1]);
            Assert.AreEqual("True", result[2]);
        }

        [Test]
        public void GetStringArray_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsNull(parser.GetStringArray("missing"));
        }

        #endregion

        #region GetDictionary Tests

        [Test]
        public void GetDictionary_ExistingKey_ReturnsDictionary()
        {
            var parser = new ToolArgsParser("{\"data\": {\"key\": \"value\"}}");
            var result = parser.GetDictionary("data");
            Assert.IsNotNull(result);
            Assert.AreEqual("value", result["key"]);
        }

        [Test]
        public void GetDictionary_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsNull(parser.GetDictionary("missing"));
        }

        [Test]
        public void GetDictionary_NonObjectValue_ReturnsNull()
        {
            var parser = new ToolArgsParser("{\"data\": \"string\"}");
            Assert.IsNull(parser.GetDictionary("data"));
        }

        #endregion

        #region GetObjectArray Tests

        private class SimpleOp
        {
            public string name;
            public int? count;
        }

        [Test]
        public void GetObjectArray_ValidArray_ReturnsMappedObjects()
        {
            var parser = new ToolArgsParser("{\"ops\": [{\"name\": \"a\", \"count\": 1}, {\"name\": \"b\", \"count\": 2}]}");
            var result = parser.GetObjectArray<SimpleOp>("ops");
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("a", result[0].name);
            Assert.AreEqual(1, result[0].count);
            Assert.AreEqual("b", result[1].name);
            Assert.AreEqual(2, result[1].count);
        }

        [Test]
        public void GetObjectArray_EmptyArray_ReturnsEmpty()
        {
            var parser = new ToolArgsParser("{\"ops\": []}");
            var result = parser.GetObjectArray<SimpleOp>("ops");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void GetObjectArray_NonExistingKey_ReturnsNull()
        {
            var parser = new ToolArgsParser("{}");
            Assert.IsNull(parser.GetObjectArray<SimpleOp>("missing"));
        }

        [Test]
        public void GetObjectArray_MixedElements_SkipsNonObjects()
        {
            // 文字列が混ざっている場合、辞書要素のみをパースしてスキップ
            var parser = new ToolArgsParser("{\"ops\": [{\"name\": \"a\"}, \"invalid\", {\"name\": \"c\"}]}");
            var result = parser.GetObjectArray<SimpleOp>("ops");
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("a", result[0].name);
            Assert.AreEqual("c", result[1].name);
        }

        [Test]
        public void GetObjectArray_AllNonObjects_ReturnsEmpty()
        {
            var parser = new ToolArgsParser("{\"ops\": [\"a\", \"b\", \"c\"]}");
            var result = parser.GetObjectArray<SimpleOp>("ops");
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void GetObjectArray_PartialFields_SetsAvailableOnly()
        {
            var parser = new ToolArgsParser("{\"ops\": [{\"name\": \"test\"}]}");
            var result = parser.GetObjectArray<SimpleOp>("ops");
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("test", result[0].name);
            Assert.IsNull(result[0].count);
        }

        [Test]
        public void GetObjectArray_SnakeCaseKeys_MapsToFields()
        {
            // snake_case キーが camelCase フィールドにマッピングされることを確認
            var parser = new ToolArgsParser("{\"ops\": [{\"name\": \"test\"}]}");
            var result = parser.GetObjectArray<SimpleOp>("ops");
            Assert.IsNotNull(result);
            Assert.AreEqual("test", result[0].name);
        }

        #endregion

        #region GetObjectArray - Nested Components Tests

        private class ComponentSpec
        {
            public string type;
            public Dictionary<string, object> properties;
        }

        private class CreateOp
        {
            public string name;
            public ComponentSpec[] components;
        }

        [Test]
        public void GetObjectArray_NestedComponents_ParsesCorrectly()
        {
            var json = "{\"objects\": [{\"name\": \"Obj\", \"components\": [{\"type\": \"Image\", \"properties\": {\"color\": [1,0,0,1]}}]}]}";
            var parser = new ToolArgsParser(json);
            var result = parser.GetObjectArray<CreateOp>("objects");
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("Obj", result[0].name);
            Assert.IsNotNull(result[0].components);
            Assert.AreEqual(1, result[0].components.Length);
            Assert.AreEqual("Image", result[0].components[0].type);
            Assert.IsNotNull(result[0].components[0].properties);
            Assert.IsTrue(result[0].components[0].properties.ContainsKey("color"));
        }

        [Test]
        public void GetObjectArray_NestedStringArray_SkipsInvalidComponents()
        {
            // components に文字列配列を渡した場合、null要素なく空配列になること
            LogAssert.Expect(LogType.Warning, new Regex(@"\[ToolArgsParser\]"));
            var json = "{\"objects\": [{\"name\": \"Obj\", \"components\": [\"Image\"]}]}";
            var parser = new ToolArgsParser(json);
            var result = parser.GetObjectArray<CreateOp>("objects");
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("Obj", result[0].name);
            // components は空配列（文字列要素がスキップされる）
            Assert.IsNotNull(result[0].components);
            Assert.AreEqual(0, result[0].components.Length);
        }

        [Test]
        public void GetObjectArray_NestedEmptyComponents_ReturnsEmptyArray()
        {
            var json = "{\"objects\": [{\"name\": \"Obj\", \"components\": []}]}";
            var parser = new ToolArgsParser(json);
            var result = parser.GetObjectArray<CreateOp>("objects");
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
            Assert.IsNotNull(result[0].components);
            Assert.AreEqual(0, result[0].components.Length);
        }

        #endregion

        #region Type Conversion Error Tests

        private class TypedOp
        {
            public int count;
            public float ratio;
            public bool enabled;
        }

        [Test]
        public void GetObjectArray_InvalidTypeConversion_DoesNotThrow()
        {
            // count に文字列を渡してもクラッシュしない
            LogAssert.Expect(LogType.Warning, new Regex(@"\[ToolArgsParser\]"));
            var parser = new ToolArgsParser("{\"ops\": [{\"count\": \"not_a_number\"}]}");
            Assert.DoesNotThrow(() => parser.GetObjectArray<TypedOp>("ops"));
        }

        [Test]
        public void GetObjectArray_InvalidTypeConversion_KeepsDefaultValue()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"\[ToolArgsParser\]"));
            var parser = new ToolArgsParser("{\"ops\": [{\"count\": \"not_a_number\"}]}");
            var result = parser.GetObjectArray<TypedOp>("ops");
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(0, result[0].count); // default int value
        }

        #endregion

        #region ToSnakeCase Tests (via GetObjectArray key matching)

        private class SnakeCaseOp
        {
            public string componentType;
            public int instanceId;
        }

        [Test]
        public void GetObjectArray_SnakeCase_ComponentType_Maps()
        {
            var parser = new ToolArgsParser("{\"ops\": [{\"component_type\": \"Image\"}]}");
            var result = parser.GetObjectArray<SnakeCaseOp>("ops");
            Assert.AreEqual("Image", result[0].componentType);
        }

        [Test]
        public void GetObjectArray_CamelCase_ComponentType_Maps()
        {
            var parser = new ToolArgsParser("{\"ops\": [{\"componentType\": \"Image\"}]}");
            var result = parser.GetObjectArray<SnakeCaseOp>("ops");
            Assert.AreEqual("Image", result[0].componentType);
        }

        [Test]
        public void GetObjectArray_SnakeCase_InstanceId_Maps()
        {
            var parser = new ToolArgsParser("{\"ops\": [{\"instance_id\": 123}]}");
            var result = parser.GetObjectArray<SnakeCaseOp>("ops");
            Assert.AreEqual(123, result[0].instanceId);
        }

        #endregion

        #region Multiple Keys / Complex JSON Tests

        [Test]
        public void MultipleKeys_AllAccessible()
        {
            var parser = new ToolArgsParser("{\"str\": \"hello\", \"num\": 42, \"flag\": true}");
            Assert.AreEqual("hello", parser.GetString("str"));
            Assert.AreEqual(42, parser.GetInt("num"));
            Assert.IsTrue(parser.GetBool("flag"));
        }

        [Test]
        public void NestedObject_TopLevelKeyAccessible()
        {
            var parser = new ToolArgsParser("{\"outer\": {\"inner\": \"value\"}}");
            Assert.IsTrue(parser.HasKey("outer"));
            var dict = parser.GetDictionary("outer");
            Assert.IsNotNull(dict);
            Assert.AreEqual("value", dict["inner"]);
        }

        #endregion
    }
}
