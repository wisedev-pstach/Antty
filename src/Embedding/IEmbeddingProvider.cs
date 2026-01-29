namespace Antty.Embedding;

/// <summary>
/// Interface for embedding providers (OpenAI, Local GGUF, etc.)
/// </summary>
public interface IEmbeddingProvider : IDisposable
{
    /// <summary>
    /// Generate embedding for a single text
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(string text);

    /// <summary>
    /// Generate embeddings for multiple texts (batch processing)
    /// </summary>
    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts);

    /// <summary>
    /// Embedding dimensions (e.g., 512 for OpenAI, 768 for Nomic)
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Provider name (e.g., "openai", "local")
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Model name (e.g., "text-embedding-3-small", "nomic-embed-text-v1.5")
    /// </summary>
    string ModelName { get; }
}
