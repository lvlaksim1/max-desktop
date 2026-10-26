using System.Security.Cryptography;
using System.Text;
using MaxDesktop.Protocol;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("Protocol smoke test failed: " + description);
}

// Fail closed before the actual TCP protocol and user credentials are involved.
var fallback = new UnavailableMaxTransport();
Check(fallback.State == ConnectionState.Disconnected, "fallback initial state");
var blocked = await fallback.BeginLoginAsync("+70000000000");
Check(blocked.State == ConnectionState.Failed && !string.IsNullOrEmpty(blocked.Error), "blocked authorization");
Check((await fallback.GetChatsAsync()).Count == 0, "no fabricated chats");
try
{
    await fallback.SendMessageAsync(1, "test");
    throw new Exception("Unverified send accepted.");
}
catch (NotSupportedException) { }

// Synthetic test data only: no account, chats, or server requests.
var chatsPayload = new Dictionary<string, object?>
{
    ["marker"] = 1700000000000L,
    ["chats"] = new object?[]
    {
        new Dictionary<string, object?>
        {
            ["id"] = -123L,
            ["title"] = "Тестовый канал",
            ["newMessages"] = 3,
            ["lastMessage"] = new Dictionary<string, object?> { ["text"] = "Тест" }
        }
    }
};
var nativeChatData = MaxMessagePack.Decode(MaxMessagePack.Encode(chatsPayload))
    as Dictionary<string, object?>;
Check(nativeChatData is not null, "native chat payload roundtrip");
var nativeChatPage = MaxChatListCodec.DecodeNative(nativeChatData!);
Check(nativeChatPage.Chats.Count == 1 &&
      nativeChatPage.Chats[0].Id == -123 &&
      nativeChatPage.Chats[0].UnreadCount == 3 &&
      nativeChatPage.Chats[0].Preview == "Тест" &&
      nativeChatPage.Marker == 1700000000000L, "native chat list fields");
using (var jsonChats = System.Text.Json.JsonDocument.Parse(
    "{\"marker\":1700000000000,\"chats\":[{\"id\":-123,\"title\":\"Тестовый канал\",\"newMessages\":3,\"lastMessage\":{\"text\":\"Тест\"}}]}"))
{
    var webChatPage = MaxChatListCodec.DecodeWeb(jsonChats.RootElement);
    Check(webChatPage.Chats.Count == 1 &&
          webChatPage.Chats[0].Id == nativeChatPage.Chats[0].Id &&
          webChatPage.Chats[0].Preview == "Тест", "web chat list fields");
}
bool missingChatsRejected = false;
try { MaxChatListCodec.DecodeNative(new Dictionary<string, object?>()); }
catch (InvalidDataException) { missingChatsRejected = true; }
Check(missingChatsRejected, "missing chats array is not a verified empty list");
Check(MaxChatListCodec.Opcode == 53 && MaxChatListCodec.PageSize == 100,
      "chat list request opcode and count");
Console.WriteLine("Chat list MessagePack/JSON decoding without account data: PASS");

Check(!MaxHistoryCodec.CanOpenChat(0) && MaxHistoryCodec.CanOpenChat(-123L) &&
      MaxHistoryCodec.CanOpenChat(123L), "special chat 0 is not a regular history endpoint");
// Synthetic-only, read-only GET_HISTORY(49) request/response regression tests.
var historyRequest = MaxHistoryCodec.CreateRequest(-33, 1781000000000L, 30);
var requestMap = historyRequest.ToNativePayload();
Check(MaxHistoryCodec.Opcode == 49 && Equals(requestMap["chatId"], -33L) &&
    Equals(requestMap["from"], 1781000000000L) &&
    Equals(requestMap["backward"], 30) && Equals(requestMap["forward"], 0) &&
    Equals(requestMap["getChat"], false) && Equals(requestMap["getMessages"], true) &&
    Equals(requestMap["interactive"], false), "GET_HISTORY must be read-only and paged");
