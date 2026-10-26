using System.Text.Json;

namespace MaxDesktop.Protocol;

/// <summary>MAX web protocol version 11: JSON WebSocket frames, not TCP MessagePack.</summary>
public static class MaxWebSocketCodec
{
    public const int Version = 11;
    public const string Endpoint = "wss://ws-api.oneme.ru/websocket";
    public const string Origin = "https://web.max.ru";

    public static string Encode(int sequence, int opcode, object payload) =>
        JsonSerializer.Serialize(new
        {
            ver = Version,
            cmd = 0,
            seq = sequence,
            opcode,
            payload
        });

    public static MaxWebSocketFrame Decode(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("ver", out var version) ||
            !version.TryGetInt32(out var v) || v != Version ||
            !root.TryGetProperty("seq", out var sequence) ||
            !sequence.TryGetInt32(out var seq) ||
            !root.TryGetProperty("opcode", out var opcode) ||
            !opcode.TryGetInt32(out var op) ||
            !root.TryGetProperty("cmd", out var command) ||
            !command.TryGetInt32(out var cmd))
            throw new InvalidDataException("MAX WebSocket вернул неверный заголовок пакета.");

        var payload = root.TryGetProperty("payload", out var element)
            ? element.Clone() : default;
        return new MaxWebSocketFrame(v, cmd, seq, op, payload);
    }
}

public sealed record MaxWebSocketFrame(
    int Version, int Command, int Sequence, int Opcode, JsonElement Payload);
