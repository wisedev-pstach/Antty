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

    private static string GetOllamaExecutablePath()
    {
        string defaultCommand = "ollama";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
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

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return defaultCommand;
    }

    public static bool IsOllamaInstalled()
    {
        try
        {
            var ollamaPath = GetOllamaExecutablePath();

            if (ollamaPath != "ollama" && !File.Exists(ollamaPath))
            {
                return false;
            }

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

    public static async Task<bool> StartOllamaAsync()
    {
        try
        {
            if (_ollamaProcess != null && !_ollamaProcess.HasExited)
            {
                return true;
            }

            var fileName = GetOllamaExecutablePath();

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _ollamaProcess = Process.Start(startInfo);

            if (_ollamaProcess == null)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Failed to start Ollama process[/]");
                return false;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_ollamaProcess.HasExited)
                    {
                        var line = await _ollamaProcess.StandardError.ReadLineAsync();
                        if (line != null)
                        {
                            if (line.Contains("level=ERROR", StringComparison.OrdinalIgnoreCase))
                            {
                                AnsiConsole.MarkupLine($"[red]Ollama ERROR:[/] [dim]{line.EscapeMarkup()}[/]");
                            }
                        }
                    }
                }
                catch
                {
                }
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_ollamaProcess.HasExited)
                    {
                        var line = await _ollamaProcess.StandardOutput.ReadLineAsync();
                    }
                }
                catch
                {
                }
            });

            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);

                if (_ollamaProcess.HasExited)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] Ollama process exited unexpectedly");
                    return false;
                }

                if (await IsOllamaRunningAsync())
                {
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

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var models = new List<string>();

            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    models.Add(parts[0]);
                }
            }

            return models;
        }
        catch
        {
            return new List<string>();
        }
    }

    public static async Task<bool> IsModelInstalledAsync(string modelName)
    {
        var models = await GetInstalledModelsAsync();
        return models.Any(m => m.Contains(modelName, StringComparison.OrdinalIgnoreCase));
    }

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
                await Task.Delay(1000);

                var fileSizeMB = await GetModelSizeAsync(modelName);

                if (fileSizeMB > 0.1)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Model downloaded: [cyan]{fileSizeMB:F1} GB[/]");
                }
                else
                {
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

    private static async Task<bool> TryAutoInstallAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
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
                return await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .SpinnerStyle(Style.Parse("cyan bold"))
                    .StartAsync("[cyan]Checking for Scoop...[/]", async ctx =>
                    {
                        try
                        {
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

                            if (!scoopInstalled)
                            {
                                ctx.Status("[cyan]Scoop not found. Installing Scoop...[/]");
                                AnsiConsole.WriteLine();
                                AnsiConsole.MarkupLine("[yellow]⚠[/] Scoop will be installed to manage Ollama");
                                AnsiConsole.MarkupLine("[dim]   Scoop is a Windows package manager: https://scoop.sh[/]");
                                AnsiConsole.WriteLine();

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

                            AnsiConsole.WriteLine();

                            var scoopExecutable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "scoop.cmd");
                            if (!File.Exists(scoopExecutable))
                            {
                                scoopExecutable = "scoop";
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

    public static async Task<bool> EnsureOllamaReadyAsync()
    {
        if (!IsOllamaInstalled())
        {
            AnsiConsole.MarkupLine("[yellow]⚠[/] Ollama is not installed");

            if (!await TryAutoInstallAsync())
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[red]✗[/] Auto-installation failed");
                AnsiConsole.MarkupLine("[yellow]Please install manually:[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[dim]{GetInstallInstructions()}[/]");
                AnsiConsole.WriteLine();
                return false;
            }

            await Task.Delay(2000);

            if (!await StartOllamaAsync())
            {
                AnsiConsole.MarkupLine("[red]✗[/] Failed to start Ollama");
                return false;
            }

            await Task.Delay(2000);
        }

        if (!await IsOllamaRunningAsync())
        {
            if (!await StartOllamaAsync())
            {
                AnsiConsole.MarkupLine("[red]✗[/] Failed to start Ollama service");
                AnsiConsole.MarkupLine("[yellow]Please start Ollama manually:[/] [cyan]ollama serve[/]");
                return false;
            }
        }

        return true;
    }
}
