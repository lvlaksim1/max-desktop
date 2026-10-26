using System.Globalization;
using System.Text.Json;

namespace MaxDesktop.Protocol;

/// <summary>Decodes GET_CHATS (53) responses. Missing data is an error, not an empty list.</summary>
public static class MaxChatListCodec
{
    public const int Opcode = 53;
    public const int PageSize = 100;
    public const int MaxPages = 20;
    public sealed record Page(IReadOnlyList<ChatSummary> Chats, long? Marker);

    public static Page DecodeNative(Dictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("chats", out var raw) || raw is not object?[] items)
            throw new InvalidDataException("MAX не вернул массив чатов.");
        var result = new List<ChatSummary>();
        foreach (var entry in items)
        {
            if (entry is not Dictionary<string, object?> item ||
                !Long(item.GetValueOrDefault("id"), out long id))
                throw new InvalidDataException("MAX вернул некорректный идентификатор чата.");
            string title = item.GetValueOrDefault("title") as string ?? "";
            string preview = item.GetValueOrDefault("lastMessage") is Dictionary<string, object?> last
                ? last.GetValueOrDefault("text") as string ?? "" : "";
            int unread = Long(item.GetValueOrDefault("newMessages"), out long n) && n > 0
                ? (int)Math.Min(n, int.MaxValue) : 0;
            result.Add(new ChatSummary(id, Title(title, id), preview, unread));
        }
        long? marker = Long(payload.GetValueOrDefault("marker"), out long next) ? next : null;
        return new Page(result, marker);
    }

    public static Page DecodeWeb(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("chats", out var chats) ||
            chats.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MAX не вернул массив чатов.");
        var result = new List<ChatSummary>();
        foreach (var chat in chats.EnumerateArray())
        {
            if (chat.ValueKind != JsonValueKind.Object ||
                !chat.TryGetProperty("id", out var raw) || !Long(raw, out long id))
                throw new InvalidDataException("MAX вернул некорректный идентификатор чата.");
            string title = Text(chat, "title") ?? "";
            string preview = chat.TryGetProperty("lastMessage", out var last) &&
                last.ValueKind == JsonValueKind.Object ? Text(last, "text") ?? "" : "";
            int unread = chat.TryGetProperty("newMessages", out var count) &&
                Long(count, out long n) && n > 0 ? (int)Math.Min(n, int.MaxValue) : 0;
            result.Add(new ChatSummary(id, Title(title, id), preview, unread));
        }
        long? marker = payload.TryGetProperty("marker", out var next) &&
            Long(next, out long m) ? m : null;
        return new Page(result, marker);
    }

    private static string Title(string name, long id) =>
        string.IsNullOrWhiteSpace(name) ? $"Чат {id}" : name;
    private static string? Text(JsonElement e, string key) =>
        e.TryGetProperty(key, out var raw) && raw.ValueKind == JsonValueKind.String
            ? raw.GetString() : null;
    private static bool Long(object? input, out long output)
    {
        output = 0;
        try
        {
            if (input is null) return false;
            if (input is not (long or int or uint or ulong or string)) return false;
            output = Convert.ToInt64(input, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception e) when (e is FormatException or OverflowException) { return false; }
    }
    private static bool Long(JsonElement input, out long output)
    {
        output = 0;
        return input.ValueKind switch
        {
            JsonValueKind.Number => input.TryGetInt64(out output),
            JsonValueKind.String => long.TryParse(input.GetString(), out output),
            _ => false
        };
    }
}
