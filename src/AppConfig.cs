using System.Text.Json;

namespace Antty;

public class AppConfig
{
    public List<string> SelectedDocuments { get; set; } = new();
    public string ApiKey { get; set; } = "sk-YOUR-OPENAI-KEY-HERE";

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

    public static string GetKnowledgeBasePath(string documentPath)
    {
        // Create a centralized cache directory for knowledge bases
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antty",
            "cache"
        );

        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }

        // Create a unique filename based on the document's full path
        // This ensures different files with the same name don't collide
        var documentHash = GetStableHash(documentPath);
        var documentName = Path.GetFileNameWithoutExtension(documentPath);
        var safeFileName = $"{documentName}_{documentHash}_knowledge.json";

        return Path.Combine(cacheDir, safeFileName);
    }

    /// <summary>
    /// Generate a stable hash from a file path for cache identification
    /// </summary>
    private static string GetStableHash(string input)
    {
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input.ToLowerInvariant()));
            // Take first 8 bytes and convert to hex string for a shorter hash
            return BitConverter.ToString(hashBytes, 0, 8).Replace("-", "").ToLowerInvariant();
        }
    }
}
