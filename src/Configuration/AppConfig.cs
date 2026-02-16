using System.Text.Json;

namespace Antty.Configuration;

public class AppConfig
{
    public List<string> SelectedDocuments { get; set; } = new();
    public string ApiKey { get; set; } = "sk-YOUR-OPENAI-KEY-HERE";
    public string AnthropicKey { get; set; } = "";
    public string GeminiKey { get; set; } = "";
    public string DeepSeekKey { get; set; } = "";
    public string GroqKey { get; set; } = "";
    public string XaiKey { get; set; } = "";

    public string ChatBackend { get; set; } = "OpenAi";
    public string ChatModel { get; set; } = "gpt-4o";

    public string EmbeddingProvider { get; set; } = "openai";
    public string LocalModelPath { get; set; } = "";

    public List<string> CustomOllamaModels { get; set; } = new();

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Antty",
        "config.json"
    );

    public static AppConfig Load()
    {
        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        return new AppConfig();
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public static string GetKnowledgeBasePath(string documentPath, string provider)
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antty",
            "cache"
        );

        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }

        var documentHash = GetStableHash(documentPath);
        var documentName = Path.GetFileNameWithoutExtension(documentPath);
        var safeFileName = $"{documentName}_{documentHash}_{provider}_knowledge.json";

        return Path.Combine(cacheDir, safeFileName);
    }
    private static string GetStableHash(string input)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input.ToLowerInvariant()));
            return BitConverter.ToString(hashBytes, 0, 8).Replace("-", "").ToLowerInvariant();
        }
    }
}
