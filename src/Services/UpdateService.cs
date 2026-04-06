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
            var psi = new ProcessStartInfo { FileName = "osascript", UseShellExecute = false };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"tell application \"Terminal\" to do script \"curl -fsSL {BootstrapUnixUrl} | bash\"");
            Process.Start(psi);
        }
        else
        {
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
