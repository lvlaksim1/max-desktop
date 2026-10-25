using System.Buffers.Binary;

namespace MaxDesktop.Protocol;

public sealed record MaxWireFrame(
    byte Version,
    byte Command,
    ushort Sequence,
    ushort Opcode,
    byte Flags,
    Dictionary<string, object?> Payload);

/// <summary>Native MAX TCP framing: 10-byte big-endian header + MessagePack body.</summary>
public static class MaxTcpFraming
{
    public const int HeaderSize = 10;
    public const byte NativeVersion = 10;

    public static byte[] Encode(ushort sequence, ushort opcode, Dictionary<string, object?> payload)
    {
        byte[] data = MaxMessagePack.Encode(payload);
        if (data.Length > MaxMessagePack.MaxPayloadBytes)
            throw new InvalidDataException("Слишком большой кадр MAX.");
        byte[] frame = new byte[HeaderSize + data.Length];
        frame[0] = NativeVersion;
        frame[1] = 0; // request
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), sequence);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), opcode);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(6, 4), (uint)data.Length);
        data.CopyTo(frame, HeaderSize);
        return frame;
    }

    public static MaxWireFrame Decode(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body)
    {
        if (header.Length != HeaderSize)
            throw new InvalidDataException("Размер заголовка TCP MAX должен быть 10 байт.");
        if (header[0] != NativeVersion)
            throw new InvalidDataException("Неподдерживаемая версия TCP MAX.");
        uint packed = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(6, 4));
        byte flag = (byte)(packed >> 24);
        int length = checked((int)(packed & 0x00ffffff));
        if (length != body.Length || length > MaxMessagePack.MaxPayloadBytes)
            throw new InvalidDataException("Длина TCP-пакета не соответствует заголовку.");
        byte[] decompressed = flag switch
        {
            0 => body.ToArray(),
            0xff => throw new NotSupportedException("Ответ MAX использует Zstandard. Требуется декодер Zstandard."),
            _ => MaxLz4.Decompress(body)
        };
        var payload = MaxMessagePack.Decode(decompressed) as Dictionary<string, object?>
            ?? throw new InvalidDataException("Ожидается карта полей MessagePack.");
        return new MaxWireFrame(
            header[0], header[1],
            BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(header.Slice(4, 2)),
            flag, payload);
    }

    public static int PayloadLength(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderSize)
            throw new InvalidDataException("Неполный заголовок TCP MAX.");
        int length = (int)(BinaryPrimitives.ReadUInt32BigEndian(header.Slice(6, 4)) & 0x00ffffff);
        if (length > MaxMessagePack.MaxPayloadBytes)
            throw new InvalidDataException("TCP MAX отправил слишком большой ответ.");
        return length;
    }
}

/// <summary>Minimal bounded LZ4 block decoder for compressed inbound MAX frames.</summary>
public static class MaxLz4
{
    public static byte[] Decompress(ReadOnlySpan<byte> source)
    {
        var data = new List<byte>(Math.Min(MaxMessagePack.MaxPayloadBytes, source.Length * 2));
        int pos = 0;
        while (pos < source.Length)
        {
            byte token = source[pos++];
            int literals = token >> 4;
            if (literals == 15) literals += ExtensionLength(source, ref pos);
            if (literals > source.Length - pos || literals > MaxMessagePack.MaxPayloadBytes - data.Count)
                throw new InvalidDataException("Повреждён блок LZ4: длина литералов.");
            for (int i = 0; i < literals; i++) data.Add(source[pos++]);
            if (pos == source.Length) break;
            if (source.Length - pos < 2)
                throw new InvalidDataException("Повреждён блок LZ4: смещение.");
            int offset = source[pos] | (source[pos + 1] << 8);
            pos += 2;
            if (offset == 0 || offset > data.Count)
                throw new InvalidDataException("Недопустимое смещение LZ4.");
            int length = (token & 15) + 4;
            if ((token & 15) == 15) length += ExtensionLength(source, ref pos);
            if (length > MaxMessagePack.MaxPayloadBytes - data.Count)
                throw new InvalidDataException("Распакованный блок LZ4 слишком большой.");
            for (int i = 0; i < length; i++)
                data.Add(data[data.Count - offset]);
        }
        return data.ToArray();
    }

    private static int ExtensionLength(ReadOnlySpan<byte> source, ref int pos)
    {
        int length = 0;
        byte next;
        do
        {
            if (pos >= source.Length) throw new InvalidDataException("Обрезанная длина блока LZ4.");
            next = source[pos++];
            checked { length += next; }
            if (length > MaxMessagePack.MaxPayloadBytes)
                throw new InvalidDataException("Слишком большая длина LZ4.");
        } while (next == byte.MaxValue);
        return length;
    }
}
