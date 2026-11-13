using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// Writes Ruby Marshal data (format 4.8), the little of it a gemspec needs: nil, booleans, small
/// integers, UTF-8 strings, symbols, arrays, hashes, plain objects and objects with their own
/// <c>marshal_dump</c> or <c>_dump</c>. Objects are written anew each time, never as links.
/// See: https://docs.ruby-lang.org/en/master/marshal_rdoc.html
/// </summary>
internal sealed class RubyMarshalWriter
{
    private readonly MemoryStream _output = new();

    // Symbols are written once, then referred to by their index.
    private readonly Dictionary<string, int> _symbols = new(StringComparer.Ordinal);

    public RubyMarshalWriter()
    {
        _output.WriteByte(4);
        _output.WriteByte(8);
    }

    public byte[] ToArray()
    {
        return _output.ToArray();
    }

    public void WriteNil()
    {
        _output.WriteByte((byte)'0');
    }

    public void WriteBoolean(bool value)
    {
        _output.WriteByte(value ? (byte)'T' : (byte)'F');
    }

    public void WriteInteger(int value)
    {
        _output.WriteByte((byte)'i');
        WriteLong(value);
    }

    /// <summary>
    /// A string, or nil for null. Strings are UTF-8, which Ruby keeps as the instance variable E.
    /// </summary>
    public void WriteString(string value)
    {
        if (value == null)
        {
            WriteNil();
            return;
        }

        _output.WriteByte((byte)'I');
        _output.WriteByte((byte)'"');
        WriteBytes(Encoding.UTF8.GetBytes(value));
        WriteLong(1);
        WriteSymbol("E");
        WriteBoolean(true);
    }

    public void WriteSymbol(string name)
    {
        if (_symbols.TryGetValue(name, out var index))
        {
            _output.WriteByte((byte)';');
            WriteLong(index);
            return;
        }

        _symbols[name] = _symbols.Count;
        _output.WriteByte((byte)':');
        WriteBytes(Encoding.UTF8.GetBytes(name));
    }

    /// <summary>
    /// Starts an array; its items follow.
    /// </summary>
    public void WriteArrayStart(int count)
    {
        _output.WriteByte((byte)'[');
        WriteLong(count);
    }

    /// <summary>
    /// Starts a hash; its keys and values follow, alternating.
    /// </summary>
    public void WriteHashStart(int count)
    {
        _output.WriteByte((byte)'{');
        WriteLong(count);
    }

    /// <summary>
    /// Starts an object of a class with <c>marshal_dump</c>; the dumped value follows.
    /// </summary>
    public void WriteUserMarshalStart(string className)
    {
        _output.WriteByte((byte)'U');
        WriteSymbol(className);
    }

    /// <summary>
    /// Starts an object of a class without its own dump; its instance variables follow, each a
    /// symbol (<see cref="WriteSymbol"/>) and a value.
    /// </summary>
    public void WriteObjectStart(string className, int instanceVariableCount)
    {
        _output.WriteByte((byte)'o');
        WriteSymbol(className);
        WriteLong(instanceVariableCount);
    }

    /// <summary>
    /// An object of a class with <c>_dump</c>, whose result is <paramref name="data"/>.
    /// </summary>
    public void WriteUserDefined(string className, byte[] data)
    {
        _output.WriteByte((byte)'u');
        WriteSymbol(className);
        WriteBytes(data);
    }

    private void WriteBytes(byte[] bytes)
    {
        WriteLong(bytes.Length);
        _output.Write(bytes);
    }

    /// <summary>
    /// Marshal's integer encoding: 0 is a zero byte, small numbers are one byte offset by 5, and
    /// others are a byte count followed by the number in little-endian order.
    /// </summary>
    private void WriteLong(int value)
    {
        switch (value)
        {
            case 0:
                _output.WriteByte(0);
                return;

            case > 0 and < 123:
                _output.WriteByte((byte)(value + 5));
                return;

            case < 0 and > -124:
                _output.WriteByte((byte)((value - 5) & 0xFF));
                return;
        }

        var bytes = new List<byte>();
        var remaining = value;
        var done = value > 0 ? 0 : -1;
        do
        {
            bytes.Add((byte)(remaining & 0xFF));
            remaining >>= 8;
        }
        while (remaining != done);

        _output.WriteByte((byte)(value > 0 ? bytes.Count : -bytes.Count));
        _output.Write(bytes.ToArray());
    }
}
