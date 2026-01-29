using Antty.Embedding;
using System.Text.Json;
using UglyToad.PdfPig;
using Spectre.Console;

namespace Antty;

public static class IngestionBuilder
{
    public static async Task BuildDatabaseAsync(string filePath, IEmbeddingProvider provider, string outputPath)
    {
        AnsiConsole.Write(new Rule("[bold yellow]🚀 STARTING INGESTION[/]").RuleStyle("yellow"));
        AnsiConsole.WriteLine();

        var chunks = new List<RawChunk>();
        int globalId = 0;

        // 1. EXTRACT TEXT FROM FILE (supports PDF, TXT, MD, JSON)
        var extractedText = await ExtractTextFromFileAsync(filePath);

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            )
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[cyan]Processing {Path.GetFileName(filePath)}[/]", maxValue: extractedText.Count);

                foreach (var (pageNumber, text) in extractedText)
                {
                    // Simple splitting by double newline (paragraphs)
                    var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var para in paragraphs)
                    {
                        string cleanText = para.Replace("\n", " ").Trim();

                        // --- 🧹 NOISE FILTERING ---
                        // Skip if too short (headers, page nums, noise)
                        if (cleanText.Length < 30) continue;
                        // Skip if it looks like just a number
                        if (int.TryParse(cleanText, out _)) continue;

                        chunks.Add(new RawChunk
                        {
                            Id = globalId++,
                            PageNumber = pageNumber,
                            Content = cleanText
                        });
                    }

                    task.Increment(1);
                    await Task.Delay(1); // Allow UI to update
                }
            });

        AnsiConsole.MarkupLine($"[green]✓[/] Extracted [bold cyan]{chunks.Count}[/] valid paragraphs.");
        AnsiConsole.WriteLine();

        // 2. EMBEDDING (Batch Process)
        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            )
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[yellow]Generating embeddings[/]", maxValue: chunks.Count);

                int batchSize = 10; // Send 10 paragraphs at a time
                for (int i = 0; i < chunks.Count; i += batchSize)
                {
                    var batch = chunks.Skip(i).Take(batchSize).ToList();
                    var batchTexts = batch.Select(c => c.Content).ToList();

                    try
                    {
                        var embeddings = await provider.GenerateEmbeddingsAsync(batchTexts);

                        for (int j = 0; j < batch.Count; j++)
                        {
                            batch[j].Vector = embeddings[j];
                        }

                        task.Increment(batch.Count);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error fetching embeddings: {ex.Message}[/]");
                    }
                }
            });

        AnsiConsole.WriteLine();

        // 3. SAVE TO DISK WITH METADATA
        var knowledgeBase = new KnowledgeBase
        {
            Metadata = new KnowledgeBaseMetadata
            {
                Provider = provider.ProviderName,
                ModelName = provider.ModelName,
                Dimensions = provider.Dimensions,
                CreatedAt = DateTime.UtcNow
            },
            Chunks = chunks
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        string jsonString = JsonSerializer.Serialize(knowledgeBase, jsonOptions);
        await File.WriteAllTextAsync(outputPath, jsonString);

        var fileSize = new FileInfo(outputPath).Length / 1024;
        AnsiConsole.Write(new Panel(
            new Markup($"[green]✅ Database saved to[/] [bold cyan]{Path.GetFileName(outputPath)}[/]\n[dim]Provider: {provider.ProviderName} ({provider.ModelName})\nSize: {fileSize} KB[/]")
        )
        .BorderColor(Color.Green)
        .Padding(1, 0));

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Extracts text from various file formats (PDF, TXT, MD, JSON)
    /// Returns a list of (page/section number, text content) tuples
    /// </summary>
    private static async Task<List<(int pageNumber, string text)>> ExtractTextFromFileAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var results = new List<(int, string)>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan bold"))
            .StartAsync($"[cyan]Reading {Path.GetFileName(filePath)}...[/]", async ctx =>
            {
                switch (extension)
                {
                    case ".pdf":
                        // Extract from PDF using PdfPig
                        using (var document = PdfDocument.Open(filePath))
                        {
                            foreach (var page in document.GetPages())
                            {
                                results.Add((page.Number, page.Text));
                            }
                        }
                        break;

                    case ".txt":
                    case ".md":
                    case ".json":
                        // Read text files directly
                        var content = await File.ReadAllTextAsync(filePath);
                        // Split into "pages" of ~2000 characters for consistency
                        var chunkSize = 2000;
                        for (int i = 0; i < content.Length; i += chunkSize)
                        {
                            var pageNum = (i / chunkSize) + 1;
                            var chunk = content.Substring(i, Math.Min(chunkSize, content.Length - i));
                            results.Add((pageNum, chunk));
                        }
                        break;

                    default:
                        throw new NotSupportedException($"File format '{extension}' is not supported. Supported formats: .pdf, .txt, .md, .json");
                }

                await Task.CompletedTask;
            });

        AnsiConsole.MarkupLine($"[green]✓[/] Loaded [bold cyan]{results.Count}[/] sections from {Path.GetExtension(filePath).ToUpper()} file.");
        return results;
    }
}
