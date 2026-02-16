namespace Antty.Embedding;

public interface IEmbeddingProvider : IDisposable
{
    Task<float[]> GenerateEmbeddingAsync(string text);

    Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts);

    int Dimensions { get; }

    string ProviderName { get; }

    string ModelName { get; }
}
