using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MaxDesktop.UpdateCore;

/// <summary>
/// Promotes an installer only after its downloaded bytes match the published digest.
/// The hash reader MUST be closed before renaming the file on Windows.
/// </summary>
public static class VerifiedInstaller
{
    public static async Task<string> VerifyAndPromoteAsync(
        string temporaryFile, string finalFile, string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        if (!Regex.IsMatch(expectedSha256, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Контрольная сумма SHA-256 имеет неверный формат.");

        string actual;
        await using (var reader = new FileStream(
            temporaryFile, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(reader, cancellationToken));
        } // file handle is closed here, BEFORE File.Move

        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Контрольная сумма загруженного обновления не совпала.");

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(temporaryFile, finalFile, true);
        return actual;
    }
}
