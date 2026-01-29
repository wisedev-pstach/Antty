using OpenAI;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using System.Numerics.Tensors;
using System.Text.Json;
using Spectre.Console;

namespace Antty;

public class SearchEngine
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly List<RawChunk> _database;

    public SearchEngine(string apiKey, string dbPath)
    {
        var client = new OpenAIClient(apiKey);
        _embeddingClient = client.GetEmbeddingClient("text-embedding-3-small");

        if (!File.Exists(dbPath))
            throw new FileNotFoundException("Database not found! Run Ingestion first.");

        // Load Data into RAM
        List<RawChunk>? loadedDatabase = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan bold"))
            .Start($"[cyan]Loading knowledge base...[/]", ctx =>
            {
                var json = File.ReadAllText(dbPath);
                loadedDatabase = JsonSerializer.Deserialize<List<RawChunk>>(json);
            });

        _database = loadedDatabase ?? throw new InvalidOperationException("Failed to load database");

        AnsiConsole.MarkupLine($"[green]✓[/] Loaded [bold cyan]{_database.Count}[/] chunks into memory");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Searches the book for relevant content based on user question.
    /// This method handles: embedding the question, computing similarities, and returning top results.
    /// </summary>
    public async Task<List<RawSearchResult>> SearchBookAsync(string userQuestion)
    {
        // 1. Embed Question (Must match dimensions: 512)
        float[] queryVector = Array.Empty<float>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("yellow bold"))
            .StartAsync("[yellow]Analyzing question...[/]", async ctx =>
            {
                var response = await _embeddingClient.GenerateEmbeddingAsync(userQuestion, new EmbeddingGenerationOptions
                {
                    Dimensions = 512
                });
                queryVector = response.Value.ToFloats().ToArray();
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
