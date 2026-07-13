using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UniForge.Tools;

namespace UniForge.Tests
{
    /// <summary>
    /// ComponentPropertyGetter のユニットテスト
    /// </summary>
    [TestFixture]
    public class ComponentPropertyGetterTests
    {
        private enum TestEnum
        {
            First,
            Second,
            Third
        }

        private sealed class PropertyGetterTestComponent : MonoBehaviour
        {
            public TestEnum enumField;
            public GameObject objectField;
            public int intField = 42;
        }

        private GameObject _testObject;
        private PropertyGetterTestComponent _component;

        [SetUp]
        public void SetUp()
        {
            _testObject = new GameObject("PropertyGetterTestObject");
            _component = _testObject.AddComponent<PropertyGetterTestComponent>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testObject != null)
            {
                Object.DestroyImmediate(_testObject);
            }
        }

        /// <summary>
        /// enum フィールドに enum メンバーと一致しない生の int を強制設定する
        /// （この場合 Unity は enumValueIndex を -1 にする）
        /// </summary>
        private void ForceInvalidEnumRawValue(int rawValue)
        {
            using (var so = new SerializedObject(_component))
            {
                var prop = so.FindProperty("enumField");
                prop.intValue = rawValue;
                so.ApplyModifiedProperties();
            }
        }

        #region Enum Tests

        [Test]
        public void GetProperties_ValidEnum_ReturnsIndexAndName()
        {
            _component.enumField = TestEnum.Second;

            var result = ComponentPropertyGetter.GetProperties(_component, new[] { "enumField" });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            var dict = result.properties["enumField"] as System.Collections.Generic.Dictionary<string, object>;
            Assert.IsNotNull(dict);
            Assert.AreEqual(1, dict["index"]);
            Assert.AreEqual("Second", dict["name"]);
        }

        [Test]
        public void GetProperties_EnumWithInvalidRawValue_ReturnsRawInt()
        {
            ForceInvalidEnumRawValue(999);

            PropertyGetResult result = null;
            Assert.DoesNotThrow(() => result = ComponentPropertyGetter.GetProperties(_component, new[] { "enumField" }));

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.AreEqual(999, result.properties["enumField"]);
        }

        [Test]
        public void GetProperties_GetAll_EnumWithInvalidRawValue_DoesNotThrow()
        {
            ForceInvalidEnumRawValue(999);

            PropertyGetResult result = null;
            Assert.DoesNotThrow(() => result = ComponentPropertyGetter.GetProperties(_component));

            Assert.IsTrue(result.properties.ContainsKey("enumField"));
            Assert.AreEqual(999, result.properties["enumField"]);
        }

        #endregion

        #region Unassigned ObjectReference Tests

        [Test]
        public void GetProperties_GetAll_IncludesUnassignedObjectReferenceAsNull()
        {
            // 未割り当ての ObjectReference も get-all モードでキーとして含まれる
            // （「未割り当て」と「存在しないプロパティ」を区別できるようにする）
            var result = ComponentPropertyGetter.GetProperties(_component);

            Assert.IsTrue(result.properties.ContainsKey("objectField"));
            Assert.IsNull(result.properties["objectField"]);
        }

        [Test]
        public void GetProperties_NamedMode_UnassignedObjectReference_ReturnsNull()
        {
            var result = ComponentPropertyGetter.GetProperties(_component, new[] { "objectField" });

            Assert.IsTrue(result.AllSucceeded, string.Join("; ", result.errors));
            Assert.IsTrue(result.properties.ContainsKey("objectField"));
            Assert.IsNull(result.properties["objectField"]);
        }

        [Test]
        public void GetProperties_GetAll_IncludesOtherProperties()
        {
            var result = ComponentPropertyGetter.GetProperties(_component);

            Assert.IsTrue(result.properties.ContainsKey("intField"));
            Assert.AreEqual(42, result.properties["intField"]);
        }

        #endregion
    }
}
