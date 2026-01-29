using LLama;
using LLama.Common;
using LLama.Native;
using Spectre.Console;

namespace Antty.Embedding;

/// <summary>
/// Local GGUF embedding provider using LLamaSharp
/// </summary>
public class LocalEmbeddingProvider : IEmbeddingProvider
{
    private LLamaEmbedder _embedder = null!;
    private LLamaWeights _weights = null!;
    private readonly string _modelName;
    private int _dimensions;

    public int Dimensions => _dimensions;
    public string ProviderName => "local";
    public string ModelName => _modelName;

    public LocalEmbeddingProvider(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"GGUF model not found at: {modelPath}");

        _modelName = Path.GetFileNameWithoutExtension(modelPath);

        var modelParams = new ModelParams(modelPath)
        {
            PoolingType = LLamaPoolingType.Mean,  // Combine embeddings into single vector
            GpuLayerCount = 0  // CPU-only
        };

        try
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan bold"))
                .Start($"[cyan]Loading local model: {_modelName}...[/]", ctx =>
                {
                    _weights = LLamaWeights.LoadFromFile(modelParams);
                    _embedder = new LLamaEmbedder(_weights, modelParams);

                    // Get dimensions by generating a test embedding
                    var testEmbeddings = _embedder.GetEmbeddings("test").Result;
                    var testEmbedding = testEmbeddings.Single(); // Should be single vector with Mean pooling
                    _dimensions = testEmbedding.Length;
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Model loaded: [cyan]{_dimensions}[/] dimensions");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            _weights?.Dispose();
            throw new InvalidOperationException($"Failed to load GGUF model: {ex.Message}", ex);
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var embeddings = await _embedder.GetEmbeddings(text);
        return embeddings.Single(); // Should be single vector with Mean pooling
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
    {
        var embeddings = new List<float[]>();

        // LLamaSharp doesn't have built-in batch support, process sequentially
        foreach (var text in texts)
        {
            var embedding = await _embedder.GetEmbeddings(text);
            embeddings.Add(embedding.Single());
        }

        return embeddings;
    }

    public void Dispose()
    {
        _weights?.Dispose();
    }
}
