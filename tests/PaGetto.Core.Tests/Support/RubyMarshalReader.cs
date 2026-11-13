using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Reads the Ruby Marshal data the server writes, for tests: strings, symbols, integers, arrays,
/// hashes and objects. Objects come back as <see cref="RubyObject"/>.
/// </summary>
public sealed class RubyMarshalReader
{
    private readonly BinaryReader _reader;
    private readonly List<string> _symbols = [];

    private RubyMarshalReader(byte[] data)
    {
        _reader = new BinaryReader(new MemoryStream(data));
    }

    /// <summary>
    /// An object of a Ruby class: its <c>marshal_dump</c> or <c>_dump</c> (<see cref="Data"/>), or
    /// its instance variables.
    /// </summary>
    public sealed record RubyObject(char Type, string ClassName, object Data, IReadOnlyDictionary<string, object> InstanceVariables);

    /// <summary>
    /// A Ruby symbol, as opposed to a string.
    /// </summary>
    public sealed record Symbol(string Name);

    public static object Read(byte[] data)
    {
        var reader = new RubyMarshalReader(data);
        if (reader._reader.ReadByte() != 4 || reader._reader.ReadByte() != 8)
        {
            throw new InvalidDataException("Not Marshal 4.8 data.");
        }

        var value = reader.ReadValue();
        if (reader._reader.BaseStream.Position != data.Length)
        {
            throw new InvalidDataException("Data is left after the value.");
        }

        return value;
    }

    private object ReadValue()
    {
        var type = (char)_reader.ReadByte();
        switch (type)
        {
            case '0':
                return null;
            case 'T':
                return true;
            case 'F':
                return false;
            case 'i':
                return ReadLong();
            case ':':
            case ';':
                return new Symbol(ReadSymbolBody(type));
            case '"':
                return Encoding.UTF8.GetString(ReadBytes());
            case 'I':
                var value = ReadValue();
                var count = ReadLong();
                for (var i = 0; i < count; i++)
                {
                    ReadSymbol();
                    ReadValue();
                }

                return value;
            case '[':
                var items = new List<object>();
                for (int i = 0, length = ReadLong(); i < length; i++)
                {
                    items.Add(ReadValue());
                }

                return items;
            case '{':
                var hash = new Dictionary<object, object>();
                for (int i = 0, length = ReadLong(); i < length; i++)
                {
                    hash.Add(ReadValue(), ReadValue());
                }

                return hash;
            case 'U':
                return new RubyObject(type, ReadSymbol(), ReadValue(), null);
            case 'u':
                return new RubyObject(type, ReadSymbol(), ReadBytes(), null);
            case 'o':
                var className = ReadSymbol();
                var ivars = new Dictionary<string, object>();
                for (int i = 0, length = ReadLong(); i < length; i++)
                {
                    ivars.Add(ReadSymbol(), ReadValue());
                }

                return new RubyObject(type, className, null, ivars);
            default:
                throw new InvalidDataException($"Unexpected type '{type}'.");
        }
    }

    private string ReadSymbol()
    {
        return ReadSymbolBody((char)_reader.ReadByte());
    }

    private string ReadSymbolBody(char type)
    {
        if (type == ';')
        {
            return _symbols[ReadLong()];
        }

        if (type != ':')
        {
            throw new InvalidDataException($"Expected a symbol, not '{type}'.");
        }

        var name = Encoding.UTF8.GetString(ReadBytes());
        _symbols.Add(name);
        return name;
    }

    private byte[] ReadBytes()
    {
        return _reader.ReadBytes(ReadLong());
    }

    private int ReadLong()
    {
        var first = (sbyte)_reader.ReadByte();
        switch (first)
        {
            case 0:
                return 0;
            case > 4:
                return first - 5;
            case < -4:
                return first + 5;
        }

        var result = 0;
        var count = Math.Abs(first);
        for (var i = 0; i < count; i++)
        {
            result |= _reader.ReadByte() << (8 * i);
        }

        return first > 0 ? result : result - (1 << (8 * count));
    }
}
