using Antty.Configuration;
using Spectre.Console;

namespace Antty.Configuration;

public class SettingsService : ISettingsService
{
    public async Task ShowSettingsMenuAsync(AppConfig config)
    {
        string choice;
        try
        {
            choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Settings[/]")
                    .MoreChoicesText("[grey](Select [cyan]🔙 Back[/] or press [yellow]ESC[/] to return)[/]")
                    .AddChoices(new[] {
                        "🔑 Update API Key",
                        "📂 Show Loaded Documents",
                        "🗑️  Clear Knowledge Base Cache",
                        "🔙 Back"
                    }));
        }
        catch
        {
            return; // ESC pressed
        }

        if (choice.StartsWith("🔑"))
        {
            await UpdateApiKeyAsync(config);
        }
        else if (choice.StartsWith("📂"))
        {
            ShowLoadedDocuments(config);
        }
        else if (choice.StartsWith("🗑"))
        {
            ClearKnowledgeBaseCache();
        }
    }

    private async Task UpdateApiKeyAsync(AppConfig config)
    {
        string providerChoice;
        try
        {
            providerChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Select provider to update API key:[/]")
                    .PageSize(10)
                    .MoreChoicesText("[grey](Select [cyan]🔙 Cancel[/] or press [yellow]ESC[/] to return)[/]")
                    .AddChoices(new[] {
                        "OpenAI",
                        "Anthropic",
                        "Google Gemini",
                        "DeepSeek",
                        "XAI (Grok)",
                        "Groq",
                        "🔙 Cancel"
                    }));
        }
        catch
        {
            return; // ESC pressed
        }

        if (providerChoice == "🔙 Cancel")
        {
            return;
        }

        string newKey = AnsiConsole.Prompt(
            new TextPrompt<string>($"[cyan]Enter your {providerChoice} API Key:[/]")
                .PromptStyle("green")
                .Secret());

        switch (providerChoice)
        {
            case "OpenAI":
                config.ApiKey = newKey;
                break;
            case "Anthropic":
                config.AnthropicKey = newKey;
                break;
            case "Google Gemini":
                config.GeminiKey = newKey;
                break;
            case "DeepSeek":
                config.DeepSeekKey = newKey;
                break;
            case "XAI (Grok)":
                config.XaiKey = newKey;
                break;
            case "Groq":
                config.GroqKey = newKey;
                break;
        }

        config.Save();
        AnsiConsole.MarkupLine($"[green]✓[/] {providerChoice} API Key updated!");
        await Task.CompletedTask;
    }

    private void ShowLoadedDocuments(AppConfig config)
    {
        if (config.SelectedDocuments.Count > 0)
        {
            AnsiConsole.MarkupLine("[cyan]Currently loaded documents:[/]");
            foreach (var doc in config.SelectedDocuments)
            {
                AnsiConsole.MarkupLine($"  [dim]•[/] {Path.GetFileName(doc)}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]⚠ No documents loaded[/]");
        }
    }

    private void ClearKnowledgeBaseCache()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antty",
            "cache"
        );

        if (Directory.Exists(cacheDir))
        {
            var confirm = AnsiConsole.Confirm("[yellow]Are you sure you want to clear all cached knowledge bases?[/]");
            if (confirm)
            {
                Directory.Delete(cacheDir, true);
                AnsiConsole.MarkupLine("[green]✓[/] Cache cleared!");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Cache is already empty[/]");
        }
    }
}
