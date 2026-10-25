using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MaxDesktop.Protocol;

namespace MaxDesktop.App;

/// <summary>
/// Stores login tokens with the Windows current-user DPAPI key.
/// Never writes a plaintext token or phone number to disk.
/// </summary>
public sealed class ProtectedMaxSessionStore : IMaxSessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MaxDesktop.Session.v1");
    private static string PathName => Path.Combine(SettingsStore.DataRoot, "max-session.dat");

    public MaxSavedSession? Load()
    {
        if (!File.Exists(PathName)) return null;
        byte[] encrypted = File.ReadAllBytes(PathName);
        byte[] plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<MaxSavedSession>(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Save(MaxSavedSession session)
    {
        Directory.CreateDirectory(SettingsStore.DataRoot);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            byte[] encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            string temp = PathName + ".tmp";
            try
            {
                File.WriteAllBytes(temp, encrypted);
                File.Move(temp, PathName, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public void Delete()
    {
        if (File.Exists(PathName)) File.Delete(PathName);
    }
}