Check(!requestMap.ContainsKey("startTime") && !requestMap.ContainsKey("endTime") &&
      !requestMap.ContainsKey("messageIds"), "GET_HISTORY never updates read watermark");
bool invalidHistoryCursorRejected = false;
try { MaxHistoryCodec.CreateRequest(-33, 0, 30); }
catch (ArgumentOutOfRangeException) { invalidHistoryCursorRejected = true; }
Check(invalidHistoryCursorRejected, "missing backward cursor rejected");
var syntheticHistory = new Dictionary<string, object?>
{
    ["messages"] = new object?[]
    {
        new Dictionary<string, object?>
        {
            ["id"] = 1002L, ["time"] = 1781000002000L, ["sender"] = 33L,
            ["type"] = "USER", ["text"] = "Второе"
        },
        new Dictionary<string, object?>
        {
            ["id"] = 1001L, ["time"] = 1781000001000L, ["sender"] = 44L,
            ["type"] = "USER", ["text"] = "Первое"
        },
        new Dictionary<string, object?>
        {
            ["id"] = 1000L, ["time"] = 1781000000000L, ["sender"] = 55L,
            ["type"] = "USER", ["attaches"] = new object?[]
            {
                new Dictionary<string, object?> { ["type"] = "IMAGE" }
            }
        }
    }
};
var packedHistory = MaxMessagePack.Decode(MaxMessagePack.Encode(syntheticHistory))
    as Dictionary<string, object?>;
Check(packedHistory is not null, "native history pack roundtrip");
var nativeHistory = MaxHistoryCodec.DecodeNative(-33, packedHistory!, 3);
Check(nativeHistory.Messages.Count == 3 &&
    nativeHistory.Messages[0].Id == 1000L &&
    nativeHistory.Messages[0].Text.Contains("Вложение", StringComparison.Ordinal) &&
    nativeHistory.Messages[1].Id == 1001L &&
    nativeHistory.Messages[1].Sender == "44" &&
    nativeHistory.Messages[2].Id == 1002L &&
    nativeHistory.NextBefore == 1781000000000L &&
    nativeHistory.HasMore, "native history sorting, cursor and attachments");
using (var historyJson = System.Text.Json.JsonDocument.Parse(
    """{"messages":[{"id":1002,"time":1781000002000,"sender":33,"text":"Второе"},{"id":1001,"time":1781000001000,"sender":44,"text":"Первое"},{"id":1000,"time":1781000000000,"sender":55,"attaches":[{"type":"IMAGE"}]}]}"""))
{
    var webHistory = MaxHistoryCodec.DecodeWeb(-33, historyJson.RootElement, 3);
    Check(webHistory.Messages.Count == 3 &&
        webHistory.Messages[0].Id == nativeHistory.Messages[0].Id &&
        webHistory.Messages[1].Text == "Первое" &&
        webHistory.NextBefore == nativeHistory.NextBefore &&
        webHistory.HasMore, "WebSocket history decoding");
}
var missingHistoryRejected = false;
try { MaxHistoryCodec.DecodeNative(55, new Dictionary<string, object?>(), 30); }
catch (InvalidDataException) { missingHistoryRejected = true; }
Check(missingHistoryRejected, "missing history is an error, not empty thread");
var lastPage = MaxHistoryCodec.DecodeNative(-33,
    new Dictionary<string, object?> { ["messages"] = new object?[] {} }, 30);
Check(lastPage.Messages.Count == 0 && !lastPage.HasMore && lastPage.NextBefore is null,
    "empty history is valid only with explicit messages array");
Console.WriteLine("GET_HISTORY(49) MessagePack/JSON, cursors, safety: PASS");


