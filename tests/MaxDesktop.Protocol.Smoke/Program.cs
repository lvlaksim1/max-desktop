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
Check((await native.GetChatsAsync()).Count == 0, "do not invent remote chats");
Console.WriteLine("Native MAX framing, MessagePack, LZ4, fingerprint, authentication boundary: PASS");

sealed class EmptyStore : IMaxSessionStore
{
    public MaxSavedSession? Load() => null;
    public void Save(MaxSavedSession session) => throw new Exception("Should not save unverified login.");
    public void Delete() { }
}


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
