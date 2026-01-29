using OpenAI;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;

namespace Antty.Embedding;

/// <summary>
/// OpenAI embedding provider using Azure.AI.OpenAI SDK
/// </summary>
public class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingClient _client;
    private const string MODEL_NAME = "text-embedding-3-small";
    private const int EMBEDDING_DIMENSIONS = 512;

    public int Dimensions => EMBEDDING_DIMENSIONS;
    public string ProviderName => "openai";
    public string ModelName => MODEL_NAME;

    public OpenAIEmbeddingProvider(string apiKey)
    {
        var client = new OpenAIClient(apiKey);
        _client = client.GetEmbeddingClient(MODEL_NAME);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var response = await _client.GenerateEmbeddingAsync(text, new EmbeddingGenerationOptions
        {
            Dimensions = EMBEDDING_DIMENSIONS
        });

        return response.Value.ToFloats().ToArray();
    }

    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts)
    {
        var response = await _client.GenerateEmbeddingsAsync(texts, new EmbeddingGenerationOptions
        {
            Dimensions = EMBEDDING_DIMENSIONS
        });

        var embeddings = new List<float[]>();
        foreach (var embedding in response.Value)
        {
            embeddings.Add(embedding.ToFloats().ToArray());
        }

        return embeddings;
    }

    public void Dispose()
    {
        // EmbeddingClient doesn't require disposal
    }
}
