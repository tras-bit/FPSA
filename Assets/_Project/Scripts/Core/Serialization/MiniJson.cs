using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kordon.Core.Serialization
{
    /// <summary>
    /// Minimal, dependency free JSON reader/writer used for every data driven
    /// config file in the project (weapon/magazine definitions, match rules,
    /// network regions, navigation graphs, settings, subtitles).
    ///
    /// It exists so <c>Kordon.Core</c> stays free of UnityEngine and of any
    /// third party package: the same parser runs inside the Unity editor, in a
    /// dedicated server build and in `dotnet test` on CI.
    ///
    /// Values are decoded into:
    ///   object  -> Dictionary&lt;string, object&gt;
    ///   array   -> List&lt;object&gt;
    ///   string  -> string
    ///   number  -> double (long when integral and it fits)
    ///   bool    -> bool
    ///   null    -> null
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            int index = 0;
            object value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            if (index != json.Length)
            {
                throw new FormatException($"Trailing content at position {index}.");
            }

            return value;
        }

        public static string Write(object value, bool pretty = true)
        {
            StringBuilder sb = new StringBuilder(pretty ? 512 : 256);
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- parse

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
            {
                throw new FormatException("Unexpected end of JSON.");
            }

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                {
                    throw new FormatException($"Expected object key at position {i}.");
                }

                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    throw new FormatException($"Expected ':' at position {i}.");
                }

                i++;
                result[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                {
                    throw new FormatException("Unterminated object.");
                }

                if (s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (s[i] == '}')
                {
                    i++;
                    return result;
                }

                throw new FormatException($"Expected ',' or '}}' at position {i}.");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> result = new List<object>();
            i++; // '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return result;
            }

            while (true)
            {
                result.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                {
                    throw new FormatException("Unterminated array.");
                }

                if (s[i] == ',')
                {
                    i++;
                    continue;
                }

                if (s[i] == ']')
                {
                    i++;
                    return result;
                }

                throw new FormatException($"Expected ',' or ']' at position {i}.");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // opening quote
            while (true)
            {
                if (i >= s.Length)
                {
                    throw new FormatException("Unterminated string.");
                }

                char c = s[i++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length)
                {
                    throw new FormatException("Unterminated escape sequence.");
                }

                char esc = s[i++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length)
                        {
                            throw new FormatException("Truncated \\u escape.");
                        }

                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default:
                        throw new FormatException($"Invalid escape '\\{esc}'.");
                }
            }
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            {
                i++;
            }

            bool isFloat = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    i++;
                }
                else if (c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+')
                {
                    isFloat |= c == '.' || c == 'e' || c == 'E';
                    i++;
                }
                else
                {
                    break;
                }
            }

            string raw = s.Substring(start, i - start);
            if (!isFloat && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long asLong))
            {
                return asLong;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double asDouble))
            {
                return asDouble;
            }

            throw new FormatException($"Invalid number '{raw}' at position {start}.");
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length ||
                string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
            {
                throw new FormatException($"Expected '{literal}' at position {i}.");
            }

            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
            {
                i++;
            }
        }

        // ---------------------------------------------------------------- write

        private static void WriteValue(StringBuilder sb, object value, bool pretty, int depth)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string str:
                    WriteString(sb, str);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case IDictionary<string, object> map:
                    WriteObject(sb, map, pretty, depth);
                    return;
                case System.Collections.IEnumerable list:
                    WriteArray(sb, list, pretty, depth);
                    return;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    return;
                default:
                    if (value is IFormattable formattable)
                    {
                        sb.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        WriteString(sb, value.ToString());
                    }

                    return;
            }
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> map, bool pretty, int depth)
        {
            sb.Append('{');
            if (map.Count == 0)
            {
                sb.Append('}');
                return;
            }

            bool first = true;
            foreach (KeyValuePair<string, object> pair in map)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                NewLine(sb, pretty, depth + 1);
                WriteString(sb, pair.Key);
                sb.Append(':');
                if (pretty)
                {
                    sb.Append(' ');
                }

                WriteValue(sb, pair.Value, pretty, depth + 1);
            }

            NewLine(sb, pretty, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, System.Collections.IEnumerable list, bool pretty, int depth)
        {
            sb.Append('[');
            bool first = true;
            bool empty = true;
            foreach (object item in list)
            {
                empty = false;
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                NewLine(sb, pretty, depth + 1);
                WriteValue(sb, item, pretty, depth + 1);
            }

            if (empty)
            {
                sb.Append(']');
                return;
            }

            NewLine(sb, pretty, depth);
            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }

        private static void NewLine(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty)
            {
                return;
            }

            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }
    }
}
