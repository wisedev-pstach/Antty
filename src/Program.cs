using Antty;
using Spectre.Console;

class Program
{
    static async Task Main(string[] args)
    {
        // Display fancy header
        AnsiConsole.Write(
            new FigletText("Antty")
                .Centered()
                .Color(Color.Cyan1));

        AnsiConsole.Write(new Rule("[dim]Semantic Search powered by OpenAI[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        // Load configuration
        var config = AppConfig.Load();

        // Check if API key is configured
        if (config.ApiKey == "sk-YOUR-OPENAI-KEY-HERE")
        {
            AnsiConsole.MarkupLine("[yellow]⚠ API Key not configured![/]");
            config.ApiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Enter your OpenAI API Key:[/]")
                    .PromptStyle("green")
                    .Secret());
            config.Save();
            AnsiConsole.MarkupLine("[green]✓[/] API Key saved!");
            AnsiConsole.WriteLine();
        }

        // Main loop
        bool running = true;
        while (running)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "📚 Select/Load Book",
                        "🔨 Build Knowledge Base",
                        "🔍 Search Book",
                        "⚙️  Settings",
                        "❌ Exit"
                    }));

            AnsiConsole.WriteLine();

            if (choice.StartsWith("📚"))
            {
                await SelectBookAsync(config);
            }
            else if (choice.StartsWith("🔨"))
            {
                await BuildKnowledgeBaseAsync(config);
            }
            else if (choice.StartsWith("🔍"))
            {
                await SearchBookAsync(config);
            }
            else if (choice.StartsWith("⚙"))
            {
                await SettingsMenuAsync(config);
            }
            else if (choice.StartsWith("❌"))
            {
                running = false;
            }

            if (running)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule().RuleStyle("dim"));
                AnsiConsole.WriteLine();
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Goodbye! 👋[/]");
    }

    static async Task SelectBookAsync(AppConfig config)
    {
        var booksFolder = Path.Combine(Directory.GetCurrentDirectory(), "books");

        if (!Directory.Exists(booksFolder))
        {
            Directory.CreateDirectory(booksFolder);
        }

        var pdfFiles = Directory.GetFiles(booksFolder, "*.pdf");

        if (pdfFiles.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ No PDF files found in the books folder![/]");
            AnsiConsole.MarkupLine($"[dim]Please add PDF files to: {booksFolder}[/]");

            var manualPath = AnsiConsole.Confirm("Would you like to specify a custom path?");
            if (manualPath)
            {
                var path = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan]Enter the full path to your PDF:[/]")
                        .PromptStyle("green"));

                if (File.Exists(path))
                {
                    config.LastBookPath = path;
                    config.LastKnowledgeBasePath = AppConfig.GetKnowledgeBasePath(path);
                    config.Save();
                    AnsiConsole.MarkupLine($"[green]✓[/] Book selected: [cyan]{Path.GetFileName(path)}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]❌ File not found![/]");
                }
            }
            return;
        }

        var bookChoices = pdfFiles.Select(Path.GetFileName).ToList()!;
        bookChoices.Add("[dim]Enter custom path...[/]");

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Select a book:[/]")
                .PageSize(15)
                .AddChoices(bookChoices));

        if (selected.Contains("custom path"))
        {
            var path = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Enter the full path to your PDF:[/]")
                    .PromptStyle("green"));

            if (File.Exists(path))
            {
                config.LastBookPath = path;
                config.LastKnowledgeBasePath = AppConfig.GetKnowledgeBasePath(path);
                config.Save();
                AnsiConsole.MarkupLine($"[green]✓[/] Book selected: [cyan]{Path.GetFileName(path)}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine("[red]❌ File not found![/]");
            }
        }
        else
        {
            var fullPath = Path.Combine(booksFolder, selected);
            config.LastBookPath = fullPath;
            config.LastKnowledgeBasePath = AppConfig.GetKnowledgeBasePath(fullPath);
            config.Save();
            AnsiConsole.MarkupLine($"[green]✓[/] Book selected: [cyan]{selected}[/]");
        }

        await Task.CompletedTask;
    }

    static async Task BuildKnowledgeBaseAsync(AppConfig config)
    {
        if (string.IsNullOrEmpty(config.LastBookPath) || !File.Exists(config.LastBookPath))
        {
            AnsiConsole.MarkupLine("[red]❌ No book selected! Please select a book first.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[cyan]Building knowledge base for:[/] [yellow]{Path.GetFileName(config.LastBookPath)}[/]");
        AnsiConsole.WriteLine();

        await IngestionBuilder.BuildDatabaseAsync(
            config.LastBookPath,
            config.ApiKey,
            config.LastKnowledgeBasePath!);

        config.Save();
    }

    static async Task SearchBookAsync(AppConfig config)
    {
        if (string.IsNullOrEmpty(config.LastBookPath) || !File.Exists(config.LastBookPath))
        {
            AnsiConsole.MarkupLine("[red]❌ No book selected! Please select a book first.[/]");
            return;
        }

        if (string.IsNullOrEmpty(config.LastKnowledgeBasePath) || !File.Exists(config.LastKnowledgeBasePath))
        {
            AnsiConsole.MarkupLine("[red]❌ Knowledge base not found! Please build it first.[/]");
            return;
        }

        try
        {
            var engine = new SearchEngine(config.ApiKey, config.LastKnowledgeBasePath);

            AnsiConsole.MarkupLine($"[dim]Searching in: {Path.GetFileName(config.LastBookPath)}[/]");
            AnsiConsole.WriteLine();

            while (true)
            {
                var query = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan]Ask a question[/] [dim](or 'exit' to return)[/]:")
                        .AllowEmpty());

                if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                var results = await engine.SearchBookAsync(query);

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

                        var truncatedText = hit.Text.Length > 200
                            ? hit.Text.Substring(0, 197) + "..."
                            : hit.Text;

                        table.AddRow(
                            $"[cyan]{hit.Page}[/]",
                            $"[{scoreColor}]{hit.Score:P1}[/]",
                            $"[dim]{truncatedText}[/]"
                        );
                    }

                    AnsiConsole.Write(table);
                }

                AnsiConsole.WriteLine();
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Error: {ex.Message}[/]");
        }
    }

    static async Task SettingsMenuAsync(AppConfig config)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Settings[/]")
                .AddChoices(new[] {
                    "🔑 Update API Key",
                    "📂 Show Current Book",
                    "🔙 Back"
                }));

        if (choice.StartsWith("🔑"))
        {
            config.ApiKey = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Enter your OpenAI API Key:[/]")
                    .PromptStyle("green")
                    .Secret());
            config.Save();
            AnsiConsole.MarkupLine("[green]✓[/] API Key updated!");
        }
        else if (choice.StartsWith("📂"))
        {
            if (!string.IsNullOrEmpty(config.LastBookPath))
            {
                AnsiConsole.MarkupLine($"[cyan]Current Book:[/] {Path.GetFileName(config.LastBookPath)}");
                AnsiConsole.MarkupLine($"[dim]Path: {config.LastBookPath}[/]");
                if (!string.IsNullOrEmpty(config.LastKnowledgeBasePath))
                {
                    var kbExists = File.Exists(config.LastKnowledgeBasePath);
                    var status = kbExists ? "[green]✓ Built[/]" : "[yellow]⚠ Not built[/]";
                    AnsiConsole.MarkupLine($"[cyan]Knowledge Base:[/] {status}");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]⚠ No book selected[/]");
            }
        }

        await Task.CompletedTask;
    }
}
