using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace UniForge.Tests
{
    /// <summary>
    /// JsonObject の文字列フォールバックパースがカルチャ非依存
    /// （InvariantCulture）であることを検証するテスト。
    /// </summary>
    [TestFixture]
    public class JsonObjectCultureTests
    {
        private CultureInfo _originalCulture;

        [SetUp]
        public void SetUp()
        {
            _originalCulture = Thread.CurrentThread.CurrentCulture;
        }

        [TearDown]
        public void TearDown()
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture;
        }

        [Test]
        public void GetFloat_StringValue_ParsesInvariantUnderGermanCulture()
        {
            // de-DE では '.' は桁区切り扱いのため、カルチャ依存パースだと 314 になってしまう
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var obj = JsonObject.Parse("{\"v\": \"3.14\"}");
            Assert.AreEqual(3.14f, obj.GetFloat("v"), 0.0001f);
        }

        [Test]
        public void GetFloat_GermanFormattedString_ReturnsDefault()
        {
            // カルチャ形式の文字列（"3,14"）は Invariant では不正 → デフォルト値
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var obj = JsonObject.Parse("{\"v\": \"3,14\"}");
            Assert.AreEqual(-1f, obj.GetFloat("v", -1f), 0.0001f);
        }

        [Test]
        public void GetInt_NegativeStringValue_ParsesInvariantUnderGermanCulture()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var obj = JsonObject.Parse("{\"v\": \"-42\"}");
            Assert.AreEqual(-42, obj.GetInt("v"));
        }

        [Test]
        public void GetLong_StringValue_ParsesInvariantUnderGermanCulture()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var obj = JsonObject.Parse("{\"v\": \"9876543210\"}");
            Assert.AreEqual(9876543210L, obj.GetLong("v"));
        }
    }
}
