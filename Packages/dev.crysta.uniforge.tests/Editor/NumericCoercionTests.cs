using System.Globalization;
using NUnit.Framework;
using UniForge.Tools;

namespace UniForge.Tests
{
    /// <summary>
    /// NumericCoercion のユニットテスト
    /// </summary>
    [TestFixture]
    public class NumericCoercionTests
    {
        #region TryToInt64 Tests

        [Test]
        public void TryToInt64_Int_ReturnsValue()
        {
            Assert.IsTrue(NumericCoercion.TryToInt64(42, out var result));
            Assert.AreEqual(42L, result);
        }

        [Test]
        public void TryToInt64_Long_ReturnsValue()
        {
            Assert.IsTrue(NumericCoercion.TryToInt64(1234567890123L, out var result));
            Assert.AreEqual(1234567890123L, result);
        }

        [Test]
        public void TryToInt64_Double_TruncatesToValue()
        {
            Assert.IsTrue(NumericCoercion.TryToInt64(2.0d, out var result));
            Assert.AreEqual(2L, result);
        }

        [Test]
        public void TryToInt64_Float_TruncatesToValue()
        {
            Assert.IsTrue(NumericCoercion.TryToInt64(3f, out var result));
            Assert.AreEqual(3L, result);
        }

        [Test]
        public void TryToInt64_IntegerString_ParsesInvariant()
        {
            Assert.IsTrue(NumericCoercion.TryToInt64("123", out var result));
            Assert.AreEqual(123L, result);
        }

        [Test]
        public void TryToInt64_NonNumericString_Fails()
        {
            Assert.IsFalse(NumericCoercion.TryToInt64("abc", out _));
        }

        [Test]
        public void TryToInt64_DecimalString_Fails()
        {
            Assert.IsFalse(NumericCoercion.TryToInt64("1.5", out _));
        }

        [Test]
        public void TryToInt64_Null_Fails()
        {
            Assert.IsFalse(NumericCoercion.TryToInt64(null, out _));
        }

        [Test]
        public void TryToInt64_Bool_Fails()
        {
            Assert.IsFalse(NumericCoercion.TryToInt64(true, out _));
        }

        #endregion

        #region TryToDouble Tests

        [Test]
        public void TryToDouble_Int_ReturnsValue()
        {
            Assert.IsTrue(NumericCoercion.TryToDouble(42, out var result));
            Assert.AreEqual(42.0, result, 0.0001);
        }

        [Test]
        public void TryToDouble_Long_ReturnsValue()
        {
            Assert.IsTrue(NumericCoercion.TryToDouble(42L, out var result));
            Assert.AreEqual(42.0, result, 0.0001);
        }

        [Test]
        public void TryToDouble_Float_ReturnsValue()
        {
            Assert.IsTrue(NumericCoercion.TryToDouble(1.5f, out var result));
            Assert.AreEqual(1.5, result, 0.0001);
        }

        [Test]
        public void TryToDouble_Double_ReturnsValue()
        {
            Assert.IsTrue(NumericCoercion.TryToDouble(2.5d, out var result));
            Assert.AreEqual(2.5, result, 0.0001);
        }

        [Test]
        public void TryToDouble_DecimalString_ParsesInvariant_UnderGermanCulture()
        {
            // de-DE では小数点が ',' のため、CurrentCulture 依存だと "1.5" が 15 になる
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                Assert.IsTrue(NumericCoercion.TryToDouble("1.5", out var result));
                Assert.AreEqual(1.5, result, 0.0001);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Test]
        public void TryToDouble_NonNumericString_Fails()
        {
            Assert.IsFalse(NumericCoercion.TryToDouble("abc", out _));
        }

        [Test]
        public void TryToDouble_Null_Fails()
        {
            Assert.IsFalse(NumericCoercion.TryToDouble(null, out _));
        }

        #endregion
    }
}
