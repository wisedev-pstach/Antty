using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;
using System.Text.Json;
using UglyToad.PdfPig;
using Spectre.Console;

namespace Antty;

public static class IngestionBuilder
{
    public static async Task BuildDatabaseAsync(string pdfPath, string apiKey, string outputPath)
    {
        AnsiConsole.Write(new Rule("[bold yellow]🚀 STARTING INGESTION[/]").RuleStyle("yellow"));
        AnsiConsole.WriteLine();

        var client = new AzureOpenAIClient(new Uri("https://api.openai.com/v1"), new ApiKeyCredential(apiKey));
        var embeddingClient = client.GetEmbeddingClient("text-embedding-3-small");
        var chunks = new List<RawChunk>();
        int globalId = 0;

        // 1. READ PDF
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green bold"))
            .StartAsync($"[cyan]Reading {Path.GetFileName(pdfPath)}...[/]", async ctx =>
            {
                using (var document = PdfDocument.Open(pdfPath))
                {
                    var progress = AnsiConsole.Progress()
                        .Columns(
                            new TaskDescriptionColumn(),
                            new ProgressBarColumn(),
                            new PercentageColumn(),
                            new SpinnerColumn()
                        );

                    await progress.StartAsync(async pctx =>
                    {
                        var task = pctx.AddTask("[green]Extracting paragraphs[/]", maxValue: document.NumberOfPages);

                        foreach (var page in document.GetPages())
                        {
                            // Simple splitting by double newline (paragraphs)
                            var paragraphs = page.Text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                            foreach (var text in paragraphs)
                            {
                                string cleanText = text.Replace("\n", " ").Trim();

                                // --- 🧹 NOISE FILTERING ---
                                // Skip if too short (headers, page nums, noise)
                                if (cleanText.Length < 30) continue;
                                // Skip if it looks like just a number
                                if (int.TryParse(cleanText, out _)) continue;

                                chunks.Add(new RawChunk
                                {
                                    Id = globalId++,
                                    PageNumber = page.Number,
                                    Content = cleanText
                                });
                            }

                            task.Increment(1);
                            await Task.Delay(1); // Allow UI to update
                        }
                    });
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
                        // Request 512 dimensions to save RAM and CPU
                        var response = await embeddingClient.GenerateEmbeddingsAsync(batchTexts, new EmbeddingGenerationOptions
                        {
                            Dimensions = 512
                        });

                        for (int j = 0; j < batch.Count; j++)
                        {
                            batch[j].Vector = response.Value[j].ToFloats().ToArray();
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

        // 3. SAVE TO DISK
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        string jsonString = JsonSerializer.Serialize(chunks, jsonOptions);
        await File.WriteAllTextAsync(outputPath, jsonString);

        var fileSize = new FileInfo(outputPath).Length / 1024;
        AnsiConsole.Write(new Panel(
            new Markup($"[green]✅ Database saved to[/] [bold cyan]{Path.GetFileName(outputPath)}[/]\n[dim]Size: {fileSize} KB[/]")
        )
        .BorderColor(Color.Green)
        .Padding(1, 0));

        AnsiConsole.WriteLine();
    }
}
