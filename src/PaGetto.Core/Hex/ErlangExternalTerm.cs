using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PaGetto.Core.Hex;

/// <summary>
/// Writes and reads Erlang's external term format (<c>term_to_binary</c>), the
/// <c>application/vnd.hex+erlang</c> bodies of the Hex HTTP API.
/// See: https://www.erlang.org/doc/apps/erts/erl_ext_dist.html
/// </summary>
/// <remarks>
/// Values map like this: a <see cref="string"/> is a binary, a <see cref="bool"/> the atom
/// <c>true</c> or <c>false</c>, null the atom <c>nil</c>, an integer an integer, a dictionary with
/// string keys a map with binary keys, and any other sequence a list. When reading, atoms other
/// than those three are read as their name and a tuple as an <see cref="object"/> array.
/// </remarks>
public static class ErlangExternalTerm
{
    private const byte Version = 131;

    private const byte SmallIntegerExt = 97;
    private const byte IntegerExt = 98;
    private const byte AtomExt = 100;
    private const byte SmallTupleExt = 104;
    private const byte LargeTupleExt = 105;
    private const byte NilExt = 106;
    private const byte StringExt = 107;
    private const byte ListExt = 108;
    private const byte BinaryExt = 109;
    private const byte SmallBigExt = 110;
    private const byte SmallAtomExt = 115;
    private const byte MapExt = 116;
    private const byte AtomUtf8Ext = 118;
    private const byte SmallAtomUtf8Ext = 119;

    private const int MaxDepth = 32;

    public static byte[] Encode(object value)
    {
        using var output = new MemoryStream();
        output.WriteByte(Version);
        Write(output, value);

        return output.ToArray();
    }

