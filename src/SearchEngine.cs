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

        KnowledgeBase? loadedKB = null;
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan bold"))
            .Start($"[cyan]Loading knowledge base...[/]", ctx =>
            {
                var json = File.ReadAllText(dbPath);

                try
                {
                    loadedKB = JsonSerializer.Deserialize<KnowledgeBase>(json);
                }
                catch (JsonException)
                {
                    try
                    {
                        var oldFormatChunks = JsonSerializer.Deserialize<List<RawChunk>>(json);
                        if (oldFormatChunks != null)
                        {
                            AnsiConsole.MarkupLine("[yellow]⚠ Migrating old knowledge base format...[/]");
                            loadedKB = new KnowledgeBase
                            {
                                Metadata = new KnowledgeBaseMetadata
                                {
                                    Provider = provider.ProviderName,
                                    ModelName = provider.ModelName,
                                    Dimensions = provider.Dimensions,
                                    CreatedAt = DateTime.UtcNow
                                },
                                Chunks = oldFormatChunks
                            };

                            var newJson = JsonSerializer.Serialize(loadedKB, new JsonSerializerOptions { WriteIndented = true });
                            File.WriteAllText(dbPath, newJson);
                            AnsiConsole.MarkupLine("[green]✓[/] Migrated to new format");
                        }
                    }
                    catch
                    {
                        throw new InvalidOperationException("Invalid knowledge base format");
                    }
                }
            });

        if (loadedKB == null)
            throw new InvalidOperationException("Failed to load database");

        _metadata = loadedKB.Metadata;
        _database = loadedKB.Chunks;

        if (_metadata.Provider != provider.ProviderName)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ Warning: Knowledge base was created with '{_metadata.Provider}' but using '{provider.ProviderName}' provider[/]");
            AnsiConsole.MarkupLine($"[yellow]  Embeddings may not be compatible. Consider rebuilding the knowledge base.[/]");
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Loaded [bold cyan]{_database.Count}[/] chunks [dim]({_metadata.Provider}/{_metadata.ModelName})[/]");
        AnsiConsole.WriteLine();
    }

    public async Task<List<RawSearchResult>> SearchBookAsync(string userQuestion, bool silent = false)
    {
        float[] queryVector = Array.Empty<float>();

        if (silent)
        {
            queryVector = await _embeddingProvider.GenerateEmbeddingAsync(userQuestion);
        }
        else
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("yellow bold"))
                .StartAsync("[yellow]Analyzing question...[/]", async ctx =>
                {
                    queryVector = await _embeddingProvider.GenerateEmbeddingAsync(userQuestion);
                });
        }

        if (queryVector.Length != _metadata.Dimensions)
        {
            throw new InvalidOperationException(
                $"Dimension mismatch! Query vector is {queryVector.Length}D but knowledge base expects {_metadata.Dimensions}D.\n" +
                $"Knowledge base was created with: {_metadata.Provider}/{_metadata.ModelName}\n" +
                $"Current provider: {_embeddingProvider.ProviderName}/{_embeddingProvider.ModelName}\n" +
                $"Please rebuild the knowledge base with the current embedding provider.");
        }

        var results = new List<RawSearchResult>();

        foreach (var chunk in _database)
        {
            double similarity = TensorPrimitives.CosineSimilarity(chunk.Vector, queryVector);

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

        return results
            .OrderByDescending(x => x.Score)
            .Take(5)
            .ToList();
    }
}
