using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UniForge.Tools
{
    /// <summary>
    /// リフレクションからToolDefinitionを生成するビルダー
    /// </summary>
    public static class ToolDefinitionBuilder
    {
        /// <summary>
        /// ハンドラークラスからToolDefinitionを生成
        /// </summary>
        public static ToolDefinition FromHandler<T>() where T : IToolHandler
        {
            return FromHandler(typeof(T));
        }

        /// <summary>
        /// ハンドラークラスからToolDefinitionを生成
        /// </summary>
        public static ToolDefinition FromHandler(Type handlerType)
        {
            var toolAttr = handlerType.GetCustomAttribute<ToolAttribute>();
            if (toolAttr == null)
            {
                throw new ArgumentException(
                    $"Handler type {handlerType.Name} must have [Tool] attribute",
                    nameof(handlerType));
            }

            var definition = new ToolDefinition
            {
                name = toolAttr.Name,
                description = toolAttr.Description,
                category = toolAttr.Category ?? ToolCategory.Other,
                annotations = new ToolAnnotations
                {
                    title = toolAttr.Title ?? toolAttr.Name,
                    readOnlyHint = toolAttr.Kind == ToolKind.Query,
                    destructiveHint = toolAttr.Kind == ToolKind.Mutation && toolAttr.Destructive,
                    idempotentHint = toolAttr.Idempotent,
                    openWorldHint = false
                }
            };

            // 引数クラスを探す
            var argsType = FindArgsType(handlerType);
            definition.inputSchema = BuildInputSchema(argsType);

            // 出力スキーマを探す
            var outputAttr = handlerType.GetCustomAttribute<ToolOutputAttribute>();
            if (outputAttr != null)
            {
                definition.outputSchema = BuildSchema(outputAttr.OutputType);
            }

            return definition;
        }

        /// <summary>
        /// アセンブリ内の全ハンドラーからToolDefinitionを生成
        /// </summary>
        public static IEnumerable<ToolDefinition> FromAssembly(Assembly assembly)
        {
            var handlerInterface = typeof(IToolHandler);
            var handlerTypes = assembly.GetTypes()
                .Where(t => handlerInterface.IsAssignableFrom(t)
                         && !t.IsAbstract
                         && !t.IsInterface
                         && t.GetCustomAttribute<ToolAttribute>()?.Register != false);

            foreach (var type in handlerTypes)
            {
                yield return FromHandler(type);
            }
        }

        /// <summary>
        /// 引数クラスを探す（ネストクラスまたはArgs suffix）
        /// </summary>
        private static Type FindArgsType(Type handlerType)
        {
            // 1. ネストクラス "Args" を探す
            var nestedArgs = handlerType.GetNestedType("Args", BindingFlags.Public | BindingFlags.NonPublic);
            if (nestedArgs != null) return nestedArgs;

            // 2. 同名 + "Args" クラスを探す
            var argsTypeName = handlerType.Name.Replace("Handler", "Args");
            var argsType = handlerType.Assembly.GetType(
                handlerType.Namespace + "." + argsTypeName);
            if (argsType != null) return argsType;

            // 3. 見つからない場合は空のスキーマ
            return null;
        }

        /// <summary>
        /// 型からJSON Schemaを生成
        /// </summary>
        private static Dictionary<string, object> BuildInputSchema(Type argsType)
        {
            var schema = new Dictionary<string, object>
            {
                { "type", "object" }
            };

            if (argsType == null)
            {
                schema["properties"] = new Dictionary<string, object>();
                schema["required"] = new List<string>();
                return schema;
            }

            var properties = new Dictionary<string, object>();
            var required = new List<string>();

            // フィールドとプロパティを処理
            var members = argsType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Cast<MemberInfo>()
                .Concat(argsType.GetProperties(BindingFlags.Public | BindingFlags.Instance));

            foreach (var member in members)
            {
                var paramAttr = member.GetCustomAttribute<ToolParameterAttribute>();

                // 属性がない場合もデフォルトで含める
                var paramName = paramAttr?.Name ?? ToSnakeCase(member.Name);
                var paramType = GetMemberType(member);
                var propSchema = BuildPropertySchema(paramType, paramAttr);

                properties[paramName] = propSchema;

                if (paramAttr?.Required == true)
                {
                    required.Add(paramName);
                }
            }

            schema["properties"] = properties;
            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        /// <summary>
        /// 型からスキーマを生成（出力用）
        /// </summary>
        private static Dictionary<string, object> BuildSchema(Type type)
        {
            if (type == null) return null;

            var schema = new Dictionary<string, object>
            {
                { "type", "object" }
            };

            var properties = new Dictionary<string, object>();
            var members = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Cast<MemberInfo>()
                .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Instance));

            foreach (var member in members)
            {
                var paramName = ToSnakeCase(member.Name);
                var paramType = GetMemberType(member);
                var propSchema = BuildPropertySchema(paramType, null);
                properties[paramName] = propSchema;
            }

            schema["properties"] = properties;
            return schema;
        }

        /// <summary>
        /// プロパティスキーマを生成
        /// </summary>
        private static Dictionary<string, object> BuildPropertySchema(Type type, ToolParameterAttribute attr)
        {
            return BuildPropertySchemaWithDepth(type, attr, 0, new HashSet<Type>());
        }

        /// <summary>
        /// 深度制限付きでプロパティスキーマを生成
        /// </summary>
        private const int MaxRecursionDepth = 3;

        private static Dictionary<string, object> BuildPropertySchemaWithDepth(
            Type type, ToolParameterAttribute attr, int depth, HashSet<Type> visitedTypes)
        {
            var schema = new Dictionary<string, object>();

            // Nullable対応
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            // 配列の場合
            if (underlyingType.IsArray || (underlyingType.IsGenericType && underlyingType.GetGenericTypeDefinition() == typeof(List<>)))
            {
                var elementType = underlyingType.IsArray
                    ? underlyingType.GetElementType()
                    : underlyingType.GetGenericArguments()[0];

                schema["type"] = "array";
                // 配列はラッパーに過ぎないため深度を増加させない（実際のネストは object のみ）
                schema["items"] = BuildPropertySchemaWithDepth(elementType, null, depth, visitedTypes);

                // 属性からの説明
                if (attr != null && !string.IsNullOrEmpty(attr.Description))
                    schema["description"] = attr.Description;

                return schema;
            }

            // Dictionary型の場合（任意のキー/値オブジェクト）
            if (underlyingType.IsGenericType && underlyingType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                schema["type"] = "object";
                schema["additionalProperties"] = true;
                if (attr != null && !string.IsNullOrEmpty(attr.Description))
                    schema["description"] = attr.Description;
                return schema;
            }

            // オブジェクト型の場合（プリミティブ以外）
            if (IsComplexType(underlyingType))
            {
                schema["type"] = "object";

                // 再帰深度チェックと循環参照チェック
                if (depth >= MaxRecursionDepth || visitedTypes.Contains(underlyingType))
                {
                    // 属性からの説明
                    if (attr != null && !string.IsNullOrEmpty(attr.Description))
                        schema["description"] = attr.Description;
                    return schema;
                }

                // 処理済みとしてマーク
                var newVisited = new HashSet<Type>(visitedTypes) { underlyingType };

                var properties = new Dictionary<string, object>();
                var members = underlyingType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Cast<MemberInfo>()
                    .Concat(underlyingType.GetProperties(BindingFlags.Public | BindingFlags.Instance));

                foreach (var member in members)
                {
                    var memberAttr = member.GetCustomAttribute<ToolParameterAttribute>();
                    var paramName = memberAttr?.Name ?? ToSnakeCase(member.Name);
                    var paramType = GetMemberType(member);
                    properties[paramName] = BuildPropertySchemaWithDepth(paramType, memberAttr, depth + 1, newVisited);
                }

                if (properties.Count > 0)
                {
                    schema["properties"] = properties;
                }

                // 属性からの説明
                if (attr != null && !string.IsNullOrEmpty(attr.Description))
                    schema["description"] = attr.Description;

                return schema;
            }

            // プリミティブ型
            var (jsonType, format) = GetJsonType(underlyingType);
            schema["type"] = jsonType;
            if (format != null) schema["format"] = format;

            // 属性からの情報
            if (attr != null)
            {
                if (!string.IsNullOrEmpty(attr.Description))
                    schema["description"] = attr.Description;

                if (attr.Default != null)
                    schema["default"] = attr.Default;

                if (!string.IsNullOrEmpty(attr.Enum))
                {
                    schema["enum"] = attr.Enum.Split(',').Select(s => s.Trim()).ToList();
                }
            }

            return schema;
        }

        /// <summary>
        /// 複合型（オブジェクト）かどうかを判定
        /// </summary>
        private static bool IsComplexType(Type type)
        {
            if (type == null) return false;
            if (type.IsPrimitive) return false;
            if (type == typeof(string)) return false;
            if (type == typeof(decimal)) return false;
            if (type == typeof(DateTime)) return false;
            if (type.IsEnum) return false;
            if (type.IsArray) return false;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return false;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return false;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) return false;

            return type.IsClass || type.IsValueType;
        }

        /// <summary>
        /// C#型からJSON Schema型へのマッピング
        /// </summary>
        private static (string type, string format) GetJsonType(Type type)
        {
            // Nullable対応
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (underlyingType == typeof(string)) return ("string", null);
            if (underlyingType == typeof(bool)) return ("boolean", null);
            if (underlyingType == typeof(int) || underlyingType == typeof(long)) return ("integer", null);
            if (underlyingType == typeof(float) || underlyingType == typeof(double)) return ("number", null);
            if (underlyingType == typeof(DateTime)) return ("string", "date-time");
            if (underlyingType.IsEnum) return ("string", null);

            return ("object", null);
        }

        /// <summary>
        /// メンバーの型を取得
        /// </summary>
        private static Type GetMemberType(MemberInfo member)
        {
            return member switch
            {
                FieldInfo f => f.FieldType,
                PropertyInfo p => p.PropertyType,
                _ => typeof(object)
            };
        }

        private static string ToSnakeCase(string input)
            => ToolArgsParser.ToSnakeCase(input);
    }
}
