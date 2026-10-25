using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

if (args.Length != 3 || !int.TryParse(args[1], out int processId)) return 2;
string installer = Path.GetFullPath(args[0]);
string expected = args[2];
if (!Regex.IsMatch(expected, "^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)) return 3;
string updateRoot = Path.GetFullPath(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaxDesktop", "Updates"));
if (!installer.StartsWith(updateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return 4;
if (!Regex.IsMatch(Path.GetFileName(installer), @"^MaxDesktop_Update_v\d+\.\d+\.\d+\.exe$", RegexOptions.CultureInvariant)) return 5;
if (!File.Exists(installer)) return 6;

try
{
    using var previousApp = Process.GetProcessById(processId);
    if (!previousApp.WaitForExit(60_000)) return 7;
}
catch (ArgumentException) { }

using (var stream = File.OpenRead(installer))
{
    string hash = Convert.ToHexString(SHA256.HashData(stream));
    if (!hash.Equals(expected, StringComparison.OrdinalIgnoreCase)) return 8;
}
try
{
    Process.Start(new ProcessStartInfo(installer,
        "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
    {
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(installer)!
    });
    return 0;
}
catch (System.ComponentModel.Win32Exception) { return 9; }
catch (IOException) { return 10; }
