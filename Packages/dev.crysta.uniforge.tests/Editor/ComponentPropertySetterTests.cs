using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UniForge.Tools;

namespace UniForge.Tests
{
    /// <summary>
    /// ComponentPropertySetter のユニットテスト
    /// </summary>
    [TestFixture]
    public class ComponentPropertySetterTests
    {
        private enum TestEnum
        {
            First,
            Second,
            Third
        }

        private sealed class PropertySetterTestComponent : MonoBehaviour
        {
            public TestEnum enumField;
            public float floatField;
            public int intField;
            public GameObject objectField;
        }

        private GameObject _testObject;
        private PropertySetterTestComponent _component;

        [SetUp]
        public void SetUp()
        {
            _testObject = new GameObject("PropertySetterTestObject");
            _component = _testObject.AddComponent<PropertySetterTestComponent>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testObject != null)
            {
                Object.DestroyImmediate(_testObject);
            }
        }

        private T GetSerializedValue<T>(string propName, System.Func<SerializedProperty, T> getter)
        {
            using (var so = new SerializedObject(_component))
            {
                return getter(so.FindProperty(propName));
            }
        }

        #region Culture Invariance Tests

        [Test]
        public void SetProperties_FloatFromString_ParsesInvariant_UnderGermanCulture()
        {
            // de-DE では小数点が ',' のため、CurrentCulture 依存だと "1.5" が 15 になる
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var result = ComponentPropertySetter.SetProperties(
                    _component, new Dictionary<string, object> { { "floatField", "1.5" } });

                Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
                Assert.AreEqual(1.5f, GetSerializedValue("floatField", p => p.floatValue), 0.0001f);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        #endregion

        #region Enum Tests

        [Test]
        public void SetProperties_EnumFromLong_SetsEnumValueIndex()
        {
            // SimpleJson は整数を long で返すため、long は enumValueIndex として扱う
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "enumField", 2L } });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.AreEqual(2, GetSerializedValue("enumField", p => p.enumValueIndex));
        }

        [Test]
        public void SetProperties_EnumFromInt_SetsEnumValueIndex()
        {
            // 直接 API 呼び出しでは int が渡される
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "enumField", 1 } });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.AreEqual(1, GetSerializedValue("enumField", p => p.enumValueIndex));
        }

        [Test]
        public void SetProperties_EnumFromDouble_SetsEnumValueIndex()
        {
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "enumField", 2.0d } });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.AreEqual(2, GetSerializedValue("enumField", p => p.enumValueIndex));
        }

        [Test]
        public void SetProperties_EnumFromName_SetsEnumValueIndex()
        {
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "enumField", "Second" } });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.AreEqual(1, GetSerializedValue("enumField", p => p.enumValueIndex));
        }

        [Test]
        public void SetProperties_EnumFromInvalidName_Fails()
        {
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "enumField", "NoSuchValue" } });

            Assert.IsFalse(result.AllSucceeded);
        }

        #endregion

        #region ObjectReference Tests

        [Test]
        public void SetProperties_ObjectReferenceFromLongInstanceId_Resolves()
        {
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "objectField", (long)_testObject.GetInstanceID() } });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.AreEqual(_testObject, GetSerializedValue("objectField", p => p.objectReferenceValue));
        }

        [Test]
        public void SetProperties_ObjectReferenceFromIntInstanceId_Resolves()
        {
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "objectField", _testObject.GetInstanceID() } });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.AreEqual(_testObject, GetSerializedValue("objectField", p => p.objectReferenceValue));
        }

        [Test]
        public void SetProperties_ObjectReferenceFromDictInstanceId_Resolves()
        {
            var dict = new Dictionary<string, object> { { "instance_id", (long)_testObject.GetInstanceID() } };
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "objectField", dict } });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.AreEqual(_testObject, GetSerializedValue("objectField", p => p.objectReferenceValue));
        }

        [Test]
        public void SetProperties_ObjectReferenceFromOutOfRangeInstanceId_Fails()
        {
            var result = ComponentPropertySetter.SetProperties(
                _component, new Dictionary<string, object> { { "objectField", long.MaxValue } });

            Assert.IsFalse(result.AllSucceeded);
        }

        #endregion
    }
}
