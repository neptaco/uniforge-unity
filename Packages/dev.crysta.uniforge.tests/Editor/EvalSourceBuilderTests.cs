using NUnit.Framework;
using UniForge.Tools.Mutations;

namespace UniForge.Tests
{
    public class EvalSourceBuilderTests
    {
        [Test]
        public void ExtractsLeadingUsingsAndPreservesOriginalLineAndColumn()
        {
            const string code = "// imports\r\n  using System.Text; // namespace\r\n\r\n" +
                "using static System.Math;\r\n// alias follows\r\n    using Text = System.Text.StringBuilder;\r\n" +
                "    new Text().Append(Abs(-1)).ToString()";

            var source = EvalSourceBuilder.Build(code, EvalSourceBuilder.Form.Expression);

            Assert.That(source, Does.Contain("#line 2 \"eval\"\n  using System.Text; // namespace\n"));
            Assert.That(source, Does.Contain("#line 4 \"eval\"\nusing static System.Math;\n"));
            Assert.That(source, Does.Contain("#line 6 \"eval\"\n    using Text = System.Text.StringBuilder;\n"));
            Assert.That(source, Does.Contain("return (\n#line 1 \"eval\"\n" +
                "// imports\r\n\r\n\r\n\r\n// alias follows\r\n\r\n" +
                "    new Text().Append(Abs(-1)).ToString()\n#line default\n);"));
        }

        [TestCase("using var x = new System.IO.MemoryStream();")]
        [TestCase("using (new System.IO.MemoryStream()) { }")]
        [TestCase("using System.IO.Stream s = new System.IO.MemoryStream();")]
        [TestCase("using System; /* a */ return 1; /* b */")]
        public void LeavesUsingStatementsInTheMethod(string statement)
        {
            var source = EvalSourceBuilder.Build(statement, EvalSourceBuilder.Form.Statements);
            const string bodyStart = "#line 1 \"eval\"\n";
            var body = source.Substring(source.LastIndexOf(bodyStart, System.StringComparison.Ordinal));
            Assert.That(body, Does.StartWith(bodyStart + statement + "\n#line default\n"));
        }

        [Test]
        public void StopsExtractingUsingsAtTheFirstStatement()
        {
            const string code = "using System.Text;\nreturn null;\nusing System.IO;";
            var source = EvalSourceBuilder.Build(code, EvalSourceBuilder.Form.Statements);
            Assert.That(source, Does.Contain("#line 1 \"eval\"\nusing System.Text;\n"));
            Assert.That(source, Does.Contain("#line 1 \"eval\"\n\nreturn null;\nusing System.IO;\n"));
        }

        [TestCase("return 2;", "Statements", null)]
        [TestCase("return \"//{}\"; // }", "Statements", null)]
        [TestCase("return @\"//literal\r\n;\"; /* } */", "Statements", null)]
        [TestCase("return $\"{string.Concat(\"//\")}\"; // }", "Statements", null)]
        [TestCase("if (false) { }", "Expression", "StatementsWithSemicolon")]
        [TestCase("new[] { 1, 2 } /* ; */ // ;", "Expression", "StatementsWithSemicolon")]
        [TestCase("new List<int> { 1, 2 }", "Expression", "StatementsWithSemicolon")]
        [TestCase("1 + 1", "Expression", "StatementsWithSemicolon")]
        [TestCase("$\"{1://}\" // ;", "Expression", "StatementsWithSemicolon")]
        [TestCase("Debug.Log(\"x\") // ;", "Expression", "StatementsWithSemicolon")]
        public void UsesLastCodeTokenToOrderAttempts(string code, string first, string retry)
        {
            var forms = EvalSourceBuilder.GetForms(code);
            Assert.That(forms.first.ToString(), Is.EqualTo(first));
            Assert.That(forms.retry?.ToString(), Is.EqualTo(retry));
        }

        [Test]
        public void PutsAddedSemicolonOnItsOwnLine()
        {
            var source = EvalSourceBuilder.Build("Debug.Log(\"x\") // log", EvalSourceBuilder.Form.StatementsWithSemicolon);
            Assert.That(source, Does.Contain("#line 1 \"eval\"\nDebug.Log(\"x\") // log\n#line default\n;\n"));
        }

        [Test]
        public void StripsLeadingBomBeforeExtractingUsings()
        {
            const string code = "using System.Text;\nreturn new StringBuilder();";
            var withBom = "\uFEFF" + code;
            Assert.That(EvalSourceBuilder.Build(withBom, EvalSourceBuilder.Form.Statements),
                Is.EqualTo(EvalSourceBuilder.Build(code, EvalSourceBuilder.Form.Statements)));
            Assert.That(EvalSourceBuilder.GetForms(withBom), Is.EqualTo(EvalSourceBuilder.GetForms(code)));
        }
    }
}
