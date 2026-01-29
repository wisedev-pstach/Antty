using OpenAI;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using System.Numerics.Tensors;
using System.Text.Json;
using Spectre.Console;

namespace Antty;

/// <summary>
/// Search engine that manages multiple documents and aggregates search results
/// </summary>
public class MultiBookSearchEngine
{
    private readonly List<(string bookName, SearchEngine engine)> _engines = new();

    /// <summary>
    /// Load multiple documents into memory for searching
    /// </summary>
    public void LoadDocuments(string apiKey, List<(string bookPath, string knowledgeBasePath)> documents)
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
                var engine = new SearchEngine(apiKey, kbPath);
                var bookName = Path.GetFileNameWithoutExtension(bookPath);
                _engines.Add((bookName, engine));
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error loading {Path.GetFileName(bookPath)}: {ex.Message}[/]");
            }
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Loaded [bold cyan]{_engines.Count}[/] document(s) for searching");
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Search across all loaded documents and aggregate results
    /// </summary>
    public async Task<List<RawSearchResult>> SearchAllAsync(string userQuestion)
    {
        if (_engines.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ No documents loaded![/]");
            return new List<RawSearchResult>();
        }

        var allResults = new List<RawSearchResult>();

        // Search each document
        foreach (var (bookName, engine) in _engines)
        {
            var results = await engine.SearchBookAsync(userQuestion);
            
            // Add book source to results
            foreach (var result in results)
            {
                result.BookSource = bookName;
                allResults.Add(result);
            }
        }

        // Sort all results by score and return top 10
        return allResults
            .OrderByDescending(x => x.Score)
            .Take(10)
            .ToList();
    }

    public int LoadedDocumentCount => _engines.Count;
}
