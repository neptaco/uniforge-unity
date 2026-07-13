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
    }
}
