using System.Text.Json;
using MaxDesktop.Protocol;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("WEB transport test: " + description);
}

// No network traffic, phone, QR link, token or real credentials.
const string dummyPhone = "+79990000000";
var request = MaxWebSocketCodec.Encode(42, 17,
    new { phone = dummyPhone, type = "START_AUTH", language = "ru" });
using (var document = JsonDocument.Parse(request))
{
    var root = document.RootElement;
    Check(root.GetProperty("ver").GetInt32() == 11, "WEB protocol version");
    Check(root.GetProperty("cmd").GetInt32() == 0, "WEB request command");
    Check(root.GetProperty("seq").GetInt32() == 42, "WEB sequence");
    Check(root.GetProperty("opcode").GetInt32() == 17, "SMS opcode");
    Check(root.GetProperty("payload").GetProperty("type").GetString() == "START_AUTH", "SMS request type");
    Check(root.GetProperty("payload").GetProperty("language").GetString() == "ru", "SMS language");
}

var response = MaxWebSocketCodec.Decode(
    """{"ver":11,"cmd":1,"seq":42,"opcode":17,"payload":{"token":"dummy","codeLength":6}}""");
Check(response.Version == 11 && response.Command == 1 && response.Opcode == 17, "frame metadata");
Check(response.Payload.GetProperty("token").GetString() == "dummy", "payload cloned after document disposed");
var rejected = MaxWebSocketCodec.Decode(
    """{"ver":11,"cmd":3,"seq":42,"opcode":17,"payload":{"error":"auth.blocked"}}""");
Check(rejected.Command == 3, "MAX protocol error command");
try
{
    MaxWebSocketCodec.Decode("""{"ver":10,"cmd":1,"seq":42,"opcode":17,"payload":{}}""");
    throw new Exception("Incorrect WEB version was accepted.");
}
catch (InvalidDataException) { }

var store = new EmptyStore();
using (var hybrid = new HybridMaxTransport(store))
{
    Check(hybrid.State == ConnectionState.Disconnected, "hybrid initial state");
    Check((await hybrid.RestoreSessionAsync()).State == ConnectionState.Disconnected, "absent saved session");
    hybrid.PreferredMode = MaxConnectionPreference.WebSocket;
    var bad = await hybrid.BeginLoginAsync("not a phone");
    Check(bad.State == ConnectionState.Failed, "reject malformed number without any network request");
    hybrid.PreferredMode = MaxConnectionPreference.Tcp;
    var badTcp = await hybrid.BeginLoginAsync("not a phone");
    Check(badTcp.State == ConnectionState.Failed, "old TCP mode preserved");
    hybrid.PreferredMode = MaxConnectionPreference.Automatic;
}
var legacy = new MaxSavedSession("test", "dummy-token", "id", "instance");
Check(legacy.Transport == "TCP", "legacy sessions stay TCP");
using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(legacy)))
    Check(doc.RootElement.GetProperty("Transport").GetString() == "TCP", "stored transport discriminator");

// Exercise the new swappable Chromium channel with a deterministic fake.
// This never reaches the MAX network and never requests a real SMS.
var simulatedStore = new CapturingStore();
using (var simulated = new HybridMaxTransport(simulatedStore, new DeterministicChannel()))
{
    simulated.PreferredMode = MaxConnectionPreference.BrowserWebSocket;
    var probe = await simulated.ProbeConnectionAsync();
    Check(probe.State == ConnectionState.Disconnected &&
        probe.Error?.Contains("подтверждены", StringComparison.Ordinal) == true,
        "browser channel anonymous INIT");
    var sms = await simulated.BeginLoginAsync(dummyPhone);
    Check(sms.State == ConnectionState.AwaitingVerification && sms.CodeLength == 6,
        "browser channel SMS start");
    var verified = await simulated.VerifyCodeAsync("123456");
    Check(verified.State == ConnectionState.Connected, "browser channel confirmed login");
    Check(simulatedStore.Saved?.Transport == "WEB_EDGE",
        "browser login session persisted with dedicated transport tag");
    await simulated.LogoutAsync();
    Check(simulated.State == ConnectionState.Disconnected && simulatedStore.Saved is null,
        "browser channel logout");
}

// Critically, never retry the SMS request automatically after a transport
// failure on opcode 17: the server could have queued the SMS already.
var smsFailure = new DeterministicChannel { RejectSmsResponse = true };
using (var hybrid = new HybridMaxTransport(new CapturingStore(), smsFailure))
{
    hybrid.PreferredMode = MaxConnectionPreference.Automatic;
    var result = await hybrid.BeginLoginAsync(dummyPhone);
    Check(result.State == ConnectionState.Failed, "uncertain SMS request fails without fallback");
    Check(smsFailure.SmsCount == 1, "no duplicate SMS on uncertain failure");
}
Console.WriteLine("Browser/managed WEB channel separation, SMS and login state, no-duplicate-code rule: PASS");

Console.WriteLine("MAX WEB JSON protocol, SMS payload, selection, and backward-compatible session: PASS");

sealed class EmptyStore : IMaxSessionStore
{
    public MaxSavedSession? Load() => null;
    public void Save(MaxSavedSession session) => throw new Exception("Unexpected save");
    public void Delete() { }
}

sealed class CapturingStore : IMaxSessionStore
{
    public MaxSavedSession? Saved { get; private set; }
    public MaxSavedSession? Load() => Saved;
    public void Save(MaxSavedSession session) => Saved = session;
    public void Delete() => Saved = null;
}

sealed class DeterministicChannel : IMaxWebSocketChannel
{
    private readonly Queue<string> _responses = new();
    public bool IsOpen { get; private set; }
    public bool RejectSmsResponse { get; set; }
    public int SmsCount { get; private set; }
    public Task ConnectAsync(CancellationToken ct)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }
    public Task SendTextAsync(string text, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(text);
        var json = doc.RootElement;
        int opcode = json.GetProperty("opcode").GetInt32();
        int seq = json.GetProperty("seq").GetInt32();
        string payload = opcode switch
        {
            6 => "{}",
            17 => """{"token":"test-otp-only","codeLength":6}""",
            18 => """{"tokenAttrs":{"LOGIN":{"token":"fake-login-token"}}}""",
            19 => "{}",
            _ => "{}"
        };
        if (opcode == 17) SmsCount++;
        if (opcode != 17 || !RejectSmsResponse)
            _responses.Enqueue($"{{\"ver\":11,\"cmd\":1,\"seq\":{seq},\"opcode\":{opcode},\"payload\":{payload}}}");
        return Task.CompletedTask;
    }
    public Task<string> ReceiveTextAsync(CancellationToken ct)
    {
        if (_responses.Count == 0)
            throw new IOException("Simulated response unavailable after SMS send.");
        return Task.FromResult(_responses.Dequeue());
    }
    public Task DisconnectAsync()
    {
        IsOpen = false;
        _responses.Clear();
        return Task.CompletedTask;
    }
    public void Dispose() => IsOpen = false;
}
