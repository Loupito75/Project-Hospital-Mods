using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HospitalModUpdateChecker
{
    internal sealed class SimpleJsonParser
    {
        private const int MaximumDepth = 32;

        private readonly string _text;
        private int _index;

        private SimpleJsonParser(string text)
        {
            _text = text ?? string.Empty;
        }

        internal static bool TryParse(string text, out object value, out string error)
        {
            value = null;
            error = null;

            try
            {
                SimpleJsonParser parser = new SimpleJsonParser(text);
                parser.SkipWhitespace();
                value = parser.ParseValue(0);
                parser.SkipWhitespace();

                if (!parser.IsEnd)
                {
                    throw parser.Error("Unexpected trailing content.");
                }

                return true;
            }
            catch (FormatException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private bool IsEnd
        {
            get { return _index >= _text.Length; }
        }

        private object ParseValue(int depth)
        {
            SkipWhitespace();

            if (IsEnd)
            {
                throw Error("Unexpected end of JSON.");
            }

            char c = _text[_index];
            if (c == '{')
            {
                if (depth >= MaximumDepth)
                {
                    throw Error("Maximum JSON nesting depth exceeded.");
                }

                return ParseObject(depth + 1);
            }

            if (c == '[')
            {
                if (depth >= MaximumDepth)
                {
                    throw Error("Maximum JSON nesting depth exceeded.");
                }

                return ParseArray(depth + 1);
            }

            if (c == '"')
            {
                return ParseString();
            }

            if (c == 't')
            {
                ReadLiteral("true");
                return true;
            }

            if (c == 'f')
            {
                ReadLiteral("false");
                return false;
            }

            if (c == 'n')
            {
                ReadLiteral("null");
                return null;
            }

            if (c == '-' || char.IsDigit(c))
            {
                return ParseNumber();
            }

            throw Error("Unexpected character '" + c + "'.");
        }

        private Dictionary<string, object> ParseObject(int depth)
        {
            Dictionary<string, object> result =
                new Dictionary<string, object>(StringComparer.Ordinal);

            Expect('{');
            SkipWhitespace();

            if (TryConsume('}'))
            {
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (IsEnd || _text[_index] != '"')
                {
                    throw Error("JSON object property name must be a string.");
                }

                string key = ParseString();

                SkipWhitespace();
                Expect(':');

                object value = ParseValue(depth);
                if (result.ContainsKey(key))
                {
                    throw Error("Duplicate JSON property '" + key + "'.");
                }

                result.Add(key, value);

                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                Expect(',');
            }
        }

        private List<object> ParseArray(int depth)
        {
            List<object> result = new List<object>();

            Expect('[');
            SkipWhitespace();

            if (TryConsume(']'))
            {
                return result;
            }

            while (true)
            {
                result.Add(ParseValue(depth));

                SkipWhitespace();
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

            StringBuilder builder = new StringBuilder();

            while (!IsEnd)
            {
                char c = _text[_index++];

                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c == '\\')
                {
                    if (IsEnd)
                    {
                        throw Error("Incomplete JSON escape sequence.");
                    }

                    char escaped = _text[_index++];
                    switch (escaped)
                    {
                        case '"':
                            builder.Append('"');
                            break;
                        case '\\':
                            builder.Append('\\');
                            break;
                        case '/':
                            builder.Append('/');
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
                            throw Error("Invalid JSON escape sequence.");
                    }

                    continue;
                }

                if (c < 0x20)
                {
                    throw Error("Control character is not allowed in a JSON string.");
                }

                builder.Append(c);
            }

            throw Error("Unterminated JSON string.");
        }

        private char ParseUnicodeEscape()
        {
            if (_index + 4 > _text.Length)
            {
                throw Error("Incomplete Unicode escape sequence.");
            }

            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                char c = _text[_index++];
                int digit;

                if (c >= '0' && c <= '9')
                {
                    digit = c - '0';
                }
                else if (c >= 'a' && c <= 'f')
                {
                    digit = c - 'a' + 10;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    digit = c - 'A' + 10;
                }
                else
                {
                    throw Error("Invalid Unicode escape sequence.");
                }

                value = (value * 16) + digit;
            }

            return (char)value;
        }

        private object ParseNumber()
        {
            int start = _index;

            if (_text[_index] == '-')
            {
                _index++;
            }

            if (IsEnd || !char.IsDigit(_text[_index]))
            {
                throw Error("Invalid JSON number.");
            }

            if (_text[_index] == '0')
            {
                _index++;
            }
            else
            {
                while (!IsEnd && char.IsDigit(_text[_index]))
                {
                    _index++;
                }
            }

            bool floatingPoint = false;

            if (!IsEnd && _text[_index] == '.')
            {
                floatingPoint = true;
                _index++;

                if (IsEnd || !char.IsDigit(_text[_index]))
                {
                    throw Error("Invalid JSON number.");
                }

                while (!IsEnd && char.IsDigit(_text[_index]))
                {
                    _index++;
                }
            }

            if (!IsEnd && (_text[_index] == 'e' || _text[_index] == 'E'))
            {
                floatingPoint = true;
                _index++;

                if (!IsEnd && (_text[_index] == '+' || _text[_index] == '-'))
                {
                    _index++;
                }

                if (IsEnd || !char.IsDigit(_text[_index]))
                {
                    throw Error("Invalid JSON exponent.");
                }

                while (!IsEnd && char.IsDigit(_text[_index]))
                {
                    _index++;
                }
            }

            string valueText = _text.Substring(start, _index - start);

            if (!floatingPoint)
            {
                long integerValue;
                if (long.TryParse(
                    valueText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out integerValue))
                {
                    return integerValue;
                }
            }

            double floatingValue;
            if (double.TryParse(
                valueText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out floatingValue))
            {
                return floatingValue;
            }

            throw Error("Invalid JSON number.");
        }

        private void ReadLiteral(string literal)
        {
            if (_index + literal.Length > _text.Length ||
                string.CompareOrdinal(_text, _index, literal, 0, literal.Length) != 0)
            {
                throw Error("Invalid JSON literal.");
            }

            _index += literal.Length;
        }

        private void SkipWhitespace()
        {
            while (!IsEnd)
            {
                char c = _text[_index];
                if (c != ' ' &&
                    c != '\t' &&
                    c != '\r' &&
                    c != '\n')
                {
                    break;
                }

                _index++;
            }
        }

        private void Expect(char expected)
        {
            SkipWhitespace();

            if (IsEnd || _text[_index] != expected)
            {
                throw Error("Expected '" + expected + "'.");
            }

            _index++;
        }

        private bool TryConsume(char expected)
        {
            SkipWhitespace();

            if (!IsEnd && _text[_index] == expected)
            {
                _index++;
                return true;
            }

            return false;
        }

        private FormatException Error(string message)
        {
            return new FormatException(
                message + " Position " +
                _index.ToString(CultureInfo.InvariantCulture) + ".");
        }
    }
}
