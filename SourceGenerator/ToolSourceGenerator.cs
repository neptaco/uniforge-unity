using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UniForge.SourceGenerator
{
    /// <summary>
    /// [Tool] 属性が付いたクラスから ToolDefinition を生成する Source Generator
    /// </summary>
    [Generator]
    public class ToolSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // [Tool] 属性を持つクラスを検出
            var toolClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidateClass(node),
                    transform: static (ctx, _) => GetToolClassInfo(ctx))
                .Where(static info => info is not null);

            // コンパイル情報と結合
            var compilationAndClasses = context.CompilationProvider.Combine(toolClasses.Collect());

            // コード生成
            context.RegisterSourceOutput(compilationAndClasses,
                static (spc, source) => Execute(source.Left, source.Right!, spc));
        }

        /// <summary>
        /// クラス宣言で属性を持つものを候補として検出
        /// </summary>
        private static bool IsCandidateClass(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax classDecl
                && classDecl.AttributeLists.Count > 0;
        }

        /// <summary>
        /// [Tool] 属性を持つクラスの情報を取得
        /// </summary>
        private static ToolClassInfo? GetToolClassInfo(GeneratorSyntaxContext context)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;

            // [Tool] 属性を探す
            foreach (var attributeList in classDecl.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var name = attribute.Name.ToString();
                    if (name == "Tool" || name == "ToolAttribute")
                    {
                        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
                        if (symbol is null) continue;

                        return ExtractToolInfo(classDecl, attribute, symbol, context.SemanticModel);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// ツール情報を抽出
        /// </summary>
        private static ToolClassInfo? ExtractToolInfo(
            ClassDeclarationSyntax classDecl,
            AttributeSyntax attribute,
            INamedTypeSymbol symbol,
            SemanticModel semanticModel)
        {
            var info = new ToolClassInfo
            {
                ClassName = symbol.Name,
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                FullyQualifiedName = symbol.ToDisplayString()
            };

            // 属性の引数を解析
            if (attribute.ArgumentList != null)
            {
                foreach (var arg in attribute.ArgumentList.Arguments)
                {
                    var nameColon = arg.NameColon?.Name.Identifier.Text;
                    var nameEquals = arg.NameEquals?.Name.Identifier.Text;
                    var name = nameColon ?? nameEquals;

                    var value = GetConstantValue(arg.Expression, semanticModel);

                    if (name == null && arg == attribute.ArgumentList.Arguments[0])
                    {
                        // 最初の位置引数は name
                        info.ToolName = value as string ?? "";
                    }
                    else if (name == "Description")
                    {
                        info.Description = value as string ?? "";
                    }
                    else if (name == "Title")
                    {
                        info.Title = value as string ?? "";
                    }
                    else if (name == "Kind")
                    {
                        info.IsQuery = value?.ToString()?.Contains("Query") == true;
                    }
                    else if (name == "Destructive")
                    {
                        info.Destructive = value as bool? ?? false;
                    }
                    else if (name == "Idempotent")
                    {
                        info.Idempotent = value as bool? ?? false;
                    }
                    else if (name == "Register")
                    {
                        info.Register = value as bool? ?? true;
                    }
                }
            }

            // ネストした Args クラスを探す
            var argsClass = classDecl.Members
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == "Args");

            if (argsClass != null)
            {
                info.Parameters = ExtractParameters(argsClass, semanticModel);
            }

            // [ToolOutput] 属性を探す
            foreach (var attrList in classDecl.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var attrName = attr.Name.ToString();
                    if (attrName == "ToolOutput" || attrName == "ToolOutputAttribute")
                    {
                        if (attr.ArgumentList?.Arguments.Count > 0)
                        {
                            var typeArg = attr.ArgumentList.Arguments[0];
                            if (typeArg.Expression is TypeOfExpressionSyntax typeOf)
                            {
                                var typeInfo = semanticModel.GetTypeInfo(typeOf.Type);
                                if (typeInfo.Type is INamedTypeSymbol outputType)
                                {
                                    info.OutputTypeName = outputType.ToDisplayString();
                                }
                            }
                        }
                    }
                }
            }

            return info;
        }

        /// <summary>
        /// Args クラスからパラメータ情報を抽出
        /// </summary>
        private static List<ParameterInfo> ExtractParameters(
            ClassDeclarationSyntax argsClass,
            SemanticModel semanticModel)
        {
            var parameters = new List<ParameterInfo>();

            foreach (var member in argsClass.Members)
            {
                if (member is FieldDeclarationSyntax field)
                {
                    foreach (var variable in field.Declaration.Variables)
                    {
                        var paramInfo = new ParameterInfo
                        {
                            Name = ToSnakeCase(variable.Identifier.Text),
                            FieldName = variable.Identifier.Text,
                            TypeName = field.Declaration.Type.ToString()
                        };

                        // [ToolParameter] 属性を探す
                        foreach (var attrList in field.AttributeLists)
                        {
                            foreach (var attr in attrList.Attributes)
                            {
                                var attrName = attr.Name.ToString();
                                if (attrName == "ToolParameter" || attrName == "ToolParameterAttribute")
                                {
                                    ExtractParameterAttribute(attr, paramInfo, semanticModel);
                                }
                            }
                        }

                        // 型からJSON Schema型を推定
                        paramInfo.JsonType = GetJsonType(field.Declaration.Type, semanticModel);

                        parameters.Add(paramInfo);
                    }
                }
                else if (member is PropertyDeclarationSyntax prop)
                {
                    var paramInfo = new ParameterInfo
                    {
                        Name = ToSnakeCase(prop.Identifier.Text),
                        FieldName = prop.Identifier.Text,
                        TypeName = prop.Type.ToString()
                    };

                    foreach (var attrList in prop.AttributeLists)
                    {
                        foreach (var attr in attrList.Attributes)
                        {
                            var attrName = attr.Name.ToString();
                            if (attrName == "ToolParameter" || attrName == "ToolParameterAttribute")
                            {
                                ExtractParameterAttribute(attr, paramInfo, semanticModel);
                            }
                        }
                    }

                    paramInfo.JsonType = GetJsonType(prop.Type, semanticModel);

                    parameters.Add(paramInfo);
                }
            }

            return parameters;
        }

        /// <summary>
        /// [ToolParameter] 属性の内容を抽出
        /// </summary>
        private static void ExtractParameterAttribute(
            AttributeSyntax attr,
            ParameterInfo paramInfo,
            SemanticModel semanticModel)
        {
            if (attr.ArgumentList == null) return;

            foreach (var arg in attr.ArgumentList.Arguments)
            {
                var nameColon = arg.NameColon?.Name.Identifier.Text;
                var nameEquals = arg.NameEquals?.Name.Identifier.Text;
                var name = nameColon ?? nameEquals;
                var value = GetConstantValue(arg.Expression, semanticModel);

                if (name == null && arg == attr.ArgumentList.Arguments[0])
                {
                    // 最初の位置引数は description
                    paramInfo.Description = value as string ?? "";
                }
                else if (name == "Name")
                {
                    paramInfo.Name = value as string ?? paramInfo.Name;
                }
                else if (name == "Description")
                {
                    paramInfo.Description = value as string ?? "";
                }
                else if (name == "Required")
                {
                    paramInfo.Required = value as bool? ?? false;
                }
                else if (name == "Default")
                {
                    paramInfo.DefaultValue = value;
                }
                else if (name == "Enum")
                {
                    paramInfo.EnumValues = (value as string)?.Split(',').Select(s => s.Trim()).ToArray();
                }
            }
        }

        /// <summary>
        /// 定数値を取得
        /// </summary>
        private static object? GetConstantValue(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            var constantValue = semanticModel.GetConstantValue(expression);
            if (constantValue.HasValue)
            {
                return constantValue.Value;
            }

            // メンバーアクセス（enum等）
            if (expression is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.ToString();
            }

            return null;
        }

        /// <summary>
        /// 型からJSON Schema型を取得
        /// </summary>
        private static string GetJsonType(TypeSyntax typeSyntax, SemanticModel semanticModel)
        {
            var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
            var type = typeInfo.Type;

            if (type == null) return "string";

            // Nullable<T> の場合は内部の型を取得
            if (type is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                type = namedType.TypeArguments[0];
            }

            return type.SpecialType switch
            {
                SpecialType.System_String => "string",
                SpecialType.System_Boolean => "boolean",
                SpecialType.System_Int32 => "integer",
                SpecialType.System_Int64 => "integer",
                SpecialType.System_Single => "number",
                SpecialType.System_Double => "number",
                _ when type.TypeKind == TypeKind.Array => "array",
                _ when type.Name == "List" => "array",
                _ => "object"
            };
        }

        /// <summary>
        /// PascalCase を snake_case に変換
        /// </summary>
        private static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (char.IsUpper(c) && i > 0)
                {
                    result.Append('_');
                }
                result.Append(char.ToLowerInvariant(c));
            }
            return result.ToString();
        }

        /// <summary>
        /// コード生成を実行
        /// </summary>
        private static void Execute(
            Compilation compilation,
            ImmutableArray<ToolClassInfo?> toolClasses,
            SourceProductionContext context)
        {
            var validTools = toolClasses
                .Where(t => t is not null)
                .Cast<ToolClassInfo>()
                .ToList();

            if (validTools.Count == 0) return;

            // 各ツールに対して拡張コードを生成
            foreach (var tool in validTools)
            {
                var source = GenerateToolDefinitionCode(tool);
                context.AddSource($"{tool.ClassName}.g.cs", SourceText.From(source, Encoding.UTF8));
            }

            // レジストリ登録用のコードを生成
            var registrySource = GenerateRegistryCode(validTools);
            context.AddSource("GeneratedToolRegistry.g.cs", SourceText.From(registrySource, Encoding.UTF8));
        }

        /// <summary>
        /// ToolDefinition を生成するコード
        /// </summary>
        private static string GenerateToolDefinitionCode(ToolClassInfo tool)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UniForge.Tools;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(tool.Namespace))
            {
                sb.AppendLine($"namespace {tool.Namespace}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"    partial class {tool.ClassName}");
            sb.AppendLine("    {");
            sb.AppendLine("        private static ToolDefinition? _generatedDefinition;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Generated tool definition</summary>");
            sb.AppendLine("        public static ToolDefinition GeneratedDefinition");
            sb.AppendLine("        {");
            sb.AppendLine("            get");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_generatedDefinition == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    _generatedDefinition = new ToolDefinition");
            sb.AppendLine("                    {");
            sb.AppendLine($"                        name = \"{tool.ToolName}\",");
            sb.AppendLine($"                        description = \"{EscapeString(tool.Description)}\",");

            // inputSchema
            sb.AppendLine("                        inputSchema = new Dictionary<string, object>");
            sb.AppendLine("                        {");
            sb.AppendLine("                            { \"type\", \"object\" },");
            sb.AppendLine("                            { \"properties\", new Dictionary<string, object>");
            sb.AppendLine("                                {");

            foreach (var param in tool.Parameters)
            {
                sb.AppendLine($"                                    {{ \"{param.Name}\", new Dictionary<string, object>");
                sb.AppendLine("                                        {");
                sb.AppendLine($"                                            {{ \"type\", \"{param.JsonType}\" }},");

                if (!string.IsNullOrEmpty(param.Description))
                {
                    sb.AppendLine($"                                            {{ \"description\", \"{EscapeString(param.Description)}\" }},");
                }

                if (param.DefaultValue != null)
                {
                    var defaultStr = FormatDefaultValue(param.DefaultValue);
                    sb.AppendLine($"                                            {{ \"default\", {defaultStr} }},");
                }

                if (param.EnumValues != null && param.EnumValues.Length > 0)
                {
                    var enumStr = string.Join(", ", param.EnumValues.Select(v => $"\"{v}\""));
                    sb.AppendLine($"                                            {{ \"enum\", new List<string> {{ {enumStr} }} }},");
                }

                sb.AppendLine("                                        }");
                sb.AppendLine("                                    },");
            }

            sb.AppendLine("                                }");
            sb.AppendLine("                            },");

            // required
            var requiredParams = tool.Parameters.Where(p => p.Required).Select(p => $"\"{p.Name}\"");
            sb.AppendLine($"                            {{ \"required\", new List<string> {{ {string.Join(", ", requiredParams)} }} }}");
            sb.AppendLine("                        },");

            // annotations
            sb.AppendLine("                        annotations = new ToolAnnotations");
            sb.AppendLine("                        {");
            sb.AppendLine($"                            title = \"{tool.Title ?? tool.ToolName}\",");
            sb.AppendLine($"                            readOnlyHint = {(tool.IsQuery ? "true" : "false")},");
            sb.AppendLine($"                            destructiveHint = {(tool.Destructive ? "true" : "false")},");
            sb.AppendLine($"                            idempotentHint = {(tool.Idempotent ? "true" : "false")},");
            sb.AppendLine("                            openWorldHint = false");
            sb.AppendLine("                        }");
            sb.AppendLine("                    };");
            sb.AppendLine("                }");
            sb.AppendLine("                return _generatedDefinition;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(tool.Namespace))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// レジストリ登録用コードを生成
        /// </summary>
        private static string GenerateRegistryCode(List<ToolClassInfo> tools)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UniForge.Tools;");
            sb.AppendLine();
            sb.AppendLine("namespace UniForge.Tools.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Auto-generated tool registry helper</summary>");
            sb.AppendLine("    public static class GeneratedToolRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>Get all generated tool definitions</summary>");
            sb.AppendLine("        public static IEnumerable<ToolDefinition> GetAllDefinitions()");
            sb.AppendLine("        {");

            foreach (var tool in tools.Where(t => t.Register))
            {
                sb.AppendLine($"            yield return {tool.FullyQualifiedName}.GeneratedDefinition;");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Register all generated handlers to registry</summary>");
            sb.AppendLine("        public static void RegisterAll(ToolRegistry registry)");
            sb.AppendLine("        {");

            foreach (var tool in tools.Where(t => t.Register))
            {
                sb.AppendLine($"            registry.Register(new {tool.FullyQualifiedName}());");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string EscapeString(string? input)
        {
            if (input == null) return "";
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatDefaultValue(object value)
        {
            return value switch
            {
                bool b => b ? "true" : "false",
                string s => $"\"{EscapeString(s)}\"",
                int i => i.ToString(),
                long l => l.ToString(),
                float f => f.ToString(),
                double d => d.ToString(),
                _ => $"\"{value}\""
            };
        }
    }

    /// <summary>
    /// ツールクラスの情報
    /// </summary>
    internal class ToolClassInfo
    {
        public string ClassName { get; set; } = "";
        public string Namespace { get; set; } = "";
        public string FullyQualifiedName { get; set; } = "";
        public string ToolName { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Title { get; set; }
        public bool IsQuery { get; set; } = true;
        public bool Destructive { get; set; }
        public bool Idempotent { get; set; }
        public bool Register { get; set; } = true;
        public List<ParameterInfo> Parameters { get; set; } = new();
        public string? OutputTypeName { get; set; }
    }

    /// <summary>
    /// パラメータ情報
    /// </summary>
    internal class ParameterInfo
    {
        public string Name { get; set; } = "";
        public string FieldName { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string JsonType { get; set; } = "string";
        public string? Description { get; set; }
        public bool Required { get; set; }
        public object? DefaultValue { get; set; }
        public string[]? EnumValues { get; set; }
    }
}