var syntheticKeyboard = new Dictionary<string, object?>
{
    ["messages"] = new object?[]
    {
        new Dictionary<string, object?>
        {
            ["id"] = 4001L, ["time"] = 1781000000000L, ["sender"] = 12L,
            ["text"] = "Проверить заказ",
            ["attaches"] = new object?[]
            {
                new Dictionary<string, object?> { ["_type"] = "PHOTO" },
                new Dictionary<string, object?>
                {
                    ["keyboard"] = new Dictionary<string, object?>
                    {
                        ["buttons"] = new object?[]
                        {
                            new object?[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["type"] = "OPEN_APP", ["text"] = "Открыть",
                                    ["webApp"] = "https://max.ru/TestShopBot",
                                    ["payload"] = "order_1", ["contactId"] = 456L
                                }
                            }
                        }
                    }
                }
            }
        }
    }
};
var nativeKeyboard = MaxMessagePack.Decode(MaxMessagePack.Encode(syntheticKeyboard))
    as Dictionary<string, object?>;
Check(nativeKeyboard is not null, "native keyboard MessagePack roundtrip");
var buttonsNative = MaxHistoryCodec.DecodeNative(100, nativeKeyboard!, 30).Messages.Single().Buttons;
Check(buttonsNative.Count == 1 && buttonsNative[0].Text == "Открыть" &&
      buttonsNative[0].ContactId == 456 && buttonsNative[0].Payload == "order_1",
      "native OPEN_APP button survives history decode");
using (var keyboardJson = System.Text.Json.JsonDocument.Parse(
    """{"messages":[{"id":4001,"time":1781000000000,"text":"Проверить заказ","attaches":[{"keyboard":{"buttons":[[{"type":"OPEN_APP","text":"Открыть","webApp":"https://max.ru/TestShopBot","payload":"order_1","contactId":456}]]}}]}]}"""))
{
    var buttonsWeb = MaxHistoryCodec.DecodeWeb(100, keyboardJson.RootElement, 30).Messages.Single().Buttons;
    Check(buttonsWeb.Count == 1 && buttonsWeb[0] == buttonsNative[0], "web OPEN_APP matches native");
}
Check(MaxMiniAppLink.TryResolve("https://max.ru/TestShopBot", "order_1", out var link) &&
      link!.AbsoluteUri == "https://max.ru/TestShopBot?startapp=order_1", "bot launch with start parameter");
Check(MaxMiniAppLink.TryResolve("@TestShopBot", null, out var handleLink) &&
      handleLink!.AbsoluteUri == "https://max.ru/TestShopBot?startapp", "bot username deep link");
Check(!MaxMiniAppLink.TryResolve("javascript:alert(1)", null, out _) &&
      !MaxMiniAppLink.TryResolve("http://example.org", null, out _) &&
      !MaxMiniAppLink.TryResolve("https://evil@example.org/", null, out _),
      "unsafe mini-app schemes and credentialed URLs rejected");
Console.WriteLine("MAX OPEN_APP keyboards (native/web), HTTPS launch URL validation: PASS");

// Isolated mini-app routing must not change Windows' VPN/proxy settings.
// MAX hosts use the physical source IPv4; third-party domains keep system routing.
Check(MiniAppNetworkProxy.IsMaxHost("max.ru") &&
      MiniAppNetworkProxy.IsMaxHost("st.max.ru") &&
      MiniAppNetworkProxy.IsMaxHost("ws-api.oneme.ru") &&
      !MiniAppNetworkProxy.IsMaxHost("example.org") &&
      !MiniAppNetworkProxy.IsMaxHost("evilmax.ru"),
      "mini-app route only bypasses VPN for MAX domains");
Check(MiniAppNetworkProxy.TryReadConnectHost("CONNECT max.ru:443 HTTP/1.1", out string hostname) &&
      hostname == "max.ru" &&
      !MiniAppNetworkProxy.TryReadConnectHost("CONNECT max.ru:80 HTTP/1.1", out _) &&
      !MiniAppNetworkProxy.TryReadConnectHost("GET http://max.ru HTTP/1.1", out _) &&
      !MiniAppNetworkProxy.TryReadConnectHost("CONNECT localhost:443 HTTP/1.1", out _) &&
      !MiniAppNetworkProxy.TryReadConnectHost("CONNECT 127.0.0.1:443 HTTP/1.1", out _) &&
      !MiniAppNetworkProxy.TryReadConnectHost("CONNECT 192.168.1.1:443 HTTP/1.1", out _) &&
      !MiniAppNetworkProxy.TryReadConnectHost("CONNECT host.example:443\\r\\nX-Test: y HTTP/1.1", out _) &&
      MiniAppNetworkProxy.IsPublicIPv4(System.Net.IPAddress.Parse("8.8.8.8")) &&
      !MiniAppNetworkProxy.IsPublicIPv4(System.Net.IPAddress.Parse("10.0.0.2")),
      "reject local-network, non-TLS and malformed proxy destinations");
