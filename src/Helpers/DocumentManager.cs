using Antty.Core;
using Antty.Configuration;
using Antty;
using Spectre.Console;

namespace Antty.Helpers;

public static class DocumentManager
{
    public static async Task<List<(string filePath, string kbPath)>?> SelectAndLoadDocumentsAsync(
        AppConfig config,
        Embedding.IEmbeddingProvider embeddingProvider)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var supportedExtensions = new[] { ".pdf", ".txt", ".md", ".json" };
        var availableFiles = supportedExtensions
            .SelectMany(ext => Directory.GetFiles(currentDir, $"*{ext}"))
            .ToList();

        if (availableFiles.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ No supported documents found in:[/] [dim]{currentDir}[/]");
            AnsiConsole.MarkupLine("[dim]Supported formats: PDF, TXT, MD, JSON[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to exit...[/]");
            Console.ReadKey();
            return null;
        }

        AnsiConsole.MarkupLine($"[cyan]Found {availableFiles.Count} document(s) in:[/] [dim]{currentDir}[/]");
        AnsiConsole.WriteLine();

        var fileChoices = availableFiles.Select(filePath =>
        {
            var fileName = Path.GetFileName(filePath);
            var kbPath = AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider);
            var hasKB = File.Exists(kbPath);
            return new
            {
                FilePath = filePath,
                DisplayName = hasKB ? $"{fileName} [green]✓ (indexed)[/]" : fileName,
                HasKB = hasKB
            };
        }).ToList();

        var selectedDisplayNames = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cyan]Select documents to load:[/]")
                .PageSize(15)
                .Required()
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm, [yellow]ESC[/] to exit | [green]✓[/] = already indexed)[/]")
                .AddChoices(fileChoices.Select(f => f.DisplayName)!));

        var selectedPaths = selectedDisplayNames
            .Select(displayName =>
            {
                var cleanName = displayName.Replace(" [green]✓ (indexed)[/]", "").Trim();
                return fileChoices.FirstOrDefault(f => Path.GetFileName(f.FilePath) == cleanName)?.FilePath;
            })
            .Where(path => path != null)
            .Select(path => path!)
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
