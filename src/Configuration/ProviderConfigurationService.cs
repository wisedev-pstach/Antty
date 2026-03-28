using Antty.Configuration;
using Antty.Core;
using Antty.Embedding;
using Antty.Services;
using MaIN.Domain.Configuration;
using Spectre.Console;

namespace Antty.Configuration;

public class ProviderConfigurationService : IProviderConfigurationService
{
    public async Task<(IEmbeddingProvider? embeddingProvider, BackendType backendType, string modelName)>
        ConfigureProvidersAsync(AppConfig config, string? updateVersion = null)
    {
        AnsiConsole.Write(new Rule("[bold cyan]🤖 ASSISTANT CONFIGURATION[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        var choices = new List<string>();
        if (updateVersion is not null)
            choices.Add($"🔄 Update to v{updateVersion}");
        choices.Add("💻 Local (Offline) - Ollama + Ollama Embeddings");
        choices.Add("☁️  Cloud (Online) - Cloud Models + OpenAI Embeddings");

        string modeChoice;
        try
        {
            modeChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Choose Operating Mode:[/]")
                    .PageSize(10)
                    .MoreChoicesText("[grey](Press [yellow]ESC[/] to keep current settings)[/]")
                    .AddChoices(choices));
        }
        catch
        {
            return (null, default, ""); // ESC → cancel
        }

        if (modeChoice.StartsWith("🔄"))
        {
            UpdateService.PerformUpdate();
            return (null, default, "");
        }

        bool isLocalMode = modeChoice.StartsWith("💻");

        if (isLocalMode)
        {
            var result = await ConfigureLocalModeAsync(config);
            if (result.embeddingProvider == null) return result;
            return result;
        }
        else
        {
            var result = await ConfigureCloudModeAsync(config);
            if (result.embeddingProvider == null) return result;
            return result;
        }
    }

    private async Task<(IEmbeddingProvider? embeddingProvider, BackendType backendType, string modelName)>
        ConfigureLocalModeAsync(AppConfig config)
    {
        AnsiConsole.MarkupLine("[dim]Configuring for fully offline use...[/]");
        const string embeddingModel = "bge-m3";
        config.EmbeddingProvider = $"ollama-{embeddingModel}";

        BackendType backendType = BackendType.Ollama;
        if (!await OllamaManager.EnsureOllamaReadyAsync())
        {
            AnsiConsole.MarkupLine("[red]Cannot proceed without Ollama.[/]");
            return (null, backendType, "");
        }
        if (!await OllamaManager.IsModelInstalledAsync(embeddingModel))
        {
            AnsiConsole.MarkupLine($"[yellow]Embedding model {embeddingModel} not found.[/]");
            if (AnsiConsole.Confirm($"Download {embeddingModel}?", true))
            {
                if (!await OllamaManager.PullModelAsync(embeddingModel))
                {
                    AnsiConsole.MarkupLine("[red]Failed to download embedding model.[/]");
                    return (null, backendType, "");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Embedding model required for local mode.[/]");
                return (null, backendType, "");
            }
        }
        AnsiConsole.WriteLine();

        string modelName = await ConfigureOllamaModelAsync(config);
        if (string.IsNullOrEmpty(modelName)) return (null, backendType, "");

        config.ChatBackend = backendType.ToString();
        config.ChatModel = modelName;
        config.Save();
        AnsiConsole.WriteLine();

        var embeddingProvider = new OllamaEmbeddingProvider("bge-m3");
        return (embeddingProvider, backendType, modelName);
    }

    private async Task<(IEmbeddingProvider? embeddingProvider, BackendType backendType, string modelName)>
        ConfigureCloudModeAsync(AppConfig config)
    {
        AnsiConsole.MarkupLine("[dim]Configuring for cloud-powered performance...[/]");
        config.EmbeddingProvider = "openai";

        if (string.IsNullOrWhiteSpace(config.ApiKey) || config.ApiKey.StartsWith("sk-YOUR"))
        {
            config.ApiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Enter OpenAI API Key (for Embeddings):[/]")
                    .PromptStyle("green")
                    .Secret());
        }

        string cloudProvider;
        try
        {
            cloudProvider = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Select Chat Provider:[/]")
                    .PageSize(10)
                    .MoreChoicesText("[grey](Press [yellow]ESC[/] to go back)[/]")
                    .AddChoices(new[] {
                        "OpenAI",
                        "Anthropic",
                        "Google Gemini",
                        "DeepSeek",
                        "XAI (Grok)",
                        "Groq"
                    }));
        }
        catch
        {
            return (null, default, ""); // ESC → cancel
        }

        string PromptKey(string name, string current)
        {
            if (!string.IsNullOrWhiteSpace(current) && !current.StartsWith("sk-YOUR")) return current;
            return AnsiConsole.Prompt(new TextPrompt<string>($"[cyan]Enter {name} API Key:[/]").Secret());
        }

        string SelectModel(Dictionary<string, string> options)
        {
            var choices = options.Keys.ToList();
            choices.Add("⌨️  Enter custom model ID...");
            string res;
            try
            {
                res = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[cyan]Select Model:[/]").AddChoices(choices));
            }
            catch
            {
                throw new OperationCanceledException(); // propagate ESC up
            }
            if (res.Contains("Enter custom")) return AnsiConsole.Prompt(new TextPrompt<string>("[cyan]Enter Model ID:[/]").Validate(s => !string.IsNullOrWhiteSpace(s)));
            return options[res];
        }

        BackendType backendType;
        string modelName;

        try
        {
            if (cloudProvider == "OpenAI")
            {
                backendType = BackendType.OpenAi;
                modelName = SelectModel(new Dictionary<string, string> {
                    { "GPT-5.2 (Flagship)", "gpt-5.2" },
                    { "o3 (Reasoning)", "o3" },
                    { "GPT-5 Nano (Light)", "gpt-5-nano" },
                    { "GPT-4o (Omni)", "gpt-4o" },
                    { "o1 (Preview)", "o1" }
                });
            }
            else if (cloudProvider == "Anthropic")
            {
                backendType = BackendType.Anthropic;
                config.AnthropicKey = PromptKey("Anthropic", config.AnthropicKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "Claude 4.5 Sonnet", "claude-sonnet-4-5-20250929" },
                    { "Claude 4.5 Haiku", "claude-haiku-4-5-20251001" },
                    { "Claude 4.5 Opus", "claude-opus-4-5-20251101" },
                    { "Claude 3.7 Sonnet", "claude-3-7-sonnet" }
                });
            }
            else if (cloudProvider == "Google Gemini")
            {
                backendType = BackendType.Gemini;
                config.GeminiKey = PromptKey("Google Gemini", config.GeminiKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "Gemini 3.0 Pro", "gemini-3.0-pro-preview" },
                    { "Gemini 2.5 Pro", "gemini-2.5-pro" },
                    { "Gemini 2.5 Flash", "gemini-2.5-flash" }
                });
            }
            else if (cloudProvider == "DeepSeek")
            {
                backendType = BackendType.DeepSeek;
                config.DeepSeekKey = PromptKey("DeepSeek", config.DeepSeekKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "DeepSeek R1 (Reasoner)", "deepseek-reasoner" },
                    { "DeepSeek V3 (Chat)", "deepseek-chat" }
                });
            }
            else if (cloudProvider == "XAI (Grok)")
            {
                backendType = BackendType.Xai;
                config.XaiKey = PromptKey("XAI (Grok)", config.XaiKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "Grok-3", "grok-3" },
                    { "Grok-2", "grok-2-1212" }
                });
            }
            else if (cloudProvider == "Groq")
            {
                backendType = BackendType.GroqCloud;
                config.GroqKey = PromptKey("Groq", config.GroqKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "Llama 3.3 70B", "llama-3.3-70b-versatile" },
                    { "Llama 3.1 8B", "llama-3.1-8b-instant" },
                    { "Mixtral 8x7B", "mixtral-8x7b-32768" }
                });
            }
            else
            {
                return (null, BackendType.Ollama, "");
            }
        }
        catch (OperationCanceledException)
        {
            return (null, default, ""); // ESC during model selection
        }

        config.ChatBackend = backendType.ToString();
        config.ChatModel = modelName;
        config.Save();
        AnsiConsole.WriteLine();

        var embeddingProvider = new OpenAIEmbeddingProvider(config.ApiKey);
        return (embeddingProvider, backendType, modelName);
    }

    private async Task<string> ConfigureOllamaModelAsync(AppConfig config)
    {
        AnsiConsole.WriteLine("[cyan]🔧 Select Ollama Chat Model:[/]");

        var installedModels = await OllamaManager.GetInstalledModelsAsync();
        var choices = new List<string>();

        void AddIfInstalled(string display, string modelId)
        {
            if (installedModels.Contains(modelId))
                choices.Add($"[green]✓[/] {display} ({modelId})");
            else
                choices.Add($"      {display} ({modelId})");
        }

        AddIfInstalled("Granite4 3b", "Granite4:3b");
        AddIfInstalled("Llama3.1 8B", "llama3.1:8b");
        AddIfInstalled("Qwen3 14b", "Qwen3:14b");

        if (config.CustomOllamaModels.Any())
        {
            foreach (var customModel in config.CustomOllamaModels)
            {
                if (installedModels.Contains(customModel))
                    choices.Add($"[green]✓[/] {customModel} (Custom)");
                else
                    choices.Add($"      {customModel} (Custom)");
            }
        }

        choices.Add("➕ Add custom model...");

        string selection;
        try
        {
            selection = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Select Model:[/]")
                    .PageSize(15)
                    .MoreChoicesText("[grey]([yellow]↓↑[/] to navigate, [yellow]ESC[/] to cancel)[/]")
                    .AddChoices(choices));
        }
        catch
        {
            return ""; // ESC → cancel
        }

        if (selection.Contains("Add custom"))
        {
            var customModelName = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Enter Ollama Model Name:[/]")
                    .Validate(s => !string.IsNullOrWhiteSpace(s)));

            if (!config.CustomOllamaModels.Contains(customModelName))
            {
                config.CustomOllamaModels.Add(customModelName);
                config.Save();
            }

            selection = customModelName;
        }
        else
        {
            selection = System.Text.RegularExpressions.Regex.Match(selection, @"\(([^)]+)\)").Groups[1].Value;
        }

        var modelName = selection.Replace(" (Custom)", "");

        if (await OllamaManager.IsModelInstalledAsync(modelName))
        {
            AnsiConsole.MarkupLine("[green]✓[/] Model already downloaded.");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]Model {modelName} not found locally.[/]");
            if (AnsiConsole.Confirm($"Download {modelName}?"))
            {
                if (!await OllamaManager.PullModelAsync(modelName))
                {
                    AnsiConsole.MarkupLine($"[red]Failed to download {modelName}[/]");
                    return "";
                }
            }
            else
                return "";
        }

        return modelName;
    }
}
