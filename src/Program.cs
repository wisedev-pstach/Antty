using Antty.Configuration;
using Antty.Core;
using Antty.Embedding;
using Antty.Helpers;
using Antty.Models;
using Antty.Services;
using Antty.UI;
using MaIN.Core;
using MaIN.Core.Hub;
using MaIN.Domain.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var services = new ServiceCollection();
services.AddMaIN(new ConfigurationBuilder().Build());
var serviceProvider = services.BuildServiceProvider();
serviceProvider.UseMaIN();

AIHub.Extensions.DisableLLamaLogs();

AnsiConsole.Write(new FigletText("Antty").Centered().Color(Color.Cyan1));
AnsiConsole.Write(new Rule("[dim]Semantic Search powered by MaIN.NET[/]").RuleStyle("dim"));
AnsiConsole.WriteLine();

var config = AppConfig.Load();
var providerService = new ProviderConfigurationService();
var settingsService = new SettingsService();
var searchService = new DocumentSearchService();
var chatService = new AssistantChatService();

var (embeddingProvider, backendType, modelName) = await providerService.ConfigureProvidersAsync(config);

if (embeddingProvider == null)
{
    AnsiConsole.MarkupLine("[red]Configuration failed. Exiting.[/]");
    return;
}

AnsiConsole.WriteLine();

var documentsToProcess = await DocumentManager.SelectAndLoadDocumentsAsync(config, embeddingProvider);
if (documentsToProcess == null || documentsToProcess.Count == 0)
{
    AnsiConsole.MarkupLine("[red]❌ No documents could be loaded. Exiting.[/]");
    return;
}

var multiEngine = new MultiBookSearchEngine();
multiEngine.LoadDocuments(embeddingProvider, documentsToProcess);

if (multiEngine.LoadedDocumentCount == 0)
{
    AnsiConsole.MarkupLine("[red]❌ No documents could be loaded. Exiting.[/]");
    return;
}

bool running = true;

while (running)
{
    var choiceText = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"[cyan]What would you like to do?[/] [dim](Embeddings: {config.EmbeddingProvider} | Model: {config.ChatBackend} - {config.ChatModel})[/]")
            .PageSize(10)
            .MoreChoicesText("[grey](Move with ↑↓, select with [green]Enter[/], [yellow]ESC[/] or [red]❌ Exit[/] to quit)[/]")
            .AddChoices(MenuChoiceExtensions.GetDisplayChoices()));

    var choice = MenuChoiceExtensions.Parse(choiceText);

    AnsiConsole.WriteLine();

    await (choice switch
    {
        MenuChoice.TalkToAssistant => chatService.TalkToAssistantAsync(config, multiEngine, documentsToProcess, backendType, modelName),
        MenuChoice.SearchDocuments => searchService.SearchDocumentsAsync(multiEngine, embeddingProvider, config),
        MenuChoice.SwitchProvider => HandleProviderSwitchAsync(),
        MenuChoice.ReloadDocuments => HandleDocumentReloadAsync(),
        MenuChoice.Settings => settingsService.ShowSettingsMenuAsync(config),
        MenuChoice.Exit => Task.Run(() => running = false),
        _ => throw new ArgumentOutOfRangeException()
    });
}

AnsiConsole.WriteLine();
AnsiConsole.MarkupLine("[dim]Goodbye! 👋[/]");

Task HandleDocumentReloadAsync()
{
    AnsiConsole.MarkupLine("[yellow]Restarting to select documents...[/]");
    AnsiConsole.WriteLine();
    running = false;
    return Task.CompletedTask;
}

async Task HandleProviderSwitchAsync()
{
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[dim]Switching will reconfigure both embedding provider and AI model...[/]");

    if (!AnsiConsole.Confirm("[cyan]Continue?[/]", true))
        return;

    embeddingProvider?.Dispose();

    var (newProvider, newBackend, newModel) = await providerService.ConfigureProvidersAsync(config);

    if (newProvider == null)
    {
        AnsiConsole.MarkupLine("[red]Configuration failed. Keeping previous settings.[/]");
        return;
    }

    embeddingProvider = newProvider;
    backendType = newBackend;
    modelName = newModel;

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[green]✓[/] Provider switched successfully!");
    AnsiConsole.WriteLine();

    var missingCount = config.SelectedDocuments.Count(f => !File.Exists(AppConfig.GetKnowledgeBasePath(f, config.EmbeddingProvider)));

    if (missingCount > 0)
    {
        AnsiConsole.MarkupLine($"[yellow]⚠[/] {missingCount} document(s) need indexing for [cyan]{config.EmbeddingProvider}[/].");

        if (AnsiConsole.Confirm("[cyan]Index missing documents now?[/]", true))
        {
            documentsToProcess = await DocumentManager.RebuildIndicesAsync(config, multiEngine, embeddingProvider);
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]Skipping re-indexing. Search might not work correctly.[/]");
            documentsToProcess = config.SelectedDocuments.Select(f => (f, AppConfig.GetKnowledgeBasePath(f, config.EmbeddingProvider))).ToList();
            multiEngine.LoadDocuments(embeddingProvider, documentsToProcess);
        }
    }
    else
    {
        AnsiConsole.MarkupLine($"[green]✓[/] All documents indexed for [cyan]{config.EmbeddingProvider}[/]. Reloading...");
        documentsToProcess = config.SelectedDocuments.Select(f => (f, AppConfig.GetKnowledgeBasePath(f, config.EmbeddingProvider))).ToList();
        multiEngine.LoadDocuments(embeddingProvider, documentsToProcess);
        AnsiConsole.MarkupLine($"[green]✓[/] Loaded {multiEngine.LoadedDocumentCount} document(s)");
    }
}
