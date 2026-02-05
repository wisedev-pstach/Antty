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
    /// Check if Ollama is installed on the system
    /// </summary>
    public static bool IsOllamaInstalled()
    {
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
            // If already running, don't start again
            if (_ollamaProcess != null && !_ollamaProcess.HasExited)
            {
                return true;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Start the process and keep reference
            _ollamaProcess = Process.Start(startInfo);

            if (_ollamaProcess == null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Failed to start Ollama process");
                return false;
            }

            // Monitor output in background
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_ollamaProcess.HasExited)
                    {
                        var line = await _ollamaProcess.StandardError.ReadLineAsync();
                        if (line != null && line.Contains("error", StringComparison.OrdinalIgnoreCase))
                        {
                            AnsiConsole.MarkupLine($"[red]Ollama error:[/] [dim]{line.EscapeMarkup()}[/]");
                        }
                    }
                }
                catch { }
            });

            AnsiConsole.MarkupLine("[cyan]⏳[/] Starting Ollama service...");

            // Wait for service to be ready with progress
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(1000);

                if (_ollamaProcess.HasExited)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] Ollama process exited with code {_ollamaProcess.ExitCode}");
                    return false;
                }

                if (await IsOllamaRunningAsync())
                {
                    AnsiConsole.MarkupLine("[green]✓[/] Ollama service started");
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
                FileName = "ollama",
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
                FileName = "ollama",
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
                FileName = "ollama",
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
                // First, try to check if winget is available
                bool hasWinget = false;
                try
                {
                    var checkWinget = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c where winget",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var checkProcess = Process.Start(checkWinget);
                    if (checkProcess != null)
                    {
                        await checkProcess.WaitForExitAsync();
                        hasWinget = checkProcess.ExitCode == 0;
                    }
                }
                catch { }

                if (hasWinget)
                {
                    // Try winget on Windows
                    return await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .SpinnerStyle(Style.Parse("cyan bold"))
                        .StartAsync("[cyan]Installing Ollama via winget...[/]", async ctx =>
                        {
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = "/c winget install Ollama.Ollama -e --silent --accept-source-agreements --accept-package-agreements",
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
                else
                {
                    // Fallback: Download and run Ollama installer directly
                    return await AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .SpinnerStyle(Style.Parse("cyan bold"))
                        .StartAsync("[cyan]Downloading Ollama installer...[/]", async ctx =>
                        {
                            try
                            {
                                var installerPath = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
                                
                                // Download installer with extended timeout
                                using var client = new HttpClient();
                                client.Timeout = TimeSpan.FromMinutes(10); // Ollama installer is large
                                var installerBytes = await client.GetByteArrayAsync("https://ollama.com/download/OllamaSetup.exe");
                                await File.WriteAllBytesAsync(installerPath, installerBytes);
                                
                                ctx.Status("[cyan]Running Ollama installer...[/]");
                                
                                // Run installer completely hidden using PowerShell
                                var psCommand = $"Start-Process -FilePath '{installerPath}' -ArgumentList '/S' -WindowStyle Hidden -Wait";
                                var startInfo = new ProcessStartInfo
                                {
                                    FileName = "powershell.exe",
                                    Arguments = $"-NoProfile -NonInteractive -Command \"{psCommand}\"",
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true
                                };

                                using var process = Process.Start(startInfo);
                                if (process == null)
                                {
                                    AnsiConsole.MarkupLine("[red]✗[/] Failed to start installer");
                                    return false;
                                }

                                await process.WaitForExitAsync();
                                
                                // Clean up
                                try { File.Delete(installerPath); } catch { }

                                if (process.ExitCode == 0)
                                {
                                    ctx.Status("[green]✓ Ollama installed successfully[/]");
                                    await Task.Delay(500);
                                    return true;
                                }
                                else
                                {
                                    AnsiConsole.MarkupLine($"[red]✗[/] Installer exited with code {process.ExitCode}");
                                    return false;
                                }
                            }
                            catch (Exception ex)
                            {
                                AnsiConsole.MarkupLine($"[red]✗[/] Download failed: {ex.Message}");
                                return false;
                            }
                        });
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
