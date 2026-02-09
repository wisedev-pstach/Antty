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

    /// <summary>
    /// Get the full path to the Ollama executable, or just "ollama" to use from PATH
    /// </summary>
    private static string GetOllamaExecutablePath()
    {
        // Default: just use "ollama" command - works if it's in PATH (most installations)
        // This respects user's environment and is the simplest approach
        string defaultCommand = "ollama";

        // Quick check: if "ollama" is accessible in PATH, use it
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Only check specific paths if needed (for edge cases where PATH isn't set)
            // This happens with some fresh Scoop installs before PATH refresh
            string[] commonPaths = {
                // Official Ollama installer - user install (most common)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
                
                // Official Ollama installer - system-wide install
                @"C:\Program Files\Ollama\ollama.exe",
                
                // Scoop user install
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "ollama", "current", "ollama.exe"),
                
                // Scoop global install
                @"C:\ProgramData\scoop\apps\ollama\current\ollama.exe",
                
                // Winget might install here
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe")
            };

            // Only use specific path if we can verify it exists
            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        // Default: just return "ollama" and let OS handle path resolution
        return defaultCommand;
    }

    /// <summary>
    /// Check if Ollama is installed on the system
    /// </summary>
    public static bool IsOllamaInstalled()
    {
        try
        {
            var ollamaPath = GetOllamaExecutablePath();

            // If we found a specific path (not just "ollama"), check if it exists
            if (ollamaPath != "ollama" && !File.Exists(ollamaPath))
            {
                return false;
            }

            // Try to run ollama --version to verify it works
            var startInfo = new ProcessStartInfo
            {
                FileName = ollamaPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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
            var isRunning = response.IsSuccessStatusCode;

            if (isRunning)
            {
                var body = await response.Content.ReadAsStringAsync();
                AnsiConsole.MarkupLine($"[dim]Ollama health check: SUCCESS (Status: {response.StatusCode}, Body: {body.EscapeMarkup()})[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]Ollama health check: FAILED (Status: {response.StatusCode})[/]");
            }

            return isRunning;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Ollama health check: EXCEPTION ({ex.GetType().Name}: {ex.Message.EscapeMarkup()})[/]");
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
            // If already running, don't start again
            if (_ollamaProcess != null && !_ollamaProcess.HasExited)
            {
                AnsiConsole.MarkupLine("[dim]Ollama process already running[/]");
                return true;
            }

            // Use the shared helper to get Ollama executable path
            var fileName = GetOllamaExecutablePath();
            AnsiConsole.MarkupLine($"[dim]Ollama executable path: {fileName.EscapeMarkup()}[/]");

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            AnsiConsole.MarkupLine($"[dim]Starting Ollama with command: {fileName.EscapeMarkup()} serve[/]");

            // Start the process and keep reference
            _ollamaProcess = Process.Start(startInfo);

            if (_ollamaProcess == null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Failed to start Ollama process - Process.Start returned null[/]");
                return false;
            }

            AnsiConsole.MarkupLine($"[dim]Ollama process started with PID: {_ollamaProcess.Id}[/]");

            // Monitor stderr in background (Ollama logs go to stderr)
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_ollamaProcess.HasExited)
                    {
                        var line = await _ollamaProcess.StandardError.ReadLineAsync();
                        if (line != null)
                        {
                            // Log all stderr output for debugging
                            AnsiConsole.MarkupLine($"[dim]Ollama stderr:[/] [grey]{line.EscapeMarkup()}[/]");

                            // Highlight errors
                            if (line.Contains("level=ERROR", StringComparison.OrdinalIgnoreCase))
                            {
                                AnsiConsole.MarkupLine($"[red]Ollama ERROR:[/] [dim]{line.EscapeMarkup()}[/]");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Error reading Ollama stderr:[/] [dim]{ex.Message.EscapeMarkup()}[/]");
                }
            });

            // Monitor stdout in background
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_ollamaProcess.HasExited)
                    {
                        var line = await _ollamaProcess.StandardOutput.ReadLineAsync();
                        if (line != null)
                        {
                            AnsiConsole.MarkupLine($"[dim]Ollama stdout:[/] [grey]{line.EscapeMarkup()}[/]");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Error reading Ollama stdout:[/] [dim]{ex.Message.EscapeMarkup()}[/]");
                }
            });

            AnsiConsole.MarkupLine("[cyan]⏳[/] Starting Ollama service...");

            // Wait for service to be ready with progress (increased timeout for first run)
            AnsiConsole.MarkupLine($"[dim]Waiting for Ollama to respond at {OllamaHealthUrl}...[/]");
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);

                if (_ollamaProcess.HasExited)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] Ollama process exited unexpectedly with code {_ollamaProcess.ExitCode}");
                    return false;
                }

                AnsiConsole.MarkupLine($"[dim]Checking Ollama health... (attempt {i + 1}/30)[/]");
                if (await IsOllamaRunningAsync())
                {
                    AnsiConsole.MarkupLine("[green]✓[/] Ollama service started and responding");
                    return true;
                }
            }

            AnsiConsole.MarkupLine("[red]✗[/] Ollama service did not start in time");
            return false;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Error starting Ollama: {ex.Message.EscapeMarkup()}");
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
            var startInfo = new ProcessStartInfo
            {
                FileName = GetOllamaExecutablePath(),
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

            var startInfo = new ProcessStartInfo
            {
                FileName = GetOllamaExecutablePath(),
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
            var startInfo = new ProcessStartInfo
            {
                FileName = GetOllamaExecutablePath(),
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
            
            Recommended (via Scoop):
            1. Install Scoop if needed: https://scoop.sh
               PowerShell: irm get.scoop.sh | iex
            2. Install Ollama: scoop install ollama
            
            Alternative methods:
            - Visit: https://ollama.com/download
            - Or use winget: winget install Ollama.Ollama
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
                // Install Ollama via Scoop package manager
                return await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan bold"))
                    .StartAsync("[cyan]Checking for Scoop...[/]", async ctx =>
                    {
                        try
                        {
                            // Check if Scoop is installed
                            bool scoopInstalled = false;
                            try
                            {
                                var scoopCheckInfo = new ProcessStartInfo
                                {
                                    FileName = "scoop",
                                    Arguments = "--version",
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };

                                using var scoopCheck = Process.Start(scoopCheckInfo);
                                if (scoopCheck != null)
                                {
                                    await scoopCheck.WaitForExitAsync();
                                    scoopInstalled = scoopCheck.ExitCode == 0;
                                }
                            }
                            catch
                            {
                                scoopInstalled = false;
                            }

                            // Install Scoop if not present
                            if (!scoopInstalled)
                            {
                                ctx.Status("[cyan]Scoop not found. Installing Scoop...[/]");
                                AnsiConsole.WriteLine();
                                AnsiConsole.MarkupLine("[yellow]⚠[/] Scoop will be installed to manage Ollama");
                                AnsiConsole.MarkupLine("[dim]   Scoop is a Windows package manager: https://scoop.sh[/]");
                                AnsiConsole.WriteLine();

                                // Install Scoop using the official installation command
                                var installScoopInfo = new ProcessStartInfo
                                {
                                    FileName = "powershell.exe",
                                    Arguments = "-ExecutionPolicy RemoteSigned -Command \"Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force; Invoke-RestMethod -Uri https://get.scoop.sh | Invoke-Expression\"",
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };

                                using var installScoop = Process.Start(installScoopInfo);
                                if (installScoop == null)
                                {
                                    AnsiConsole.MarkupLine("[red]✗[/] Failed to start Scoop installation");
                                    return false;
                                }

                                // Show output
                                var outputTask = Task.Run(async () =>
                                {
                                    while (!installScoop.HasExited)
                                    {
                                        var line = await installScoop.StandardOutput.ReadLineAsync();
                                        if (!string.IsNullOrWhiteSpace(line))
                                        {
                                            AnsiConsole.MarkupLine($"[dim]{line.EscapeMarkup()}[/]");
                                        }
                                    }
                                });

                                await installScoop.WaitForExitAsync();

                                try { await outputTask; } catch { }

                                if (installScoop.ExitCode != 0)
                                {
                                    var error = await installScoop.StandardError.ReadToEndAsync();
                                    AnsiConsole.MarkupLine("[red]✗[/] Scoop installation failed");
                                    if (!string.IsNullOrEmpty(error))
                                    {
                                        AnsiConsole.MarkupLine($"[dim]{error.Split('\n')[0].EscapeMarkup()}[/]");
                                    }
                                    return false;
                                }

                                AnsiConsole.MarkupLine("[green]✓[/] Scoop installed successfully");
                                await Task.Delay(1000);

                                // Refresh PATH for current process to include Scoop
                                var scoopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims");
                                var currentProcessPath = Environment.GetEnvironmentVariable("Path") ?? "";
                                if (!currentProcessPath.Contains(scoopPath))
                                {
                                    Environment.SetEnvironmentVariable("Path", $"{scoopPath};{currentProcessPath}");
                                }
                            }
                            else
                            {
                                ctx.Status("[green]✓ Scoop is installed[/]");
                                await Task.Delay(500);
                            }

                            // Now install Ollama via Scoop
                            ctx.Status("[cyan]Installing Ollama via Scoop...[/]");
                            AnsiConsole.WriteLine();

                            // Use full path to scoop to ensure it's found
                            var scoopExecutable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "scoop.cmd");
                            if (!File.Exists(scoopExecutable))
                            {
                                scoopExecutable = "scoop"; // Fallback to PATH
                            }

                            var installOllamaInfo = new ProcessStartInfo
                            {
                                FileName = scoopExecutable,
                                Arguments = "install ollama",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            using var installOllama = Process.Start(installOllamaInfo);
                            if (installOllama == null)
                            {
                                AnsiConsole.MarkupLine("[red]✗[/] Failed to start Ollama installation");
                                return false;
                            }

                            // Show installation output
                            var ollamaOutputTask = Task.Run(async () =>
                            {
                                while (!installOllama.HasExited)
                                {
                                    var line = await installOllama.StandardOutput.ReadLineAsync();
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        AnsiConsole.MarkupLine($"[dim]{line.EscapeMarkup()}[/]");
                                    }
                                }
                            });

                            await installOllama.WaitForExitAsync();

                            try { await ollamaOutputTask; } catch { }

                            if (installOllama.ExitCode != 0)
                            {
                                var error = await installOllama.StandardError.ReadToEndAsync();
                                AnsiConsole.MarkupLine("[red]✗[/] Ollama installation failed");
                                if (!string.IsNullOrEmpty(error))
                                {
                                    AnsiConsole.MarkupLine($"[dim]{error.Split('\n')[0].EscapeMarkup()}[/]");
                                }
                                return false;
                            }

                            ctx.Status("[green]✓ Ollama installed successfully[/]");
                            await Task.Delay(500);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]✗[/] Installation failed: {ex.Message.EscapeMarkup()}");
                            return false;
                        }
                    });
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
