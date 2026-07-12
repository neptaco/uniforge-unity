using NUnit.Framework;
using UniForge.Tools;

namespace UniForge.Tests
{
    [TestFixture]
    public class ToolCategoryTests
    {
        #region CompareOrder Tests - Alphabetical Ordering

        [Test]
        public void CompareOrder_SameCategory_ReturnsZero()
        {
            Assert.AreEqual(0, ToolCategory.CompareOrder(ToolCategory.GameObject, ToolCategory.GameObject));
        }

        [Test]
        public void CompareOrder_AlphabeticallyEarlier_ReturnsNegative()
        {
            // "Asset" comes before "GameObject" alphabetically
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Asset, ToolCategory.GameObject), 0);
        }

        [Test]
        public void CompareOrder_AlphabeticallyLater_ReturnsPositive()
        {
            // "Scene" comes after "Prefab" alphabetically
            Assert.Greater(ToolCategory.CompareOrder(ToolCategory.Scene, ToolCategory.Prefab), 0);
        }

        [Test]
        public void CompareOrder_FollowsAlphabeticalOrder()
        {
            // Verify alphabetical ordering
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Addressables, ToolCategory.Asset), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Asset, ToolCategory.Compilation), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Compilation, ToolCategory.Editor), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Editor, ToolCategory.GameObject), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.GameObject, ToolCategory.Input), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Input, ToolCategory.Logs), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Logs, ToolCategory.Material), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Material, ToolCategory.Prefab), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Prefab, ToolCategory.Scene), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Scene, ToolCategory.Test), 0);
        }

        #endregion

        #region CompareOrder Tests - Other Category at End

        [Test]
        public void CompareOrder_Other_ComesAfterRegularCategories()
        {
            Assert.Greater(ToolCategory.CompareOrder(ToolCategory.Other, ToolCategory.Test), 0);
            Assert.Greater(ToolCategory.CompareOrder(ToolCategory.Other, ToolCategory.Addressables), 0);
        }

        [Test]
        public void CompareOrder_RegularCategory_ComesBeforeOther()
        {
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.GameObject, ToolCategory.Other), 0);
        }

        [Test]
        public void CompareOrder_Null_TreatedAsOther()
        {
            Assert.Greater(ToolCategory.CompareOrder(null, ToolCategory.Test), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Test, null), 0);
        }

        [Test]
        public void CompareOrder_EmptyString_TreatedAsOther()
        {
            Assert.Greater(ToolCategory.CompareOrder("", ToolCategory.Test), 0);
            Assert.Less(ToolCategory.CompareOrder(ToolCategory.Test, ""), 0);
        }

        [Test]
        public void CompareOrder_BothOther_ReturnsZero()
        {
            Assert.AreEqual(0, ToolCategory.CompareOrder(ToolCategory.Other, ToolCategory.Other));
            Assert.AreEqual(0, ToolCategory.CompareOrder(null, ""));
            Assert.AreEqual(0, ToolCategory.CompareOrder("", ToolCategory.Other));
        }

        [Test]
        public void CompareOrder_UnknownCategory_SortedAlphabetically()
        {
            // Unknown categories are sorted alphabetically, not treated as Other
            Assert.Less(ToolCategory.CompareOrder("CustomCategory", ToolCategory.Editor), 0);
            Assert.Greater(ToolCategory.CompareOrder("ZCategory", ToolCategory.Test), 0);
        }

        #endregion
    }
}