    /// <summary>
    /// Reads a term.
    /// </summary>
    /// <exception cref="FormatException">The data is not a term this reader reads.</exception>
    public static object Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data[0] != Version)
        {
            throw new FormatException("The data is not in the external term format.");
        }

        var position = 1;
        var value = Read(data, ref position, 0);
        if (position != data.Length)
        {
            throw new FormatException("The data has bytes after its term.");
        }

        return value;
    }

    private static void Write(Stream output, object value)
    {
        switch (value)
        {
            case null:
                WriteAtom(output, "nil");
                break;

            case bool boolean:
                WriteAtom(output, boolean ? "true" : "false");
                break;

            case string text:
                var bytes = Encoding.UTF8.GetBytes(text);
                output.WriteByte(BinaryExt);
                WriteUInt32(output, (uint)bytes.Length);
                output.Write(bytes);
                break;

            case int integer:
                WriteInteger(output, integer);
                break;

            case long integer:
                WriteInteger(output, integer);
                break;

            case IEnumerable<KeyValuePair<string, object>> map:
                var pairs = new List<KeyValuePair<string, object>>(map);
                output.WriteByte(MapExt);
                WriteUInt32(output, (uint)pairs.Count);
                foreach (var pair in pairs)
                {
                    Write(output, pair.Key);
                    Write(output, pair.Value);
                }

                break;

            case IEnumerable list:
                var items = new List<object>();
                foreach (var item in list)
                {
                    items.Add(item);
                }

                if (items.Count > 0)
                {
                    output.WriteByte(ListExt);
                    WriteUInt32(output, (uint)items.Count);
                    foreach (var item in items)
                    {
                        Write(output, item);
                    }
                }

                // A proper list ends with the empty list.
                output.WriteByte(NilExt);
                break;

            default:
                throw new ArgumentException($"Values of type {value.GetType()} are not written.", nameof(value));
        }
    }

    private static void WriteAtom(Stream output, string name)
    {
        output.WriteByte(SmallAtomUtf8Ext);
        output.WriteByte((byte)name.Length);
        output.Write(Encoding.ASCII.GetBytes(name));
    }

    private static void WriteInteger(Stream output, long value)
    {
        if (value is >= 0 and <= 255)
        {
            output.WriteByte(SmallIntegerExt);
            output.WriteByte((byte)value);
        }
        else if (value is >= int.MinValue and <= int.MaxValue)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, (int)value);
            output.WriteByte(IntegerExt);
            output.Write(buffer);
        }
        else
        {
            // The magnitude, least significant byte first, after the sign.
            var magnitude = value < 0 ? (ulong)-(value + 1) + 1 : (ulong)value;
            var digits = new List<byte>();
            while (magnitude > 0)
            {
                digits.Add((byte)magnitude);
                magnitude >>= 8;
            }

            output.WriteByte(SmallBigExt);
            output.WriteByte((byte)digits.Count);
            output.WriteByte(value < 0 ? (byte)1 : (byte)0);
            output.Write(digits.ToArray());
        }
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        output.Write(buffer);
    }

    private static object Read(ReadOnlySpan<byte> data, ref int position, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new FormatException("The term is nested too deeply.");
        }

        var tag = ReadByte(data, ref position);
        switch (tag)
        {
            case SmallIntegerExt:
                return (long)ReadByte(data, ref position);

            case IntegerExt:
                return (long)BinaryPrimitives.ReadInt32BigEndian(Take(data, ref position, 4));

            case SmallBigExt:
                var count = ReadByte(data, ref position);
                var negative = ReadByte(data, ref position) != 0;
                var digits = Take(data, ref position, count);
                if (count > 8)
                {
                    throw new FormatException("An integer is too large.");
                }

                ulong magnitude = 0;
                for (var i = digits.Length - 1; i >= 0; i--)
                {
                    magnitude = (magnitude << 8) | digits[i];
                }

                if (magnitude > long.MaxValue)
                {
                    throw new FormatException("An integer is too large.");
                }

                return negative ? -(long)magnitude : (long)magnitude;

            case AtomExt:
                return ReadAtom(Encoding.Latin1.GetString(Take(data, ref position, ReadUInt16(data, ref position))));

            case SmallAtomExt:
                return ReadAtom(Encoding.Latin1.GetString(Take(data, ref position, ReadByte(data, ref position))));

            case AtomUtf8Ext:
                return ReadAtom(Encoding.UTF8.GetString(Take(data, ref position, ReadUInt16(data, ref position))));

            case SmallAtomUtf8Ext:
                return ReadAtom(Encoding.UTF8.GetString(Take(data, ref position, ReadByte(data, ref position))));

            case BinaryExt:
                return Encoding.UTF8.GetString(Take(data, ref position, ReadLength(data, ref position)));

            case StringExt:
                // A list of bytes, which term_to_binary writes for a charlist.
                return Encoding.Latin1.GetString(Take(data, ref position, ReadUInt16(data, ref position)));

            case NilExt:
                return new List<object>();

            case ListExt:
                var length = ReadLength(data, ref position);
                var list = new List<object>();
                for (var i = 0; i < length; i++)
                {
                    list.Add(Read(data, ref position, depth + 1));
                }

                if (ReadByte(data, ref position) != NilExt)
                {
                    throw new FormatException("Improper lists are not read.");
                }

                return list;

            case SmallTupleExt:
            case LargeTupleExt:
                var arity = tag == SmallTupleExt ? ReadByte(data, ref position) : ReadLength(data, ref position);
                var tuple = new object[arity];
                for (var i = 0; i < arity; i++)
                {
                    tuple[i] = Read(data, ref position, depth + 1);
                }

                return tuple;

            case MapExt:
                var size = ReadLength(data, ref position);
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                for (var i = 0; i < size; i++)
                {
                    if (Read(data, ref position, depth + 1) is not string key)
                    {
                        throw new FormatException("Only maps with binary or atom keys are read.");
                    }

                    map[key] = Read(data, ref position, depth + 1);
                }

                return map;

            default:
                throw new FormatException($"Terms with tag {tag} are not read.");
        }
    }

    private static object ReadAtom(string name)
    {
        return name switch
        {
            "true" => true,
            "false" => false,
            "nil" => null,
            _ => name,
        };
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int position)
    {
        return Take(data, ref position, 1)[0];
    }

    private static int ReadUInt16(ReadOnlySpan<byte> data, ref int position)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(Take(data, ref position, 2));
    }

    /// <summary>
    /// Reads a 32-bit count, which can be no larger than what is left of the data: every element
    /// takes at least a byte.
    /// </summary>
    private static int ReadLength(ReadOnlySpan<byte> data, ref int position)
    {
        var length = BinaryPrimitives.ReadUInt32BigEndian(Take(data, ref position, 4));
        if (length > (uint)(data.Length - position))
        {
            throw new FormatException("The term ends unexpectedly.");
        }

        return (int)length;
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int position, int count)
    {
        if (count > data.Length - position)
        {
            throw new FormatException("The term ends unexpectedly.");
        }

        var slice = data.Slice(position, count);
        position += count;
        return slice;
    }
}
