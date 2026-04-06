using Antty.Core;
using Antty.Configuration;
namespace Antty.Services;

public interface IDocumentSearchService
{
    Task SearchDocumentsAsync(
        MultiBookSearchEngine multiSearchEngine,
        Embedding.IEmbeddingProvider embeddingProvider,
        AppConfig config);
}
