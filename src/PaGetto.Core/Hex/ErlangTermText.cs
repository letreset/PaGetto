using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PaGetto.Core.Hex;

/// <summary>
/// Reads Erlang terms written as text, each followed by a dot, as in the <c>metadata.config</c> of
/// a Hex package. Only the terms Hex writes there are read: binaries, strings, integers, atoms,
/// lists and tuples.
/// </summary>
/// <remarks>
/// A binary or a string is read as a <see cref="string"/> (binaries as UTF-8), an integer as a
/// <see cref="long"/>, the atoms <c>true</c> and <c>false</c> as a <see cref="bool"/> and other
/// atoms as their name, a list as a <see cref="List{T}"/> and a tuple as an <see cref="object"/> array.
/// </remarks>
public sealed class ErlangTermText
{
    // Deeper nesting than Hex writes is rejected before it could exhaust the stack.
    private const int MaxDepth = 32;

    private static readonly UTF8Encoding _strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _text;
    private int _position;

    private ErlangTermText(string text)
    {
        _text = text;
    }

    /// <summary>
    /// Reads all terms of a file. The file is UTF-8, or Latin-1 when it is not valid UTF-8, as
    /// Hex reads it.
    /// </summary>
    /// <exception cref="FormatException">The content is not a sequence of terms Hex writes.</exception>
    public static IReadOnlyList<object> Parse(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string text;
        try
        {
            text = _strictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(content);
        }

        return new ErlangTermText(text).ReadTerms();
    }

    private List<object> ReadTerms()
    {
        var terms = new List<object>();
        while (true)
        {
            SkipWhitespace();
            if (_position == _text.Length)
            {
                return terms;
            }

            terms.Add(ReadTerm(0));
            Expect('.');
        }
    }

    private object ReadTerm(int depth)
    {
        if (depth > MaxDepth)
        {
            throw Error("The terms are nested too deeply.");
        }

        SkipWhitespace();
        if (_position == _text.Length)
        {
            throw Error("The terms end unexpectedly.");
        }

        var c = _text[_position];
        if (c == '<' && Peek(1) == '<')
        {
            _position += 2;
            return ReadBinary();
        }

        switch (c)
        {
            case '"':
                return ReadQuoted('"');

            case '\'':
                return ReadAtom(ReadQuoted('\''));

            case '[':
                _position++;
                return ReadElements(']', depth);

            case '{':
                _position++;
                return ReadElements('}', depth).ToArray();
        }

        if (c is '+' or '-' || char.IsAsciiDigit(c))
        {
            return ReadInteger();
        }

        if (char.IsAsciiLetterLower(c))
        {
            var start = _position;
            while (_position < _text.Length && (char.IsAsciiLetterOrDigit(_text[_position]) || _text[_position] is '_' or '@'))
            {
                _position++;
            }

            return ReadAtom(_text[start.._position]);
        }

        throw Error($"Unexpected character '{c}'.");
    }

    private List<object> ReadElements(char end, int depth)
    {
        var elements = new List<object>();

        SkipWhitespace();
        if (Peek(0) == end)
        {
            _position++;
            return elements;
        }

        while (true)
        {
            elements.Add(ReadTerm(depth + 1));

            SkipWhitespace();
            if (Peek(0) == ',')
            {
                _position++;
                continue;
            }

            Expect(end);
            return elements;
        }
    }

    /// <summary>
    /// Reads a binary after its <c>&lt;&lt;</c>: segments that are strings, UTF-8 strings or bytes.
    /// </summary>
    private string ReadBinary()
    {
        using var bytes = new MemoryStream();

        SkipWhitespace();
        if (Peek(0) == '>' && Peek(1) == '>')
        {
            _position += 2;
            return string.Empty;
        }

        while (true)
        {
            SkipWhitespace();
            if (Peek(0) == '"')
            {
                var text = ReadQuoted('"');

                SkipWhitespace();
                if (Peek(0) == '/')
                {
                    _position++;
                    SkipWhitespace();
                    if (string.CompareOrdinal(_text, _position, "utf8", 0, 4) != 0)
                    {
                        throw Error("Only the utf8 type of a binary segment is read.");
                    }

                    _position += 4;
                    var utf8 = Encoding.UTF8.GetBytes(text);
                    bytes.Write(utf8);
                }
                else
                {
                    // Without a type, each character is one byte.
                    foreach (var c in text)
                    {
                        if (c > 0xFF)
                        {
                            throw Error("A binary segment has a character that is not a byte.");
                        }

                        bytes.WriteByte((byte)c);
                    }
                }
            }
            else
            {
                var value = ReadInteger();
                if (value is < 0 or > 255)
                {
                    throw Error("A binary segment is not a byte.");
                }

                bytes.WriteByte((byte)value);
            }

            SkipWhitespace();
            if (Peek(0) == ',')
            {
                _position++;
                continue;
            }

            if (Peek(0) != '>' || Peek(1) != '>')
            {
                throw Error("A binary is not closed.");
            }

            _position += 2;

            try
            {
                return _strictUtf8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            }
            catch (DecoderFallbackException)
            {
                throw Error("A binary is not UTF-8 text.");
            }
        }
    }

    private long ReadInteger()
    {
        var start = _position;
        if (_text[_position] is '+' or '-')
        {
            _position++;
        }

        var digits = _position;
        while (_position < _text.Length && char.IsAsciiDigit(_text[_position]))
        {
            _position++;
        }

        if (_position == digits
            || !long.TryParse(_text.AsSpan(start, _position - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            throw Error("An integer is not valid.");
        }

        return value;
    }

    /// <summary>
    /// Reads a quoted string or atom, with the escapes of Erlang.
    /// </summary>
    private string ReadQuoted(char quote)
    {
        _position++;
        var builder = new StringBuilder();

        while (true)
        {
            if (_position == _text.Length)
            {
                throw Error("A string is not closed.");
            }

            var c = _text[_position++];
            if (c == quote)
            {
                return builder.ToString();
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (_position == _text.Length)
            {
                throw Error("A string is not closed.");
            }

            c = _text[_position++];
            if (IsOctal(c) && IsOctal(Peek(0)) && IsOctal(Peek(1)))
            {
                builder.Append((char)(((c - '0') * 64) + ((_text[_position] - '0') * 8) + (_text[_position + 1] - '0')));
                _position += 2;
            }
            else if (c == '^' && _position < _text.Length)
            {
                builder.Append((char)(_text[_position++] & 31));
            }
            else if (c <= ' ')
            {
                // An escaped line break or space is left out.
            }
            else
            {
                builder.Append(c switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'v' => '\v',
                    'b' => '\b',
                    'f' => '\f',
                    'e' => '\u001b',
                    's' => ' ',
                    'd' => '\u007f',
                    _ => c,
                });
            }
        }
    }

    private static object ReadAtom(string name)
    {
        return name switch
        {
            "true" => true,
            "false" => false,
            _ => name,
        };
    }

    private void Expect(char c)
    {
        SkipWhitespace();
        if (Peek(0) != c)
        {
            throw Error($"Expected '{c}'.");
        }

        _position++;
    }

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _text.Length ? _text[index] : '\0';
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && _text[_position] <= ' ')
        {
            _position++;
        }
    }

    private static bool IsOctal(char c)
    {
        return c is >= '0' and <= '7';
    }

    private FormatException Error(string message)
    {
        return new FormatException($"{message} (at character {_position})");
    }
}
