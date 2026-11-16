using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PaGetto.Core.Hex;

/// <summary>
/// Writes the fields of a protocol buffers message, in the order they are written. Only the wire
/// types of the Hex registry are written: varints and length-delimited fields.
/// See: https://protobuf.dev/programming-guides/encoding/
/// </summary>
internal sealed class ProtobufWriter
{
    private const int VarintType = 0;
    private const int LengthDelimitedType = 2;

    private readonly MemoryStream _output = new();

    public void WriteVarint(int field, ulong value)
    {
        WriteRawVarint((ulong)field << 3 | VarintType);
        WriteRawVarint(value);
    }

    public void WriteBool(int field, bool value)
    {
        WriteVarint(field, value ? 1UL : 0UL);
    }

    public void WriteString(int field, string value)
    {
        WriteBytes(field, Encoding.UTF8.GetBytes(value));
    }

    public void WriteBytes(int field, byte[] value)
    {
        WriteRawVarint((ulong)field << 3 | LengthDelimitedType);
        WriteRawVarint((ulong)value.Length);
        _output.Write(value);
    }

    public void WriteMessage(int field, ProtobufWriter message)
    {
        WriteBytes(field, message.ToArray());
    }

    /// <summary>
    /// Writes a packed repeated field of non-negative integers. An empty field is left out.
    /// </summary>
    public void WritePacked(int field, IReadOnlyCollection<int> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        var packed = new ProtobufWriter();
        foreach (var value in values)
        {
            packed.WriteRawVarint((ulong)value);
        }

        WriteBytes(field, packed.ToArray());
    }

    public byte[] ToArray()
    {
        return _output.ToArray();
    }

    private void WriteRawVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        _output.WriteByte((byte)value);
    }
}
