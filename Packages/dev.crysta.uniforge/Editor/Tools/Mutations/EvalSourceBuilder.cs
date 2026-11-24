using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UniForge.Tools.Mutations
{
    internal static class EvalSourceBuilder
    {
        internal enum Form
        {
            Statements,
            Expression,
            StatementsWithSemicolon
        }

        private const string Identifier = @"@?[\p{L}\p{Nl}_][\p{L}\p{Nl}\p{Nd}\p{Pc}\p{Mn}\p{Mc}\p{Cf}]*";
        private const string NamePart = Identifier + @"(?:\s*<[^;=]+>)?";
        private static readonly Regex UsingDirective = new Regex(
            @"^\s*(?<directive>using\s+(?:static\s+|" + Identifier + @"\s*=\s*)?" +
            NamePart + @"(?:\s*(?:\.|::)\s*" + NamePart + @")*\s*;)\s*(?://.*)?$");

        internal static (Form first, Form? retry, bool preferStatementDiagnostics) GetForms(string code)
        {
            var lastCharacter = LastCodeCharacter(StripLeadingBom(code));
            return lastCharacter == ';'
                ? (Form.Statements, (Form?)null, false)
                : (Form.Expression, Form.StatementsWithSemicolon, lastCharacter == '}');
        }

        internal static string Build(string code, Form form)
        {
            code = StripLeadingBom(code);
            // Retain line endings, including those inside verbatim string literals.
            var lines = Regex.Split(code, @"(\r\n|[\r\n\u0085\u2028\u2029])");
            var usings = new HashSet<string>(StringComparer.Ordinal);
            var source = new StringBuilder();
            for (var index = 0; index < lines.Length; index += 2)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                var match = UsingDirective.Match(line);
                if (!match.Success)
                    break;

                source.Append("#line ").Append(index / 2 + 1).AppendLine(" \"eval\"");
                source.AppendLine(line);
                lines[index] = "";
                usings.Add(match.Groups["directive"].Value);
            }

            source.AppendLine("#line default");
            foreach (var name in new[] { "System", "System.Collections.Generic", "System.Linq", "System.Threading.Tasks", "UnityEngine", "UnityEditor" })
            {
                var directive = $"using {name};";
                if (!usings.Contains(directive))
                    source.AppendLine(directive);
            }

            source.AppendLine("#pragma warning disable CS1998, CS0162");
            source.AppendLine("public static class UniForgeEvalScript {");
            source.AppendLine("public static async global::System.Threading.Tasks.Task<object> Run(global::System.Threading.CancellationToken cancellationToken) {");
            if (form == Form.Expression)
                source.AppendLine("return (");
            source.AppendLine("#line 1 \"eval\"");
            source.AppendLine(string.Concat(lines));
            source.AppendLine("#line default");
            if (form == Form.Expression)
                source.AppendLine(");");
            else if (form == Form.StatementsWithSemicolon)
                source.AppendLine(";");
            source.AppendLine("return null;");
            source.AppendLine("}}");
            return source.ToString();
        }

        private static string StripLeadingBom(string code) =>
            code.Length > 0 && code[0] == '\uFEFF' ? code.Substring(1) : code;

        private static char LastCodeCharacter(string text)
        {
            var end = LastCodeCharacterEnd(text);
            return end == 0 ? '\0' : text[end - 1];
        }

        private static int LastCodeCharacterEnd(string text)
        {
            var lastEnd = 0;
            for (var index = 0; index < text.Length;)
            {
                if (char.IsWhiteSpace(text[index]))
                    index++;
                else if (!SkipComment(text, ref index))
                {
                    if (!SkipLiteral(text, ref index))
                        index++;
                    lastEnd = index;
                }
            }

            return lastEnd;
        }

        private static bool SkipComment(string text, ref int index)
        {
            if (text[index] != '/' || index + 1 >= text.Length)
                return false;

            if (text[index + 1] == '/')
            {
                while (index < text.Length && "\r\n\u0085\u2028\u2029".IndexOf(text[index]) < 0)
                    index++;
                return true;
            }

            if (text[index + 1] != '*')
                return false;

            var end = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
            index = end < 0 ? text.Length : end + 2;
            return true;
        }

        private static bool SkipLiteral(string text, ref int index)
        {
            var start = index;
            var interpolated = text[start] == '$';
            if (interpolated)
                start++;
            var verbatim = start < text.Length && text[start] == '@';
            if (verbatim)
                start++;
            if (verbatim && !interpolated && start < text.Length && text[start] == '$')
            {
                interpolated = true;
                start++;
            }

            if (start >= text.Length || (text[start] != '"' && (start != index || text[start] != '\'')))
                return false;

            var quote = text[start];
            index = start + 1;
            while (index < text.Length)
            {
                var character = text[index++];
                if (!verbatim && character == '\\')
                {
                    if (index < text.Length)
                        index++;
                }
                else if (character == quote)
                {
                    if (!verbatim || index >= text.Length || text[index] != quote)
                        break;
                    index++;
                }
                else if (interpolated && character == '{')
                {
                    if (index < text.Length && text[index] == '{')
                        index++;
                    else
                        SkipInterpolation(text, ref index);
                }
            }

            return true;
        }

        private static void SkipInterpolation(string text, ref int index)
        {
            var nesting = 0;
            while (index < text.Length)
            {
                if (SkipComment(text, ref index) || SkipLiteral(text, ref index))
                    continue;

                switch (text[index++])
                {
                    case '}' when nesting == 0:
                        return;
                    case '{':
                    case '(':
                    case '[':
                        nesting++;
                        break;
                    case '}':
                    case ')':
                    case ']':
                        if (nesting > 0)
                            nesting--;
                        break;
                    case ':' when nesting == 0:
                        if (index < text.Length && text[index] == ':')
                        {
                            index++; // Alias qualifier, such as global::.
                            break;
                        }

                        // The format text can contain characters such as // that are not comments.
                        while (index < text.Length && text[index++] != '}') { }
                        return;
                }
            }
        }
    }
}
