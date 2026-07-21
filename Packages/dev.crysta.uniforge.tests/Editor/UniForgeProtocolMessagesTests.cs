using NUnit.Framework;

namespace UniForge.Tests
{
    /// <summary>
    /// ExtractJsonObjectField（ExtractParamsJson / ExtractArgsJson 経由）の
    /// 文字列リテラルを考慮したブレースカウントの検証。
    /// </summary>
    [TestFixture]
    public class UniForgeProtocolMessagesTests
    {
        [Test]
        public void ExtractParamsJson_ClosingBraceInsideString_ReturnsFullObject()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"method\":\"daemon.executeTool\",\"params\":{\"text\":\"has } brace\",\"next\":1}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(json);
            Assert.AreEqual("{\"text\":\"has } brace\",\"next\":1}", result);
        }

        [Test]
        public void ExtractParamsJson_OpeningBraceInsideString_ReturnsFullObject()
        {
            var json = "{\"params\":{\"text\":\"has { brace\"}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(json);
            Assert.AreEqual("{\"text\":\"has { brace\"}", result);
        }

        [Test]
        public void ExtractParamsJson_EscapedQuotesAndBraceInsideString_ReturnsFullObject()
        {
            // 値: say \"hi\" and }
            var json = "{\"params\":{\"text\":\"say \\\"hi\\\" and }\"}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(json);
            Assert.AreEqual("{\"text\":\"say \\\"hi\\\" and }\"}", result);
        }

        [Test]
        public void ExtractParamsJson_EscapedBackslashBeforeClosingQuote_ReturnsFullObject()
        {
            // 値がエスケープ済みバックスラッシュで終わる: path\
            var json = "{\"params\":{\"path\":\"C:\\\\temp\\\\\",\"n\":1}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(json);
            Assert.AreEqual("{\"path\":\"C:\\\\temp\\\\\",\"n\":1}", result);
        }

        [Test]
        public void ExtractParamsJson_NestedObjects_ReturnsFullObject()
        {
            var json = "{\"params\":{\"a\":{\"b\":{\"c\":1}},\"d\":2}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(json);
            Assert.AreEqual("{\"a\":{\"b\":{\"c\":1}},\"d\":2}", result);
        }

        [Test]
        public void ExtractParamsJson_RegexLikeContent_ReturnsFullObject()
        {
            var json = "{\"params\":{\"pattern\":\"a{2,3}\"}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(json);
            Assert.AreEqual("{\"pattern\":\"a{2,3}\"}", result);
        }

        [Test]
        public void ExtractParamsJson_CodeSnippetContent_ReturnsFullObject()
        {
            // 文字列内に閉じられない '{' を含むコード片
            var json = "{\"params\":{\"code\":\"if (x) { return;\",\"line\":10}}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(json);
            Assert.AreEqual("{\"code\":\"if (x) { return;\",\"line\":10}", result);
        }

        [Test]
        public void ExtractArgsJson_UnbalancedBracesInsideString_ReturnsFullObject()
        {
            var json = "{\"args\":{\"snippet\":\"} } {\",\"ok\":true}}";
            var result = UniForgeProtocolMessages.ExtractArgsJson(json);
            Assert.AreEqual("{\"snippet\":\"} } {\",\"ok\":true}", result);
        }

