using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Backgammon.Conversation
{
    internal static class ConversationMiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var parser = new Parser(json);
            return parser.Parse();
        }

        public static string Serialize(object value, bool prettyPrint)
        {
            var builder = new StringBuilder();
            Writer.WriteValue(builder, value, prettyPrint, 0);
            return builder.ToString();
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json;
            }

            public object Parse()
            {
                var value = ParseValue();
                ConsumeWhitespace();
                if (index != json.Length)
                {
                    throw new FormatException("Unexpected trailing characters in JSON.");
                }

                return value;
            }

            private object ParseValue()
            {
                ConsumeWhitespace();
                if (index >= json.Length)
                {
                    throw new FormatException("Unexpected end of JSON.");
                }

                return json[index] switch
                {
                    '{' => ParseObject(),
                    '[' => ParseArray(),
                    '"' => ParseString(),
                    't' => ParseLiteral("true", true),
                    'f' => ParseLiteral("false", false),
                    'n' => ParseLiteral("null", null),
                    _ => ParseNumber()
                };
            }

            private Dictionary<string, object> ParseObject()
            {
                Expect('{');
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                ConsumeWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    var key = ParseString();
                    ConsumeWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    ConsumeWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private List<object> ParseArray()
            {
                Expect('[');
                var result = new List<object>();
                ConsumeWhitespace();
                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    ConsumeWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (index < json.Length)
                {
                    var current = json[index++];
                    if (current == '"')
                    {
                        return builder.ToString();
                    }

                    if (current != '\\')
                    {
                        builder.Append(current);
                        continue;
                    }

                    if (index >= json.Length)
                    {
                        throw new FormatException("Unexpected end of JSON string.");
                    }

                    var escaped = json[index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            throw new FormatException($"Unsupported escape sequence: \\{escaped}");
                    }
                }

                throw new FormatException("Unterminated JSON string.");
            }

            private char ParseUnicodeEscape()
            {
                if (index + 4 > json.Length)
                {
                    throw new FormatException("Incomplete unicode escape.");
                }

                var code = json.Substring(index, 4);
                index += 4;
                return (char)Convert.ToInt32(code, 16);
            }

            private object ParseLiteral(string literal, object value)
            {
                if (index + literal.Length > json.Length
                    || !string.Equals(json.Substring(index, literal.Length), literal, StringComparison.Ordinal))
                {
                    throw new FormatException($"Invalid JSON token at {index}.");
                }

                index += literal.Length;
                return value;
            }

            private object ParseNumber()
            {
                var start = index;
                if (json[index] == '-')
                {
                    index++;
                }

                while (index < json.Length && char.IsDigit(json[index]))
                {
                    index++;
                }

                var isDouble = false;
                if (index < json.Length && json[index] == '.')
                {
                    isDouble = true;
                    index++;
                    while (index < json.Length && char.IsDigit(json[index]))
                    {
                        index++;
                    }
                }

                if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
                {
                    isDouble = true;
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                    {
                        index++;
                    }

                    while (index < json.Length && char.IsDigit(json[index]))
                    {
                        index++;
                    }
                }

                var token = json.Substring(start, index - start);
                if (isDouble)
                {
                    return double.Parse(token, CultureInfo.InvariantCulture);
                }

                return long.Parse(token, CultureInfo.InvariantCulture);
            }

            private void ConsumeWhitespace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }
            }

            private bool TryConsume(char expected)
            {
                ConsumeWhitespace();
                if (index < json.Length && json[index] == expected)
                {
                    index++;
                    return true;
                }

                return false;
            }

            private void Expect(char expected)
            {
                ConsumeWhitespace();
                if (index >= json.Length || json[index] != expected)
                {
                    throw new FormatException($"Expected '{expected}' at position {index}.");
                }

                index++;
            }
        }

        private static class Writer
        {
            public static void WriteValue(StringBuilder builder, object value, bool prettyPrint, int depth)
            {
                switch (value)
                {
                    case null:
                        builder.Append("null");
                        return;
                    case bool boolValue:
                        builder.Append(boolValue ? "true" : "false");
                        return;
                    case int intValue:
                        builder.Append(intValue.ToString(CultureInfo.InvariantCulture));
                        return;
                    case long longValue:
                        builder.Append(longValue.ToString(CultureInfo.InvariantCulture));
                        return;
                    case double doubleValue:
                        builder.Append(doubleValue.ToString(CultureInfo.InvariantCulture));
                        return;
                    case string stringValue:
                        WriteString(builder, stringValue);
                        return;
                    case Dictionary<string, object> objectValue:
                        WriteObject(builder, objectValue, prettyPrint, depth);
                        return;
                    case List<object> listValue:
                        WriteArray(builder, listValue, prettyPrint, depth);
                        return;
                    case IEnumerable<string> stringEnumerable:
                    {
                        var items = new List<object>();
                        foreach (var item in stringEnumerable)
                        {
                            items.Add(item);
                        }

                        WriteArray(builder, items, prettyPrint, depth);
                        return;
                    }
                    default:
                        throw new InvalidOperationException($"Unsupported JSON value type: {value.GetType().Name}");
                }
            }

            private static void WriteObject(StringBuilder builder, Dictionary<string, object> value, bool prettyPrint, int depth)
            {
                builder.Append('{');
                if (value.Count == 0)
                {
                    builder.Append('}');
                    return;
                }

                var first = true;
                foreach (var pair in value)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    if (prettyPrint)
                    {
                        builder.AppendLine();
                        AppendIndent(builder, depth + 1);
                    }

                    WriteString(builder, pair.Key);
                    builder.Append(prettyPrint ? ": " : ":");
                    WriteValue(builder, pair.Value, prettyPrint, depth + 1);
                    first = false;
                }

                if (prettyPrint)
                {
                    builder.AppendLine();
                    AppendIndent(builder, depth);
                }

                builder.Append('}');
            }

            private static void WriteArray(StringBuilder builder, List<object> value, bool prettyPrint, int depth)
            {
                builder.Append('[');
                for (var i = 0; i < value.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    if (prettyPrint)
                    {
                        builder.AppendLine();
                        AppendIndent(builder, depth + 1);
                    }

                    WriteValue(builder, value[i], prettyPrint, depth + 1);
                }

                if (value.Count > 0 && prettyPrint)
                {
                    builder.AppendLine();
                    AppendIndent(builder, depth);
                }

                builder.Append(']');
            }

            private static void WriteString(StringBuilder builder, string value)
            {
                builder.Append('"');
                if (value != null)
                {
                    for (var i = 0; i < value.Length; i++)
                    {
                        switch (value[i])
                        {
                            case '"':
                                builder.Append("\\\"");
                                break;
                            case '\\':
                                builder.Append("\\\\");
                                break;
                            case '\b':
                                builder.Append("\\b");
                                break;
                            case '\f':
                                builder.Append("\\f");
                                break;
                            case '\n':
                                builder.Append("\\n");
                                break;
                            case '\r':
                                builder.Append("\\r");
                                break;
                            case '\t':
                                builder.Append("\\t");
                                break;
                            default:
                                builder.Append(value[i]);
                                break;
                        }
                    }
                }

                builder.Append('"');
            }

            private static void AppendIndent(StringBuilder builder, int depth)
            {
                builder.Append(' ', depth * 2);
            }
        }
    }
}
