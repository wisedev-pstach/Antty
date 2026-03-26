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
    /// <summary>
    /// Returns the latest version string if a newer version is available,
    /// null if up to date, or throws if the check could not complete.
    /// </summary>
    public static async Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.Add("User-Agent", "Antty-UpdateCheck");
        client.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };

        var url = $"{VersionUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var raw = (await client.GetStringAsync(url, cancellationToken)).Trim();

        if (Version.TryParse(raw, out var latest) &&
            Version.TryParse(CurrentVersion, out var current) &&
            latest > current)
        {
            return raw;
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
        else if (OperatingSystem.IsMacOS())
        {
            // Open a new interactive Terminal window so sudo can prompt for password
            Process.Start(new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e 'tell application \"Terminal\" to do script \"curl -fsSL {BootstrapUnixUrl} | bash\"'",
                UseShellExecute = false
            });
        }
        else
        {
            // Linux: open a new terminal emulator window
            var terminals = new[] { "gnome-terminal", "xterm", "konsole", "xfce4-terminal" };
            foreach (var term in terminals)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = term,
                        Arguments = $"-- bash -c 'curl -fsSL {BootstrapUnixUrl} | bash; exec bash'",
                        UseShellExecute = false
                    });
                    break;
                }
                catch { }
            }
        }

        Environment.Exit(0);
    }
}