        [Test]
        public void ExtractParamsJson_MissingField_ReturnsEmptyObject()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"method\":\"x\"}";
            var result = UniForgeProtocolMessages.ExtractParamsJson(json);
            Assert.AreEqual("{}", result);
        }

        [Test]
        public void TryParseUnityRegisterResponse_WithPackageAndUnityVersions_ReturnsVersions()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":\"u-1\",\"result\":{\"success\":true,\"latestPackageVersion\":\"0.12.0\",\"minPackageVersion\":\"0.10.0\",\"latestPackageUnity\":\"6000.2\",\"latestPackageUnityRelease\":\"0f1\"}}";

            var parsed = UniForgeProtocolMessages.TryParseUnityRegisterResponse(
                json,
                out var requestId,
                out var success,
                out var latestPackageVersion,
                out var minPackageVersion,
                out var latestPackageUnity,
                out var latestPackageUnityRelease);

            Assert.IsTrue(parsed);
            Assert.AreEqual("u-1", requestId);
            Assert.IsTrue(success);
            Assert.AreEqual("0.12.0", latestPackageVersion);
            Assert.AreEqual("0.10.0", minPackageVersion);
            Assert.AreEqual("6000.2", latestPackageUnity);
            Assert.AreEqual("0f1", latestPackageUnityRelease);
        }

        [Test]
        public void TryParseUnityRegisterResponse_WithoutPackageVersions_ReturnsNullVersions()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":\"u-1\",\"result\":{\"success\":true}}";

            var parsed = UniForgeProtocolMessages.TryParseUnityRegisterResponse(
                json,
                out var requestId,
                out var success,
                out var latestPackageVersion,
                out var minPackageVersion,
                out var latestPackageUnity,
                out var latestPackageUnityRelease);

            Assert.IsTrue(parsed);
            Assert.AreEqual("u-1", requestId);
            Assert.IsTrue(success);
            Assert.IsNull(latestPackageVersion);
            Assert.IsNull(minPackageVersion);
            Assert.IsNull(latestPackageUnity);
            Assert.IsNull(latestPackageUnityRelease);
        }

        [Test]
        public void TryParseUnityRegisterResponse_WithoutSuccess_ReturnsAvailableVersionFields()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":\"u-1\",\"result\":{\"latestPackageVersion\":\"0.12.0\"}}";

            var parsed = UniForgeProtocolMessages.TryParseUnityRegisterResponse(
                json,
                out var requestId,
                out var success,
                out var latestPackageVersion,
                out var minPackageVersion,
                out var latestPackageUnity,
                out var latestPackageUnityRelease);

            Assert.IsTrue(parsed);
            Assert.AreEqual("u-1", requestId);
            Assert.IsTrue(success);
            Assert.AreEqual("0.12.0", latestPackageVersion);
            Assert.IsNull(minPackageVersion);
            Assert.IsNull(latestPackageUnity);
            Assert.IsNull(latestPackageUnityRelease);
        }

        [Test]
        public void TryParseUnityRegisterResponse_WithExplicitFailure_ReturnsFalseSuccess()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":\"u-1\",\"result\":{\"success\":false,\"latestPackageVersion\":\"0.12.0\"}}";

            var parsed = UniForgeProtocolMessages.TryParseUnityRegisterResponse(
                json,
                out var requestId,
                out var success,
                out var latestPackageVersion,
                out var minPackageVersion,
                out var latestPackageUnity,
                out var latestPackageUnityRelease);

            Assert.IsTrue(parsed);
            Assert.AreEqual("u-1", requestId);
            Assert.IsFalse(success);
            Assert.AreEqual("0.12.0", latestPackageVersion);
            Assert.IsNull(minPackageVersion);
            Assert.IsNull(latestPackageUnity);
            Assert.IsNull(latestPackageUnityRelease);
        }

        [Test]
        public void TryParseUnityRegisterResponse_WithMismatchedRequestId_ReturnsFalse()
        {
            var json = "{\"jsonrpc\":\"2.0\",\"id\":\"u-old\",\"result\":{\"success\":true,\"latestPackageVersion\":\"0.12.0\"}}";

            var parsed = UniForgeProtocolMessages.TryParseUnityRegisterResponse(
                json,
                "u-current",
                out var success,
                out var latestPackageVersion,
                out var minPackageVersion,
                out var latestPackageUnity,
                out var latestPackageUnityRelease);

            Assert.IsFalse(parsed);
            Assert.IsFalse(success);
            Assert.IsNull(latestPackageVersion);
            Assert.IsNull(minPackageVersion);
            Assert.IsNull(latestPackageUnity);
            Assert.IsNull(latestPackageUnityRelease);
        }

        [Test]
        public void TryParseUnityRegisterResponse_WithInvalidJson_ReturnsFalse()
        {
            var parsed = UniForgeProtocolMessages.TryParseUnityRegisterResponse(
                "{invalid-json}",
                out var requestId,
                out var success,
                out var latestPackageVersion,
                out var minPackageVersion,
                out var latestPackageUnity,
                out var latestPackageUnityRelease);

            Assert.IsFalse(parsed);
            Assert.IsNull(requestId);
            Assert.IsFalse(success);
            Assert.IsNull(latestPackageVersion);
            Assert.IsNull(minPackageVersion);
            Assert.IsNull(latestPackageUnity);
            Assert.IsNull(latestPackageUnityRelease);
        }
    }
}
