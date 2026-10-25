using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace MaxDesktop.Protocol;

/// <summary>
/// Fingerprint for the independently audited ru.oneme.app 26.35.1 / build 6867.
/// Values are metadata digests from that precise APK, not secret signing keys.
/// </summary>
public static class MaxAndroidFingerprint
{
    public const string ApkVersion = "26.35.1";
    public const int BuildNumber = 6867;
    public const string Architecture = "arm64-v8a";

    // Correspond to the verified APK version 26.35.1 (6867), not arbitrary versions.
    private const string CertificateMetadataSha256 = "1684414033eb263e2c615f8b7df5ed8793850a07656304997fbf07e9e21e1e93";
    private const string DexMetadataSha256 = "58593332e67b8f80fe708f139f43ad65d654ebeea768adf2ea2b1611a54f19b5";
    private const string Arm64NativeMetadataSha256 = "fdaf7841b16e6ddc64092d188a6202a37a525c0d77d6b773d63f9be6e0ac2529";

    public static byte[] Generate(string deviceId, long callsSeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        Span<byte> seed = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(seed, callsSeed);
        byte[] device = Encoding.UTF8.GetBytes(deviceId);
        byte[] result = new byte[96];
        string[] digests = [CertificateMetadataSha256, DexMetadataSha256, Arm64NativeMetadataSha256];
        for (int i = 0; i < digests.Length; i++)
        {
            byte[] metadata = Convert.FromHexString(digests[i]);
            byte[] joined = new byte[metadata.Length + seed.Length + device.Length];
            metadata.CopyTo(joined, 0);
            seed.CopyTo(joined.AsSpan(metadata.Length, seed.Length));
            device.CopyTo(joined, metadata.Length + seed.Length);
            SHA256.HashData(joined).CopyTo(result, i * 32);
            CryptographicOperations.ZeroMemory(joined);
        }
        return result;
    }
}
