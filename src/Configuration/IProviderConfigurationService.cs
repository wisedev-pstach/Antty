using MaIN.Domain.Configuration;

namespace Antty.Configuration;

public interface IProviderConfigurationService
{
    Task<(Antty.Embedding.IEmbeddingProvider? embeddingProvider, BackendType backendType, string modelName)>
        ConfigureProvidersAsync(AppConfig config);
}