using (var localProxy = new MiniAppNetworkProxy(System.Net.IPAddress.Loopback))
using (var client = new System.Net.Sockets.TcpClient())
{
    await client.ConnectAsync(System.Net.IPAddress.Loopback, localProxy.Port);
    using var network = client.GetStream();
    await network.WriteAsync(System.Text.Encoding.ASCII.GetBytes(
        "CONNECT 127.0.0.1:443 HTTP/1.1\r\nHost: localhost\r\n\r\n"));
    byte[] response = new byte[256];
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    int received = await network.ReadAsync(response, timeout.Token);
    Check(System.Text.Encoding.ASCII.GetString(response, 0, received)
          .StartsWith("HTTP/1.1 403", StringComparison.Ordinal),
          "mini-app loopback proxy refuses internal endpoints");
}
Console.WriteLine("Mini-app HTTPS CONNECT policy / local refusal: PASS");

// Round-trip the field types actually needed by MAX's native handshake and SMS login.
var map = new Dictionary<string, object?>
{
    ["phone"] = "+79990000000",
    ["type"] = "START_AUTH",
    ["mode"] = new byte[] { 0x10, 0xff, 0x00 },
    ["interactive"] = true,
    ["seed"] = 123456789L,
    ["presenceSync"] = -1L,
    ["optional"] = null,
    ["userAgent"] = new Dictionary<string, object?>
    {
        ["deviceType"] = "ANDROID",
        ["buildNumber"] = 6867,
        ["supported"] = new object?[] { "SMS", "2FA", 17L }
    }
};
byte[] encoded = MaxMessagePack.Encode(map);
var decoded = MaxMessagePack.Decode(encoded) as Dictionary<string, object?>;
Check(decoded is not null, "decoded dictionary");
Check(Equals(decoded["phone"], "+79990000000"), "phone roundtrip");
Check(Equals(decoded["seed"], 123456789L), "integer roundtrip");
Check(Equals(decoded["presenceSync"], -1L), "negative integer roundtrip");
Check(Equals(decoded["interactive"], true), "boolean roundtrip");
Check(decoded["optional"] is null, "nil roundtrip");
Check(((byte[])decoded["mode"]!).SequenceEqual(new byte[] { 0x10, 0xff, 0x00 }), "binary roundtrip");
Check(decoded["userAgent"] is Dictionary<string, object?> agent &&
    Equals(agent["deviceType"], "ANDROID"), "nested map roundtrip");

byte[] frame = MaxTcpFraming.Encode(42, 17, map);
Check(frame.Length == MaxTcpFraming.HeaderSize + encoded.Length, "frame length");
Check(frame[0] == 10 && frame[1] == 0, "TCP version and command");
var parsed = MaxTcpFraming.Decode(frame.AsSpan(0, 10), frame.AsSpan(10));
Check(parsed.Sequence == 42 && parsed.Opcode == 17 && parsed.Command == 0, "frame opcode and sequence");
Check(Equals(parsed.Payload["type"], "START_AUTH"), "frame payload");
Check(MaxTcpFraming.PayloadLength(frame.AsSpan(0, 10)) == encoded.Length, "frame length decoded");
byte[] emptyFrame = new byte[10];
emptyFrame[0] = 10; emptyFrame[1] = 1;
Check(MaxTcpFraming.Decode(emptyFrame, ReadOnlySpan<byte>.Empty).Payload.Count == 0, "empty response");

