using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MaxDesktop.Protocol;

/// <summary>
/// Bounded MessagePack primitives used by the native MAX TCP protocol.
/// The implementation is deliberately independent of WPF and local sessions.
/// </summary>
public static class MaxMessagePack
{
    public const int MaxPayloadBytes = 5 * 1024 * 1024;

    public static byte[] Encode(object? value)
    {
        using var output = new MemoryStream();
        Write(output, value);
        if (output.Length > MaxPayloadBytes)
            throw new InvalidDataException("Слишком большой пакет MAX.");
        return output.ToArray();
    }

    public static object? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxPayloadBytes)
            throw new InvalidDataException("Пакет MAX превышает ограничение размера.");
        var reader = new Reader(bytes);
        object? value = reader.ReadValue(0);
        if (!reader.EndOfData)
            throw new InvalidDataException("После объекта MessagePack остались лишние данные.");
        return value;
    }

    private static void Write(Stream stream, object? value)
    {
        switch (value)
        {
            case null:
                stream.WriteByte(0xc0);
                return;
            case bool boolean:
                stream.WriteByte(boolean ? (byte)0xc3 : (byte)0xc2);
                return;
            case string text:
                WriteString(stream, text);
                return;
            case byte[] binary:
                WriteBinary(stream, binary);
                return;
            case int integer:
                WriteInteger(stream, integer);
                return;
            case long integer:
                WriteInteger(stream, integer);
                return;
            case uint integer:
                WriteUnsigned(stream, integer);
                return;
            case ulong integer:
                WriteUnsigned(stream, integer);
                return;
            case short integer:
                WriteInteger(stream, integer);
                return;
            case byte integer:
                WriteUnsigned(stream, integer);
                return;
            case double real:
                stream.WriteByte(0xcb);
                WriteU64(stream, (ulong)BitConverter.DoubleToInt64Bits(real));
                return;
            case IDictionary<string, object?> map:
                WriteMapHeader(stream, map.Count);
                foreach (var item in map)
                {
                    WriteString(stream, item.Key);
                    Write(stream, item.Value);
                }
                return;
            case IEnumerable<object?> list:
                var values = list.ToList();
                WriteArrayHeader(stream, values.Count);
                foreach (object? item in values) Write(stream, item);
                return;
            default:
                throw new NotSupportedException($"Неподдерживаемый тип MessagePack: {value.GetType().Name}");
        }
    }

    private static void WriteInteger(Stream s, long value)
    {
        if (value >= 0) { WriteUnsigned(s, (ulong)value); return; }
        if (value >= -32) { s.WriteByte((byte)(sbyte)value); return; }
        if (value >= sbyte.MinValue) { s.WriteByte(0xd0); s.WriteByte((byte)(sbyte)value); return; }
        if (value >= short.MinValue) { s.WriteByte(0xd1); WriteU16(s, unchecked((ushort)value)); return; }
        if (value >= int.MinValue) { s.WriteByte(0xd2); WriteU32(s, unchecked((uint)value)); return; }
        s.WriteByte(0xd3); WriteU64(s, unchecked((ulong)value));
    }

    private static void WriteUnsigned(Stream s, ulong value)
    {
        if (value <= 0x7f) { s.WriteByte((byte)value); return; }
        if (value <= byte.MaxValue) { s.WriteByte(0xcc); s.WriteByte((byte)value); return; }
        if (value <= ushort.MaxValue) { s.WriteByte(0xcd); WriteU16(s, (ushort)value); return; }
        if (value <= uint.MaxValue) { s.WriteByte(0xce); WriteU32(s, (uint)value); return; }
        s.WriteByte(0xcf); WriteU64(s, value);
    }

    private static void WriteString(Stream s, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= 31) s.WriteByte((byte)(0xa0 | bytes.Length));
        else if (bytes.Length <= byte.MaxValue) { s.WriteByte(0xd9); s.WriteByte((byte)bytes.Length); }
        else if (bytes.Length <= ushort.MaxValue) { s.WriteByte(0xda); WriteU16(s, (ushort)bytes.Length); }
        else { s.WriteByte(0xdb); WriteU32(s, checked((uint)bytes.Length)); }
        s.Write(bytes);
    }

    private static void WriteBinary(Stream s, byte[] value)
    {
        if (value.Length <= byte.MaxValue) { s.WriteByte(0xc4); s.WriteByte((byte)value.Length); }
        else if (value.Length <= ushort.MaxValue) { s.WriteByte(0xc5); WriteU16(s, (ushort)value.Length); }
        else { s.WriteByte(0xc6); WriteU32(s, checked((uint)value.Length)); }
        s.Write(value);
    }

    private static void WriteMapHeader(Stream s, int count)
    {
        if (count <= 15) s.WriteByte((byte)(0x80 | count));
        else if (count <= ushort.MaxValue) { s.WriteByte(0xde); WriteU16(s, (ushort)count); }
        else { s.WriteByte(0xdf); WriteU32(s, (uint)count); }
    }

    private static void WriteArrayHeader(Stream s, int count)
    {
        if (count <= 15) s.WriteByte((byte)(0x90 | count));
        else if (count <= ushort.MaxValue) { s.WriteByte(0xdc); WriteU16(s, (ushort)count); }
        else { s.WriteByte(0xdd); WriteU32(s, (uint)count); }
    }

    private static void WriteU16(Stream s, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        s.Write(bytes);
    }

    private static void WriteU32(Stream s, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        s.Write(bytes);
    }

    private static void WriteU64(Stream s, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        s.Write(bytes);
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _index;

        public Reader(ReadOnlySpan<byte> data) { _data = data; _index = 0; }
        public bool EndOfData => _index == _data.Length;

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _data.Length - _index)
                throw new InvalidDataException("Обрезанный объект MessagePack.");
            var result = _data.Slice(_index, count);
            _index += count;
            return result;
        }

        private byte Byte() => Take(1)[0];
        private ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        private uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4));
        private ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Take(8));
        private string Text(int length) => Encoding.UTF8.GetString(Take(length));
        private byte[] Binary(int length) => Take(length).ToArray();

        public object? ReadValue(int depth)
        {
            if (depth > 48) throw new InvalidDataException("Избыточная вложенность MessagePack.");
            byte code = Byte();
            if (code <= 0x7f) return (long)code;
            if ((code & 0xe0) == 0xe0) return (long)(sbyte)code;
            if ((code & 0xe0) == 0xa0) return Text(code & 0x1f);
            if ((code & 0xf0) == 0x80) return ReadMap(code & 0x0f, depth + 1);
            if ((code & 0xf0) == 0x90) return ReadArray(code & 0x0f, depth + 1);
            return code switch
            {
                0xc0 => null,
                0xc2 => false,
                0xc3 => true,
                0xc4 => Binary(Byte()),
                0xc5 => Binary(U16()),
                0xc6 => Binary(checked((int)U32())),
                0xc7 => ReadExt(Byte(), depth + 1),
                0xc8 => ReadExt(U16(), depth + 1),
                0xc9 => ReadExt(checked((int)U32()), depth + 1),
                0xca => (double)BitConverter.Int32BitsToSingle(unchecked((int)U32())),
                0xcb => BitConverter.Int64BitsToDouble(unchecked((long)U64())),
                0xcc => (long)Byte(),
                0xcd => (long)U16(),
                0xce => (long)U32(),
                0xcf => ReadUInt64(),
                0xd0 => (long)(sbyte)Byte(),
                0xd1 => (long)(short)U16(),
                0xd2 => (long)(int)U32(),
                0xd3 => unchecked((long)U64()),
                0xd4 => ReadExt(1, depth + 1),
                0xd5 => ReadExt(2, depth + 1),
                0xd6 => ReadExt(4, depth + 1),
                0xd7 => ReadExt(8, depth + 1),
                0xd8 => ReadExt(16, depth + 1),
                0xd9 => Text(Byte()),
                0xda => Text(U16()),
                0xdb => Text(checked((int)U32())),
                0xdc => ReadArray(U16(), depth + 1),
                0xdd => ReadArray(checked((int)U32()), depth + 1),
                0xde => ReadMap(U16(), depth + 1),
                0xdf => ReadMap(checked((int)U32()), depth + 1),
                _ => throw new InvalidDataException($"Неизвестный тип MessagePack: 0x{code:x2}")
            };
        }

        private object ReadUInt64()
        {
            ulong n = U64();
            return n <= long.MaxValue ? (object)(long)n : n;
        }

        private object? ReadExt(int length, int depth)
        {
            sbyte type = (sbyte)Byte();
            byte[] data = Binary(length);
            // MAX wraps some individual MessagePack values in extension type 1.
            return type == 1 ? MaxMessagePack.Decode(data) : data;
        }

        private object?[] ReadArray(int length, int depth)
        {
            CheckContainerLength(length);
            var list = new object?[length];
            for (int i = 0; i < length; i++) list[i] = ReadValue(depth);
            return list;
        }

        private Dictionary<string, object?> ReadMap(int length, int depth)
        {
            CheckContainerLength(length);
            var map = new Dictionary<string, object?>(length, StringComparer.Ordinal);
            for (int i = 0; i < length; i++)
            {
                object? key = ReadValue(depth);
                string name = key switch
                {
                    string s => s,
                    byte[] bytes => Encoding.UTF8.GetString(bytes),
                    null => "",
                    _ => Convert.ToString(key, CultureInfo.InvariantCulture) ?? ""
                };
                map[name] = ReadValue(depth);
            }
            return map;
        }

        private void CheckContainerLength(int length)
        {
            if (length < 0 || length > MaxPayloadBytes || length > _data.Length)
                throw new InvalidDataException("Недопустимый размер контейнера MessagePack.");
        }
    }
}
