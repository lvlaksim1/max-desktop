using System.Globalization;
using System.Text.Json;

namespace MaxDesktop.Protocol;

/// <summary>Read-only MAX GET_HISTORY opcode 49; never invokes read-marker operations.</summary>
public static class MaxHistoryCodec
{
    public const ushort Opcode = 49;
    public const int PageSize = 30;
    public const int MaxPageSize = 100;

    public sealed record Request(
        long chatId, int forward, int backward, long backwardTime,
        long forwardTime, bool getChat, long from, bool getMessages, bool interactive)
    {
        public Dictionary<string, object?> ToNativePayload() => new()
        {
            ["chatId"] = chatId, ["forward"] = forward, ["backward"] = backward,
            ["backwardTime"] = backwardTime, ["forwardTime"] = forwardTime,
            ["getChat"] = getChat, ["from"] = from, ["getMessages"] = getMessages,
            ["interactive"] = interactive
        };
    }

    public static Request CreateRequest(long chatId, long from, int count)
    {
        if (chatId == 0) throw new ArgumentOutOfRangeException(nameof(chatId));
        if (count is < 1 or > MaxPageSize) throw new ArgumentOutOfRangeException(nameof(count));
        if (from <= 0) throw new ArgumentOutOfRangeException(nameof(from));
        return new Request(chatId, 0, count, 0, 0, false, from, true, false);
    }

    public static ChatHistoryPage DecodeNative(long chatId,
        Dictionary<string, object?> payload, int requestedCount)
    {
        if (!payload.TryGetValue("messages", out var raw) || raw is not object?[] items)
            throw new InvalidDataException("MAX не вернул массив сообщений.");
        if (items.Length > 1000) throw new InvalidDataException("Слишком большой ответ истории.");
        var messages = new List<ChatMessage>(items.Length);
        foreach (var entry in items)
        {
            if (entry is not Dictionary<string, object?> item ||
                !Number(item.GetValueOrDefault("id"), out long id) ||
                !Number(item.GetValueOrDefault("time"), out long time))
                throw new InvalidDataException("В истории MAX отсутствует ID или время сообщения.");
            var sender = Sender(item.GetValueOrDefault("sender"));
            string text = item.GetValueOrDefault("text") as string ?? "";
            bool attachments = item.GetValueOrDefault("attaches") is object?[] a && a.Length > 0;
            messages.Add(Message(chatId, id, time, sender, text, attachments));
        }
        return Page(messages, items.Length, requestedCount);
    }

    public static ChatHistoryPage DecodeWeb(long chatId, JsonElement payload, int requestedCount)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("messages", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("MAX не вернул массив сообщений.");
        if (items.GetArrayLength() > 1000) throw new InvalidDataException("Слишком большой ответ истории.");
        var messages = new List<ChatMessage>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idValue) || !Number(idValue, out long id) ||
                !item.TryGetProperty("time", out var timeValue) || !Number(timeValue, out long time))
                throw new InvalidDataException("В истории MAX отсутствует ID или время сообщения.");
            string sender = item.TryGetProperty("sender", out var who) ? Sender(who) : "MAX";
            string text = item.TryGetProperty("text", out var raw) &&
                raw.ValueKind == JsonValueKind.String ? raw.GetString() ?? "" : "";
            bool attachments = item.TryGetProperty("attaches", out var attaches) &&
                attaches.ValueKind == JsonValueKind.Array && attaches.GetArrayLength() > 0;
            messages.Add(Message(chatId, id, time, sender, text, attachments));
        }
        return Page(messages, items.GetArrayLength(), requestedCount);
    }

    private static ChatHistoryPage Page(List<ChatMessage> messages, int rawCount, int requested)
    {
        // Sort oldest to newest for the UI; the next cursor remains an
        // inclusive timestamp. The UI deduplicates border messages by ID.
        messages.Sort((a, b) =>
        {
            int c = a.SentAt.CompareTo(b.SentAt);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });
        long? oldest = messages.Count > 0 ? messages[0].SentAt.ToUnixTimeMilliseconds() : null;
        return new ChatHistoryPage(messages, oldest, rawCount >= requested && oldest.HasValue);
    }

    private static ChatMessage Message(long chatId, long id, long time,
        string sender, string text, bool attachments)
    {
        DateTimeOffset when;
        try { when = DateTimeOffset.FromUnixTimeMilliseconds(time); }
        catch (ArgumentOutOfRangeException)
        { throw new InvalidDataException("MAX вернул недопустимое время сообщения."); }
        if (text.Length == 0)
            text = attachments ? "[Вложение — просмотр пока недоступен]" : "[Сообщение без текста]";
        return new ChatMessage(id, chatId, sender, text, when);
    }

    private static string Sender(object? obj) => obj switch
    {
        long n => n.ToString(CultureInfo.InvariantCulture),
        int n => n.ToString(CultureInfo.InvariantCulture),
        string s when !string.IsNullOrWhiteSpace(s) => s,
        Dictionary<string, object?> m when Number(m.GetValueOrDefault("id"), out long n)
            => n.ToString(CultureInfo.InvariantCulture),
        _ => "MAX"
    };
    private static string Sender(JsonElement obj) => obj.ValueKind switch
    {
        JsonValueKind.String => obj.GetString() ?? "MAX",
        JsonValueKind.Number when Number(obj, out long n)
            => n.ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Object when obj.TryGetProperty("id", out var id) &&
            Number(id, out long n) => n.ToString(CultureInfo.InvariantCulture),
        _ => "MAX"
    };
    private static bool Number(object? value, out long number)
    {
        number = 0;
        return value switch
        {
            long n => (number = n) == n,
            int n => (number = n) == n,
            string s => long.TryParse(s, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out number),
            _ => false
        };
    }
    private static bool Number(JsonElement value, out long number)
    {
        number = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out number),
            JsonValueKind.String => long.TryParse(value.GetString(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out number),
            _ => false
        };
    }
}
