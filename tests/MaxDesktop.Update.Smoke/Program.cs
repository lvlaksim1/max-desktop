using System.Security.Cryptography;
using MaxDesktop.UpdateCore;

string root = Path.Combine(Path.GetTempPath(), "MaxDesktop.Update.Smoke." + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    byte[] content = RandomNumberGenerator.GetBytes(8192);
    string temp = Path.Combine(root, "MaxDesktop_Update_v0.1.2.exe.download");
    string installer = Path.Combine(root, "MaxDesktop_Update_v0.1.2.exe");
    await File.WriteAllBytesAsync(temp, content);
    string expected = Convert.ToHexString(SHA256.HashData(content));
    string actual = await VerifiedInstaller.VerifyAndPromoteAsync(temp, installer, expected);
    if (actual != expected || !File.Exists(installer) || File.Exists(temp))
        throw new Exception("Downloaded installer was not promoted correctly.");

    // Replacing a previously downloaded installer is part of the update flow.
    await File.WriteAllBytesAsync(temp, content);
    await VerifiedInstaller.VerifyAndPromoteAsync(temp, installer, expected);
    if (!File.ReadAllBytes(installer).SequenceEqual(content))
        throw new Exception("Replacement changed the installer content.");

    // A bad checksum must not replace a good existing installer.
    await File.WriteAllBytesAsync(temp, content);
    bool rejected = false;
    try { await VerifiedInstaller.VerifyAndPromoteAsync(temp, installer, new string('0', 64)); }
    catch (InvalidDataException) { rejected = true; }
    if (!rejected || !File.Exists(temp) || !File.ReadAllBytes(installer).SequenceEqual(content))
        throw new Exception("Checksum mismatch was not rejected without overwriting.");
    Console.WriteLine("Updater file-close / SHA-256 / promotion checks: PASS");
}
finally
{
    Directory.Delete(root, recursive: true);
}
