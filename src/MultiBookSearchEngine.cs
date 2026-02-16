using Antty.Embedding;
using Spectre.Console;

namespace Antty;

/// <summary>
/// Search engine that manages multiple documents and aggregates search results
/// </summary>
public class MultiBookSearchEngine
{
    private readonly List<(string bookName, SearchEngine engine)> _engines = new();

    public void LoadDocuments(IEmbeddingProvider provider, List<(string bookPath, string knowledgeBasePath)> documents)
    {
        _engines.Clear();

        foreach (var (bookPath, kbPath) in documents)
        {
            if (!File.Exists(kbPath))
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Knowledge base not found for {Path.GetFileName(bookPath)}[/]");
                continue;
            }

            try
            {
                var engine = new SearchEngine(provider, kbPath);
                var bookName = Path.GetFileNameWithoutExtension(bookPath);
                _engines.Add((bookName, engine));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error loading {Path.GetFileName(bookPath)}: {ex.Message}[/]");
            }
        }

    }

    public async Task<List<RawSearchResult>> SearchAllAsync(string userQuestion, bool silent = false)
    {
        if (_engines.Count == 0)
        {
            if (!silent) AnsiConsole.MarkupLine("[yellow]⚠ No documents loaded![/]");
            return new List<RawSearchResult>();
        }

        var allResults = new List<RawSearchResult>();

        foreach (var (bookName, engine) in _engines)
        {
            var results = await engine.SearchBookAsync(userQuestion, silent);

            foreach (var result in results)
            {
                result.BookSource = bookName;
                allResults.Add(result);
            }
        }

        return allResults
            .OrderByDescending(x => x.Score)
            .Take(10)
            .ToList();
    }

    public int LoadedDocumentCount => _engines.Count;
}
