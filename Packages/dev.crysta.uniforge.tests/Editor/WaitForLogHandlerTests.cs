using System;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UniForge.Tools;
using UniForge.Tools.Queries;

namespace UniForge.Tests
{
    /// <summary>
    /// wait-for-log ツールの初回チェック（lookback_ms）の挙動を検証するテスト。
    /// </summary>
    [TestFixture]
    public class WaitForLogHandlerTests
    {
        [SetUp]
        public void SetUp()
        {
            ConsoleLogCapture.instance.EnsureSubscribed();
            ConsoleLogCapture.instance.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ConsoleLogCapture.instance.Clear();
        }

        private static string CreateMarker() => "WaitForLogTest_" + Guid.NewGuid().ToString("N");

        [Test]
        public void Execute_LogEmittedJustBefore_FoundByDefaultLookback()
        {
            var marker = CreateMarker();
            Debug.Log(marker);
            // ログの timestamp が呼び出し時刻より確実に前になるようにする
            Thread.Sleep(10);

            var handler = new WaitForLogHandler();
            var result = handler.Execute("{\"pattern\":\"" + marker + "\"}");

            Assert.AreEqual(ToolResultKind.Complete, result.Kind,
                "Log emitted just before the call must be found immediately via default lookback");
            var output = (WaitForLogHandler.Output)result.ResultPayload;
            Assert.IsTrue(output.found);
            Assert.GreaterOrEqual(output.log_count, 1);
        }

        [Test]
        public void Execute_LookbackZero_ExistingLogIsIgnored()
        {
            var marker = CreateMarker();
            Debug.Log(marker);
            Thread.Sleep(10);

            var handler = new WaitForLogHandler();
            var result = handler.Execute(
                "{\"pattern\":\"" + marker + "\",\"lookback_ms\":0,\"timeout_ms\":100}");

            Assert.AreEqual(ToolResultKind.WaitForDomainReload, result.Kind,
                "lookback_ms=0 must preserve old semantics (only logs after the call)");
        }

        [Test]
        public void Execute_NegativeLookback_TreatedAsZero()
        {
            var marker = CreateMarker();
            Debug.Log(marker);
            Thread.Sleep(10);

            var handler = new WaitForLogHandler();
            var result = handler.Execute(
                "{\"pattern\":\"" + marker + "\",\"lookback_ms\":-500,\"timeout_ms\":100}");

            Assert.AreEqual(ToolResultKind.WaitForDomainReload, result.Kind,
                "Negative lookback_ms must be clamped to 0");
        }
    }
}