byte[] ext = new byte[] { 0xc7, 0x01, 0x01, 0x2a };
Check(Equals(MaxMessagePack.Decode(ext), 42L), "nested MAX extension");
byte[] lz4Literal = new byte[] { 0xb0 }.Concat(Encoding.UTF8.GetBytes("hello world")).ToArray();
Check(Encoding.UTF8.GetString(MaxLz4.Decompress(lz4Literal)) == "hello world", "LZ4 literal block");
byte[] lz4Backref = new byte[] { 0x40, 0x61, 0x62, 0x63, 0x64, 0x04, 0x00 };
Check(Encoding.UTF8.GetString(MaxLz4.Decompress(lz4Backref)) == "abcdabcd", "LZ4 backreference");
try
{
    MaxLz4.Decompress(new byte[] { 0x00, 0x00, 0x00 });
    throw new Exception("Zero-offset LZ4 was accepted.");
}
catch (InvalidDataException) { }

byte[] fingerprint = MaxAndroidFingerprint.Generate("0123456789abcdef", 123456789L);
Check(fingerprint.Length == 96, "fingerprint length");
Check(Convert.ToHexString(SHA256.HashData(fingerprint)).Equals(
    "EAFDE211F2023FD3E7C5C9C22FFFCFB046D3FBAD992A9230969C2B22D7B0EBFE",
    StringComparison.Ordinal), "fingerprint reference vector");

using var native = new NativeMaxTransport(new EmptyStore());
Check(native.State == ConnectionState.Disconnected, "native initial state");
var malformed = await native.BeginLoginAsync("abc");
Check(malformed.State == ConnectionState.Failed, "reject malformed phone before network");
Check((await native.RestoreSessionAsync()).State == ConnectionState.Disconnected, "no stored token");
bool unauthenticatedChatsRejected = false;
try { await native.GetChatsAsync(); }
catch (InvalidOperationException) { unauthenticatedChatsRejected = true; }
Check(unauthenticatedChatsRejected, "unauthenticated chat fetch must be rejected");
Console.WriteLine("Native MAX framing, MessagePack, LZ4, fingerprint, authentication boundary: PASS");



if (MaxTlsCertificateValidator.Validate(new object(), null, null,
    System.Net.Security.SslPolicyErrors.None))
    throw new Exception("A connection with no server certificate was accepted.");

if (MaxTlsCertificateValidator.Validate(new object(), null, null,
    System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch))
    throw new Exception("A certificate name mismatch was accepted.");

const string certResource = "MaxDesktop.Protocol.Certificates.rootca_ssl_rsa2022.crt";
using (var resource = typeof(MaxTlsCertificateValidator).Assembly.GetManifestResourceStream(certResource))
{
    if (resource is null || resource.Length < 1000)
        throw new Exception("The MAX-specific root CA certificate was not embedded.");
}
Console.WriteLine("TLS trust policy and embedded root checks passed.");


// End-to-end loopback tests for raw proxy CONNECT/SOCKS5 tunnels.
// No traffic to MAX, and never transmit an account token through the proxy.
using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var server = Task.Run(async () =>
    {
        using var peer = await listener.AcceptTcpClientAsync(stop.Token);
        var stream = peer.GetStream();
        using var request = new MemoryStream();
        byte[] one = new byte[1];
        while (request.Length < 2048)
        {
            await stream.ReadExactlyAsync(one, stop.Token);
            request.WriteByte(one[0]);
            if (Encoding.ASCII.GetString(request.ToArray()).EndsWith("\r\n\r\n"))
                break;
        }
        string requestLine = Encoding.ASCII.GetString(request.ToArray());
        Check(requestLine.StartsWith("CONNECT api2.oneme.ru:443 HTTP/1.1\r\n", StringComparison.Ordinal),
            "HTTP CONNECT target");
        Check(!requestLine.Contains("Proxy-Authorization", StringComparison.OrdinalIgnoreCase),
            "do not send proxy credentials");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 Connection established\r\n\r\n"), stop.Token);
        byte[] proof = new byte[3];
        await stream.ReadExactlyAsync(proof, stop.Token);
        Check(proof.SequenceEqual(new byte[] { 3, 1, 4 }), "HTTP CONNECT tunnel bytes");
    });
    var tunnel = await MaxSystemProxyTunnel.OpenAsync(
        new Uri($"http://127.0.0.1:{port}"), "api2.oneme.ru", 443, stop.Token);
    try
    {
        await tunnel.Stream.WriteAsync(new byte[] { 3, 1, 4 }, stop.Token);
        await server;
    }
    finally
    {
        tunnel.Stream.Dispose();
        tunnel.Socket.Dispose();
        listener.Stop();
    }
}

