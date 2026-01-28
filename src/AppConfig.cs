using System.Text.Json;

namespace Antty;

public class AppConfig
{
    public string? LastBookPath { get; set; }
    public string? LastKnowledgeBasePath { get; set; }
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

    public static string GetKnowledgeBasePath(string bookPath)
    {
        var bookName = Path.GetFileNameWithoutExtension(bookPath);
        return Path.Combine(
            Path.GetDirectoryName(bookPath) ?? "",
            $"{bookName}_knowledge.json"
        );
    }
}
