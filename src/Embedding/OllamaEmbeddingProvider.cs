using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace Antty.Embedding;

/// <summary>
/// Ollama-based embedding provider using nomic-embed-text
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private readonly string _baseUrl;
    private int _dimensions;

    public int Dimensions => _dimensions;
    public string ProviderName => "ollama";
    public string ModelName => _modelName;

    public OllamaEmbeddingProvider(string modelName = "nomic-embed-text", string baseUrl = "http://localhost:11434")
    {
        _modelName = modelName;
        _baseUrl = baseUrl;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Test connection and get dimensions
        try
        {
            AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan bold"))
                .Start($"[cyan]Connecting to Ollama ({_modelName})...[/]", ctx =>
                {
                    var testEmbedding = GenerateEmbeddingAsync("test").Result;
                    _dimensions = testEmbedding.Length;
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Ollama embeddings ready: [cyan]{_dimensions}[/] dimensions");
            AnsiConsole.WriteLine();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to connect to Ollama: {ex.Message}", ex);
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var requestBody = new
        {
            model = _modelName,
            prompt = text
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/embeddings", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaEmbeddingResponse>(responseJson);

            if (result?.embedding == null || result.embedding.Length == 0)
            {
                throw new InvalidOperationException("Ollama returned empty embedding");
            }

            return result.embedding;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to generate embedding with Ollama: {ex.Message}", ex);
        }
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
    {
        var embeddings = new List<float[]>();

        // Process sequentially to avoid overwhelming Ollama
        foreach (var text in texts)
        {
            var embedding = await GenerateEmbeddingAsync(text);
            embeddings.Add(embedding);
        }

        return embeddings;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    private class OllamaEmbeddingResponse
    {
        public float[] embedding { get; set; } = Array.Empty<float>();
    }
}
