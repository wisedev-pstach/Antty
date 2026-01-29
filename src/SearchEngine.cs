using Antty.Embedding;
using System.Numerics.Tensors;
using System.Text.Json;
using Spectre.Console;

namespace Antty;

public class SearchEngine
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly List<RawChunk> _database;
    private readonly KnowledgeBaseMetadata _metadata;

    public SearchEngine(IEmbeddingProvider provider, string dbPath)
    {
        _embeddingProvider = provider;

        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Database not found! Run Ingestion first.");

        // Load Data into RAM
        KnowledgeBase? loadedKB = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan bold"))
            .Start($"[cyan]Loading knowledge base...[/]", ctx =>
            {
                var json = File.ReadAllText(dbPath);
                loadedKB = JsonSerializer.Deserialize<KnowledgeBase>(json);
            });

        if (loadedKB == null)
            throw new InvalidOperationException("Failed to load database");

        _metadata = loadedKB.Metadata;
        _database = loadedKB.Chunks;

        // Validate provider compatibility
        if (_metadata.Provider != provider.ProviderName)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ Warning: Knowledge base was created with '{_metadata.Provider}' but using '{provider.ProviderName}' provider[/]");
            AnsiConsole.MarkupLine($"[yellow]  Embeddings may not be compatible. Consider rebuilding the knowledge base.[/]");
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Loaded [bold cyan]{_database.Count}[/] chunks [dim]({_metadata.Provider}/{_metadata.ModelName})[/]");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Searches the book for relevant content based on user question.
    /// This method handles: embedding the question, computing similarities, and returning top results.
    /// </summary>
    public async Task<List<RawSearchResult>> SearchBookAsync(string userQuestion)
    {
        // 1. Embed Question
        float[] queryVector = Array.Empty<float>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow bold"))
            .StartAsync("[yellow]Analyzing question...[/]", async ctx =>
            {
                queryVector = await _embeddingProvider.GenerateEmbeddingAsync(userQuestion);
            });

        // 2. Vector Math Search
        var results = new List<RawSearchResult>();

        foreach (var chunk in _database)
        {
            // Cosine Similarity: 1.0 is identical, 0.0 is unrelated
            double similarity = TensorPrimitives.CosineSimilarity(chunk.Vector, queryVector);

            // 3. Threshold Filtering (0.45 eliminates most irrelevant noise)
            if (similarity > 0.45)
            {
                results.Add(new RawSearchResult
                {
                    Text = chunk.Content,
                    Page = chunk.PageNumber,
                    Score = similarity
                });
            }
        }

        // 4. Return Top 5 Sorted
        return results
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToList();
    }
}
