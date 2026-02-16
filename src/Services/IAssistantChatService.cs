using Antty.Core;
using Antty.Configuration;
using MaIN.Domain.Configuration;

namespace Antty.Services;

public interface IAssistantChatService
{
    Task TalkToAssistantAsync(
        AppConfig config,
        MultiBookSearchEngine multiSearchEngine,
        List<(string filePath, string kbPath)> documents,
        BackendType backendType,
        string modelName);
}