using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var server = Task.Run(async () =>
    {
        using var peer = await listener.AcceptTcpClientAsync(stop.Token);
        var stream = peer.GetStream();
        byte[] greeting = new byte[3];
        await stream.ReadExactlyAsync(greeting, stop.Token);
        Check(greeting.SequenceEqual(new byte[] { 5, 1, 0 }), "SOCKS5 no-auth greeting");
        await stream.WriteAsync(new byte[] { 5, 0 }, stop.Token);
        byte[] header = new byte[5];
        await stream.ReadExactlyAsync(header, stop.Token);
        Check(header[0] == 5 && header[1] == 1 && header[3] == 3, "SOCKS5 connect request");
        byte[] destination = new byte[header[4] + 2];
        await stream.ReadExactlyAsync(destination, stop.Token);
        string host = Encoding.ASCII.GetString(destination, 0, header[4]);
        Check(host == "api-tg.oneme.ru", "SOCKS5 DNS host preserved");
        Check(destination[^2] == 1 && destination[^1] == 187, "SOCKS5 target port");
        await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 0 }, stop.Token);
        byte[] proof = new byte[3];
        await stream.ReadExactlyAsync(proof, stop.Token);
        Check(proof.SequenceEqual(new byte[] { 2, 7, 1 }), "SOCKS5 tunnel bytes");
    });
    var tunnel = await MaxSystemProxyTunnel.OpenAsync(
        new Uri($"socks5://127.0.0.1:{port}"), "api-tg.oneme.ru", 443, stop.Token);
    try
    {
        await tunnel.Stream.WriteAsync(new byte[] { 2, 7, 1 }, stop.Token);
        await server;
    }
    finally
    {
        tunnel.Stream.Dispose();
        tunnel.Socket.Dispose();
        listener.Stop();
    }
}

using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var server = Task.Run(async () =>
    {
        using var peer = await listener.AcceptTcpClientAsync(stop.Token);
        var stream = peer.GetStream();
        // Consume the CONNECT request before returning proxy-auth-required.
        byte[] one = new byte[1];
        int state = 0;
        while (state != 4)
        {
            await stream.ReadExactlyAsync(one, stop.Token);
            if (one[0] == 13 && (state == 0 || state == 2)) state++;
            else if (one[0] == 10 && (state == 1 || state == 3)) state++;
            else state = one[0] == 13 ? 1 : 0;
        }
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 407 Proxy Authentication Required\r\n\r\n"), stop.Token);
    });
    bool refused = false;
    try
    {
        await MaxSystemProxyTunnel.OpenAsync(
            new Uri($"http://127.0.0.1:{port}"), "api2.oneme.ru", 443, stop.Token);
    }
    catch (UnauthorizedAccessException) { refused = true; }
    await server;
    listener.Stop();
    Check(refused, "HTTP CONNECT 407 must fail, not silently transmit credentials");
}
Console.WriteLine("HTTP CONNECT, SOCKS5 and proxy authentication refusal: PASS");

sealed class EmptyStore : IMaxSessionStore
{
    public MaxSavedSession? Load() => null;
    public void Save(MaxSavedSession session) => throw new Exception("Should not save unverified login.");
    public void Delete() { }
}
