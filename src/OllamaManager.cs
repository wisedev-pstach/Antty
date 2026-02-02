using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Spectre.Console;

namespace Antty;

/// <summary>
/// Manages Ollama installation, service, and models
/// </summary>
public static class OllamaManager
{
    private const string OllamaHealthUrl = "http://localhost:11434/api/version";
    private const int OllamaDefaultPort = 11434;
    private static Process? _ollamaProcess;
    private static string? _ollamaExecutablePath; // Store the actual path to ollama.exe

    /// <summary>
    /// Get the Ollama executable path (checks PATH and default locations)
    /// </summary>
    private static string GetOllamaExecutablePath()
    {
        // Return cached path if we already found it
        if (!string.IsNullOrEmpty(_ollamaExecutablePath) && File.Exists(_ollamaExecutablePath))
        {
            return _ollamaExecutablePath;
        }

        // Try to use 'ollama' from PATH first
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(5000);
                if (process.ExitCode == 0)
                {
                    _ollamaExecutablePath = "ollama"; // Available in PATH
                    return "ollama";
                }
            }
        }
        catch { }

        // PATH doesn't work, check default installation locations
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var possiblePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Programs", "Ollama", "ollama.exe")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _ollamaExecutablePath = path;
                    return path;
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (File.Exists("/usr/local/bin/ollama"))
            {
                _ollamaExecutablePath = "/usr/local/bin/ollama";
                return "/usr/local/bin/ollama";
            }
            if (File.Exists("/opt/homebrew/bin/ollama"))
            {
                _ollamaExecutablePath = "/opt/homebrew/bin/ollama";
                return "/opt/homebrew/bin/ollama";
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (File.Exists("/usr/local/bin/ollama"))
            {
                _ollamaExecutablePath = "/usr/local/bin/ollama";
                return "/usr/local/bin/ollama";
            }
            if (File.Exists("/usr/bin/ollama"))
            {
                _ollamaExecutablePath = "/usr/bin/ollama";
                return "/usr/bin/ollama";
            }
        }

        // Default fallback
        return "ollama";
    }

    /// <summary>
    /// Check if Ollama is installed on the system
    /// </summary>
    public static bool IsOllamaInstalled()
    {
        var exePath = GetOllamaExecutablePath();
        return !string.IsNullOrEmpty(exePath) && (exePath == "ollama" || File.Exists(exePath));
    }

    /// <summary>
    /// Check if Ollama service is running
    /// </summary>
    public static async Task<bool> IsOllamaRunningAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync(OllamaHealthUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Start Ollama service in background
    /// </summary>
    public static async Task<bool> StartOllamaAsync()
    {
        try
        {
            // Check if Ollama is already running (e.g., GUI auto-started)
            if (await IsOllamaRunningAsync())
            {
                AnsiConsole.MarkupLine("[green]✓[/] Ollama service is already running");
                return true;
            }

            // If already running, don't start again
            if (_ollamaProcess != null && !_ollamaProcess.HasExited)
            {
                return true;
            }

            var ollamaPath = GetOllamaExecutablePath();

            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan bold"))
                .Start("[cyan]Starting Ollama service...[/]", ctx =>
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = ollamaPath, // Use the resolved path
                        Arguments = "serve",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    // Keep reference to prevent garbage collection
                    _ollamaProcess = Process.Start(startInfo);
                });

            // Wait for service to be ready
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(1000);
                if (await IsOllamaRunningAsync())
                {
                    AnsiConsole.MarkupLine("[green]✓[/] Ollama service started");
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get list of installed Ollama models
    /// </summary>
    public static async Task<List<string>> GetInstalledModelsAsync()
    {
        try
        {
            var ollamaPath = GetOllamaExecutablePath();
            
            var startInfo = new ProcessStartInfo
            {
                FileName = ollamaPath,
                Arguments = "list",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return new List<string>();

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Parse output - first line is header, rest are models
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var models = new List<string>();

            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    models.Add(parts[0]); // First column is model name
                }
            }

            return models;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Check if a specific model is installed
    /// </summary>
    public static async Task<bool> IsModelInstalledAsync(string modelName)
    {
        var models = await GetInstalledModelsAsync();
        return models.Any(m => m.Contains(modelName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Pull/download an Ollama model with progress
    /// </summary>
    public static async Task<bool> PullModelAsync(string modelName)
    {
        try
        {
            AnsiConsole.MarkupLine($"[cyan]Downloading model[/] [yellow]{modelName}[/]");
            AnsiConsole.WriteLine();

            var ollamaPath = GetOllamaExecutablePath();

            var startInfo = new ProcessStartInfo
            {
                FileName = ollamaPath,
                Arguments = $"pull {modelName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Failed to start download");
                return false;
            }

            // Ollama outputs progress to both stdout and stderr, read both
            var outputTask = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        var line = await process.StandardOutput.ReadLineAsync();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            Console.WriteLine(line); // Direct console output to avoid markup issues
                        }
                    }
                }
                catch { }
            });

            var errorTask = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        var line = await process.StandardError.ReadLineAsync();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            Console.WriteLine(line); // Progress often goes to stderr
                        }
                    }
                }
                catch { }
            });

            await process.WaitForExitAsync();

            try
            {
                await Task.WhenAll(outputTask, errorTask);
            }
            catch { }

            AnsiConsole.WriteLine();

            if (process.ExitCode == 0)
            {
                // Give Ollama a moment to finalize the model
                await Task.Delay(1000);

                var fileSizeMB = await GetModelSizeAsync(modelName);

                // If size check fails but exit code was 0, trust the exit code
                if (fileSizeMB > 0.1)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Model downloaded: [cyan]{fileSizeMB:F1} GB[/]");
                }
                else
                {
                    // Exit code 0 means success, even if size check had issues
                    AnsiConsole.MarkupLine($"[green]✓[/] Model downloaded successfully");
                }
                return true;
            }

            AnsiConsole.MarkupLine("[red]✗[/] Download failed");
            return false;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Download error: {ex.Message.EscapeMarkup()}");
            return false;
        }
    }

    /// <summary>
    /// Get model size (approximate from ollama list)
    /// </summary>
    private static async Task<double> GetModelSizeAsync(string modelName)
    {
        try
        {
            var ollamaPath = GetOllamaExecutablePath();
            
            var startInfo = new ProcessStartInfo
            {
                FileName = ollamaPath,
                Arguments = "list",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return 0;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains(modelName, StringComparison.OrdinalIgnoreCase))
                {
                    // Try to parse size from output
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (part.Contains("GB", StringComparison.OrdinalIgnoreCase))
                        {
                            var sizeStr = part.Replace("GB", "").Trim();
                            if (double.TryParse(sizeStr, out var size))
                                return size;
                        }
                    }
                }
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Get installation instructions for current platform
    /// </summary>
    public static string GetInstallInstructions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return """
            To install Ollama on macOS:
            1. Visit: https://ollama.com/download
            2. Download the macOS installer
            3. Run the installer and follow the prompts
            
            Or use the command line:
            curl -fsSL https://ollama.com/install.sh | sh
            """;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return """
            To install Ollama on Windows:
            1. Visit: https://ollama.com/download
            2. Download the Windows installer
            3. Run the installer and follow the prompts
            
            Or use winget:
            winget install Ollama.Ollama
            """;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return """
            To install Ollama on Linux:
            curl -fsSL https://ollama.com/install.sh | sh
            """;
        }

        return "Visit https://ollama.com/download for installation instructions.";
    }

    /// <summary>
    /// Try to install Ollama silently
    /// </summary>
    private static async Task<bool> TryAutoInstallAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Use Homebrew on macOS
                return await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan bold"))
                    .StartAsync("[cyan]Installing Ollama via Homebrew...[/]", async ctx =>
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = "-c \"brew install ollama\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(startInfo);
                        if (process == null)
                        {
                            AnsiConsole.MarkupLine("[red]✗[/] Failed to start installation");
                            AnsiConsole.MarkupLine("[yellow]⚠[/] Make sure Homebrew is installed: [cyan]https://brew.sh[/]");
                            return false;
                        }

                        await process.WaitForExitAsync();

                        if (process.ExitCode == 0)
                        {
                            ctx.Status("[green]✓ Ollama installed successfully[/]");
                            await Task.Delay(500); // Brief pause to show success
                            return true;
                        }
                        else
                        {
                            var error = await process.StandardError.ReadToEndAsync();
                            AnsiConsole.MarkupLine($"[red]✗[/] Installation failed");
                            if (error.Contains("brew: command not found"))
                            {
                                AnsiConsole.MarkupLine("[yellow]⚠[/] Homebrew not found. Install it from: [cyan]https://brew.sh[/]");
                            }
                            else if (!string.IsNullOrEmpty(error))
                            {
                                AnsiConsole.MarkupLine($"[dim]{error.Split('\n')[0]}[/]");
                            }
                            return false;
                        }
                    });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Use the official install script on Linux
                return await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan bold"))
                    .StartAsync("[cyan]Installing Ollama...[/]", async ctx =>
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = "-c \"curl -fsSL https://ollama.com/install.sh | sh\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(startInfo);
                        if (process == null)
                        {
                            AnsiConsole.MarkupLine("[red]✗[/] Failed to start installation");
                            return false;
                        }

                        await process.WaitForExitAsync();

                        if (process.ExitCode == 0)
                        {
                            ctx.Status("[green]✓ Ollama installed successfully[/]");
                            await Task.Delay(500);
                            return true;
                        }
                        else
                        {
                            var error = await process.StandardError.ReadToEndAsync();
                            AnsiConsole.MarkupLine($"[red]✗[/] Installation failed");
                            if (!string.IsNullOrEmpty(error))
                            {
                                AnsiConsole.MarkupLine($"[dim]{error.Split('\n')[0]}[/]");
                            }
                            return false;
                        }
                    });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Try winget on Windows
                AnsiConsole.MarkupLine("[cyan]Installing Ollama via winget...[/]");
                AnsiConsole. MarkupLine("[dim]This may take a few minutes and might require admin privileges[/]");
                AnsiConsole.WriteLine();

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = "install Ollama.Ollama -e --silent --accept-source-agreements --accept-package-agreements",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        AnsiConsole.MarkupLine("[red]✗[/] Failed to start winget");
                        return false;
                    }

                    // Track installation phases
                    bool foundPackage = false;
                    bool downloading = false;
                    bool installing = false;

                    // Monitor output for key events
                    var outputTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!process.HasExited)
                            {
                                var line = await process.StandardOutput.ReadLineAsync();
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    if (line.Contains("Found") && !foundPackage)
                                    {
                                        foundPackage = true;
                                        AnsiConsole.MarkupLine("  [cyan]→[/] Found Ollama package");
                                    }
                                    else if (line.Contains("Downloading") && !downloading)
                                    {
                                        downloading = true;
                                        AnsiConsole.MarkupLine("  [cyan]→[/] Downloading Ollama (1.17 GB)...");
                                    }
                                    else if (line.Contains("Installing") && !installing)
                                    {
                                        installing = true;
                                        AnsiConsole.MarkupLine("  [cyan]→[/] Installing...");
                                    }
                                }
                            }
                        }
                        catch { }
                    });

                    // Wait with timeout (20 minutes for slow connections)
                    var timeoutTask = Task.Delay(TimeSpan.FromMinutes(20));
                    var completedTask = await Task.WhenAny(process.WaitForExitAsync(), timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        // Timeout occurred
                        try { process.Kill(); } catch { }
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[yellow]⚠[/] Installation timed out after 20 minutes");
                        return false;
                    }

                    try { await outputTask; } catch { }

                    AnsiConsole.WriteLine();

                    if (process.ExitCode == 0)
                    {
                        AnsiConsole.MarkupLine("[green]✓[/] Ollama installed successfully");
                        return true;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]✗[/] Installation failed (exit code: {process.ExitCode})");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] Installation error: {ex.Message}");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Installation error: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Ensure Ollama is ready (installed and running)
    /// </summary>
    public static async Task<bool> EnsureOllamaReadyAsync()
    {
        // Check installation
        if (!IsOllamaInstalled())
        {
            AnsiConsole.MarkupLine("[yellow]⚠[/] Ollama is not installed");

            // Try to auto-install
            if (!await TryAutoInstallAsync())
            {
                // Auto-install failed, provide manual instructions
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[red]✗[/] Auto-installation failed");
                AnsiConsole.MarkupLine("[yellow]Please install manually:[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[dim]{GetInstallInstructions()}[/]");
                AnsiConsole.WriteLine();
                return false;
            }

            // Wait a moment for installation to complete
            await Task.Delay(2000);

            // Start Ollama service after fresh installation
            AnsiConsole.MarkupLine("[cyan]Starting Ollama service...[/]");
            if (!await StartOllamaAsync())
            {
                AnsiConsole.MarkupLine("[red]✗[/] Failed to start Ollama");
                return false;
            }

            // Give it a moment to fully start
            await Task.Delay(2000);
        }

        // Check if running
        if (!await IsOllamaRunningAsync())
        {
            AnsiConsole.MarkupLine("[yellow]⚠[/] Ollama service is not running");

            // Try to start it
            if (!await StartOllamaAsync())
            {
                AnsiConsole.MarkupLine("[red]✗[/] Failed to start Ollama service");
                AnsiConsole.MarkupLine("[yellow]Please start Ollama manually:[/] [cyan]ollama serve[/]");
                return false;
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[green]✓[/] Ollama service is running");
        }

        return true;
    }
}
