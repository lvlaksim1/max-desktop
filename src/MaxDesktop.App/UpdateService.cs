using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaxDesktop.UpdateCore;

namespace MaxDesktop.App;

public sealed record PreparedUpdate(string InstallerPath, string Sha256);
public sealed record UpdateProgress(double Percent, string Message);

/// <summary>Only a release containing a matching SHA-256 file can be applied.</summary>
public static class UpdateService
{
    private const string ReleasesApi = "https://api.github.com/repos/lvlaksim1/max-desktop/releases/latest";
    private const string ReleasePrefix = "https://github.com/lvlaksim1/max-desktop/releases/download/";

    public static async Task<PreparedUpdate?> PrepareAsync(
        IProgress<UpdateProgress> progress, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MaxDesktopUpdater/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var releaseResponse = await client.GetAsync(ReleasesApi, cancellationToken);
        if (releaseResponse.StatusCode == HttpStatusCode.NotFound) return null;
        releaseResponse.EnsureSuccessStatusCode();

        using var release = JsonDocument.Parse(await releaseResponse.Content.ReadAsStringAsync(cancellationToken));
        string tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Некорректная версия выпуска.");
        if (!Version.TryParse(tag[1..], out Version? available))
            throw new InvalidOperationException("Некорректный номер выпуска.");
        Version current = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        if (available <= current) return null;

        string name = $"MaxDesktop_Update_{tag}.exe";
        string checksumName = name + ".sha256";
        Uri? installerUri = null;
        Uri? checksumUri = null;
        foreach (JsonElement asset in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            string assetName = asset.GetProperty("name").GetString() ?? "";
            string address = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!address.StartsWith(ReleasePrefix, StringComparison.Ordinal)) continue;
            if (assetName == name) installerUri = new Uri(address);
            if (assetName == checksumName) checksumUri = new Uri(address);
        }

        if (installerUri is null || checksumUri is null)
            throw new InvalidOperationException("В выпуске отсутствует установщик обновления или контрольная сумма.");
        progress.Report(new UpdateProgress(0, "Проверка контрольной суммы..."));
        string text = await client.GetStringAsync(checksumUri, cancellationToken);
        string expected = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!Regex.IsMatch(expected, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Неверный формат SHA-256.");

        string directory = Path.Combine(SettingsStore.DataRoot, "Updates");
        Directory.CreateDirectory(directory);
        string finalPath = Path.Combine(directory, name);
        string temporaryPath = finalPath + ".download";
        try
        {
            using var response = await client.GetAsync(installerUri,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long? contentLength = response.Content.Headers.ContentLength;
            const long maxLength = 512L * 1024L * 1024L;
            if (contentLength > maxLength)
                throw new InvalidOperationException("Размер обновления превышает допустимый.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 128 * 1024, FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[128 * 1024];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (total > maxLength)
                        throw new InvalidOperationException("Файл обновления превышает допустимый размер.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    double percent = contentLength is > 0
                        ? Math.Min(100.0, (double)total / contentLength.Value * 100.0)
                        : 0;
                    progress.Report(new UpdateProgress(percent, $"Загрузка обновления: {percent:0}%"));
                }
            }
            // The verifier disposes its FileStream before renaming the installer.
            // On Windows, renaming while File.OpenRead is still in scope fails
            // with ERROR_SHARING_VIOLATION, even for our own process.
            string actual = await VerifiedInstaller.VerifyAndPromoteAsync(
                temporaryPath, finalPath, expected, cancellationToken);
            progress.Report(new UpdateProgress(100, "Проверено, установка..."));
            return new PreparedUpdate(finalPath, actual);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
