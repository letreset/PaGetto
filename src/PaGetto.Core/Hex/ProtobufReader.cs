using System;
using System.Collections.Generic;
using System.Text;

namespace PaGetto.Core.Hex;

/// <summary>
/// Reads the fields of a protocol buffers message, front to back. Fields of every wire type can be
/// skipped; varints and length-delimited fields can be read.
/// See: https://protobuf.dev/programming-guides/encoding/
/// </summary>
internal sealed class ProtobufReader
{
    public const int VarintType = 0;
    public const int LengthDelimitedType = 2;

    private const int Fixed64Type = 1;
    private const int Fixed32Type = 5;

    private readonly byte[] _data;
    private int _position;

    public ProtobufReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public int Field { get; private set; }

    public int WireType { get; private set; }

    /// <summary>
    /// Moves to the next field.
    /// </summary>
    /// <returns>False at the end of the message.</returns>
    /// <exception cref="FormatException">The message is not valid.</exception>
    public bool Next()
    {
        if (_position == _data.Length)
        {
            return false;
        }

        var tag = ReadRawVarint();
        Field = (int)(tag >> 3);
        WireType = (int)(tag & 7);

        if (Field == 0)
        {
            throw new FormatException("A protocol buffers field has the number 0.");
        }

        return true;
    }

    public ulong ReadVarint()
    {
        Expect(VarintType);
        return ReadRawVarint();
    }

    public byte[] ReadBytes()
    {
        Expect(LengthDelimitedType);

        var length = ReadRawVarint();
        if (length > (ulong)(_data.Length - _position))
        {
            throw new FormatException("A protocol buffers message ends unexpectedly.");
        }

        var bytes = _data.AsSpan(_position, (int)length).ToArray();
        _position += (int)length;
        return bytes;
    }

    public string ReadString()
    {
        return Encoding.UTF8.GetString(ReadBytes());
    }

    /// <summary>
    /// Reads a repeated integer field, packed or not.
    /// </summary>
    public IEnumerable<ulong> ReadVarints()
    {
        if (WireType == VarintType)
        {
            return [ReadRawVarint()];
        }

        var packed = new ProtobufReader(ReadBytes());
        var values = new List<ulong>();
        while (packed._position < packed._data.Length)
        {
            values.Add(packed.ReadRawVarint());
        }

        return values;
    }

    public void Skip()
    {
        switch (WireType)
        {
            case VarintType:
                ReadRawVarint();
                break;

            case LengthDelimitedType:
                ReadBytes();
                break;

            case Fixed64Type:
                Advance(8);
                break;

            case Fixed32Type:
                Advance(4);
                break;

            default:
                throw new FormatException($"Protocol buffers wire type {WireType} is not read.");
        }
    }

    private void Expect(int wireType)
    {
        if (WireType != wireType)
        {
            throw new FormatException($"Protocol buffers field {Field} has wire type {WireType}, not {wireType}.");
        }
    }

    private void Advance(int count)
    {
        if (count > _data.Length - _position)
        {
            throw new FormatException("A protocol buffers message ends unexpectedly.");
        }

        _position += count;
    }

    private ulong ReadRawVarint()
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (_position == _data.Length)
            {
                throw new FormatException("A protocol buffers message ends unexpectedly.");
            }

            var b = _data[_position++];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
            {
                return value;
            }
        }

        throw new FormatException("A protocol buffers varint is too long.");
    }
}
