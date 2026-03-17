using System.Diagnostics;
using System.Reflection;

namespace Antty.Services;

public static class UpdateService
{
    private const string VersionUrl = "https://raw.githubusercontent.com/wisedev-pstach/Antty/main/version.txt";
    private const string BootstrapWinUrl = "https://raw.githubusercontent.com/wisedev-pstach/Antty/main/bootstrap.ps1";
    private const string BootstrapUnixUrl = "https://raw.githubusercontent.com/wisedev-pstach/Antty/main/bootstrap.sh";

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Returns the latest version string if a newer version is available, otherwise null.
    /// </summary>
    public static async Task<string?> CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var raw = (await client.GetStringAsync(VersionUrl)).Trim();

            if (Version.TryParse(raw, out var latest) &&
                Version.TryParse(CurrentVersion, out var current) &&
                latest > current)
            {
                return raw;
            }
        }
        catch
        {
            // Offline or unreachable — silently skip
        }

        return null;
    }

    /// <summary>
    /// Launches the bootstrap installer in a new terminal window and exits the current process.
    /// </summary>
    public static void PerformUpdate()
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"irm {BootstrapWinUrl} | iex\"",
                UseShellExecute = true
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = $"-c \"curl -fsSL {BootstrapUnixUrl} | bash\"",
                UseShellExecute = true
            });
        }

        Environment.Exit(0);
    }
}
