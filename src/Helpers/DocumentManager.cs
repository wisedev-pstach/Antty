using Antty.Core;
using Antty.Configuration;
using Antty;
using Spectre.Console;

namespace Antty.Helpers;

public static class DocumentManager
{
    private const string SelectAllCurrentSentinel = "  [bold]Select All[/] [dim](current folder)[/]";
    private const string SelectAllRecursiveSentinel = "  [bold]Select All[/] [dim](including subdirectories)[/]";

    public static async Task<List<(string filePath, string kbPath)>?> SelectAndLoadDocumentsAsync(
        AppConfig config,
        Embedding.IEmbeddingProvider embeddingProvider)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var supportedExtensions = new[] { ".pdf", ".txt", ".md", ".json" };

        var currentDirFiles = supportedExtensions
            .SelectMany(ext => Directory.GetFiles(currentDir, $"*{ext}"))
            .OrderBy(f => f)
            .ToList();

        var subDirFiles = supportedExtensions
            .SelectMany(ext => Directory.GetFiles(currentDir, $"*{ext}", SearchOption.AllDirectories))
            .Except(currentDirFiles)
            .OrderBy(f => f)
            .ToList();

        var allFiles = currentDirFiles.Concat(subDirFiles).ToList();

        if (allFiles.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ No supported documents found in:[/] [dim]{currentDir}[/]");
            AnsiConsole.MarkupLine("[dim]Supported formats: PDF, TXT, MD, JSON[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to exit...[/]");
            Console.ReadKey();
            return null;
        }

        if (subDirFiles.Count > 0)
            AnsiConsole.MarkupLine($"[cyan]Found {currentDirFiles.Count} document(s) in current folder[/] [dim]and {subDirFiles.Count} in subdirectories[/]");
        else
            AnsiConsole.MarkupLine($"[cyan]Found {currentDirFiles.Count} document(s) in:[/] [dim]{currentDir}[/]");

        AnsiConsole.WriteLine();

        // Build display name → file path map
        var choiceMap = new Dictionary<string, string>();

        string MakeDisplayName(string filePath, bool useRelativePath)
        {
            var name = useRelativePath
                ? Path.GetRelativePath(currentDir, filePath)
                : Path.GetFileName(filePath);
            var hasKB = File.Exists(AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider));
            var display = hasKB ? $"{name} [green]✓ (indexed)[/]" : name;
            choiceMap[display] = filePath;
            return display;
        }

        var choices = new List<string>();

        choices.Add(SelectAllCurrentSentinel);
        if (subDirFiles.Count > 0)
            choices.Add(SelectAllRecursiveSentinel);

        foreach (var f in currentDirFiles)
            choices.Add(MakeDisplayName(f, false));

        foreach (var f in subDirFiles)
            choices.Add(MakeDisplayName(f, true));

        var selectedDisplayNames = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cyan]Select documents to load:[/]")
                .PageSize(20)
                .Required()
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm, [yellow]ESC[/] to exit | [green]✓[/] = already indexed)[/]")
                .AddChoices(choices));

        List<string> selectedPaths;

        if (selectedDisplayNames.Contains(SelectAllRecursiveSentinel))
            selectedPaths = allFiles;
        else if (selectedDisplayNames.Contains(SelectAllCurrentSentinel))
            selectedPaths = currentDirFiles;
        else
            selectedPaths = selectedDisplayNames
                .Where(d => choiceMap.ContainsKey(d))
                .Select(d => choiceMap[d])
                .ToList();

        config.SelectedDocuments = selectedPaths;
        config.Save();

        AnsiConsole.WriteLine();

        return await BuildKnowledgeBasesAsync(selectedPaths, config, embeddingProvider);
    }

    public static async Task<List<(string filePath, string kbPath)>> BuildKnowledgeBasesAsync(
        List<string> selectedPaths,
        AppConfig config,
        Embedding.IEmbeddingProvider embeddingProvider)
    {
        var documentsToProcess = new List<(string filePath, string kbPath)>();
        int existingKBCount = 0;
        int newKBCount = 0;

        foreach (var filePath in selectedPaths)
        {
            var kbPath = AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider);
            documentsToProcess.Add((filePath, kbPath));

            if (!File.Exists(kbPath))
            {
                newKBCount++;
                AnsiConsole.MarkupLine($"[yellow]⚙ Building knowledge base for:[/] [cyan]{Path.GetFileName(filePath)}[/]");
                AnsiConsole.WriteLine();

                await IngestionBuilder.BuildDatabaseAsync(filePath, embeddingProvider, kbPath);
            }
            else
            {
                existingKBCount++;
                AnsiConsole.MarkupLine($"[green]✓[/] Using cached knowledge base for [cyan]{Path.GetFileName(filePath)}[/]");
            }
        }

        if (existingKBCount > 0 || newKBCount > 0)
        {
            AnsiConsole.WriteLine();
            if (existingKBCount > 0)
                AnsiConsole.MarkupLine($"[dim]Used {existingKBCount} cached knowledge base(s)[/]");
            if (newKBCount > 0)
                AnsiConsole.MarkupLine($"[dim]Built {newKBCount} new knowledge base(s)[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[dim]Loading Documents[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        return documentsToProcess;
    }

    public static async Task<List<(string filePath, string kbPath)>> RebuildIndicesAsync(
        AppConfig config,
        MultiBookSearchEngine multiEngine,
        Embedding.IEmbeddingProvider embeddingProvider)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[dim]Loading Knowledge Bases[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        var newDocumentsToProcess = new List<(string filePath, string kbPath)>();
        int rebuilt = 0;
        int usedCache = 0;

        foreach (var filePath in config.SelectedDocuments)
        {
            var kbPath = AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider);
            newDocumentsToProcess.Add((filePath, kbPath));

            if (!File.Exists(kbPath))
            {
                AnsiConsole.MarkupLine($"[yellow]⚙ Indexing:[/] [cyan]{Path.GetFileName(filePath)}[/]");
                await IngestionBuilder.BuildDatabaseAsync(filePath, embeddingProvider, kbPath);
                rebuilt++;
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Using existing cache for:[/] [cyan]{Path.GetFileName(filePath)}[/]");
                usedCache++;
            }
        }

        AnsiConsole.WriteLine();
        if (rebuilt > 0)
            AnsiConsole.MarkupLine($"[green]✓[/] Indexed {rebuilt} new document(s) with {config.EmbeddingProvider} embeddings");
        if (usedCache > 0)
            AnsiConsole.MarkupLine($"[dim]✓ Used {usedCache} existing {config.EmbeddingProvider} cache(s)[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Rule("[dim]Reloading Documents[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        multiEngine.LoadDocuments(embeddingProvider, newDocumentsToProcess);

        AnsiConsole.MarkupLine($"[green]✓[/] Loaded {multiEngine.LoadedDocumentCount} document(s)");

        return newDocumentsToProcess;
    }
}
