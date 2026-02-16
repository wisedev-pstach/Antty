using Antty.Core;
using Antty.Configuration;
using Antty.Embedding;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Antty.Services;

public class DocumentSearchService : IDocumentSearchService
{
    public async Task SearchDocumentsAsync(
        MultiBookSearchEngine multiSearchEngine,
        IEmbeddingProvider embeddingProvider,
        AppConfig config)
    {
        AnsiConsole.Write(new Rule("[bold cyan]🔍 SEARCH MODE[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Searching across {multiSearchEngine.LoadedDocumentCount} document(s)[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var query = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Search:[/] [dim](or 'exit' to return)[/]")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            var results = await multiSearchEngine.SearchAllAsync(query);

            AnsiConsole.WriteLine();

            if (results.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]⚠ No relevant data found.[/]");
            }
            else
            {
                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Cyan1)
                    .AddColumn(new TableColumn("[bold]Source[/]").Centered())
                    .AddColumn(new TableColumn("[bold]Page[/]").Centered())
                    .AddColumn(new TableColumn("[bold]Relevance[/]").Centered())
                    .AddColumn("[bold]Content[/]");

                foreach (var hit in results)
                {
                    var scoreColor = hit.Score switch
                    {
                        > 0.8 => "green",
                        > 0.6 => "yellow",
                        _ => "orange1"
                    };

                    var truncatedText = hit.Text.Length > 150
                        ? hit.Text.Substring(0, 147) + "..."
                        : hit.Text;

                    var escapedBookSource = Markup.Escape(hit.BookSource);
                    var escapedText = Markup.Escape(truncatedText);

                    table.AddRow(
                        $"[cyan]{escapedBookSource}[/]",
                        $"[dim]{hit.Page}[/]",
                        $"[{scoreColor}]{hit.Score:P1}[/]",
                        $"[dim]{escapedText}[/]"
                    );
                }

                AnsiConsole.Write(table);
            }

            AnsiConsole.WriteLine();
        }
    }
}
