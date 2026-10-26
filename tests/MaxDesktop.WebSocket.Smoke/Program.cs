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
Console.WriteLine("MAX WEB JSON protocol, SMS payload, selection, and backward-compatible session: PASS");

sealed class EmptyStore : IMaxSessionStore
{
    public MaxSavedSession? Load() => null;
    public void Save(MaxSavedSession session) => throw new Exception("Unexpected save");
    public void Delete() { }
}
