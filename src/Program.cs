using Antty;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MaIN.Core;

class Program
{
    static async Task Main(string[] args)
    {
        // Enable UTF-8 encoding for emoji support
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Initialize MaIN.NET framework for console apps
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddMaIN(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.UseMaIN();

        // Display fancy header
        AnsiConsole.Write(
            new FigletText("Antty")
                .Centered()
                .Color(Color.Cyan1));

        AnsiConsole.Write(new Rule("[dim]Semantic Search powered by MaIN.NET[/]").RuleStyle("dim"));
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

        // 1. Scan current directory for supported documents
        var currentDir = Directory.GetCurrentDirectory();
        var supportedExtensions = new[] { ".pdf", ".txt", ".md", ".json" };
        var availableFiles = supportedExtensions
            .SelectMany(ext => Directory.GetFiles(currentDir, $"*{ext}"))
            .ToList();

        if (availableFiles.Count == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠ No supported documents found in:[/] [dim]{currentDir}[/]");
            AnsiConsole.MarkupLine("[dim]Supported formats: PDF, TXT, MD, JSON[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to exit...[/]");
            Console.ReadKey();
            return;
        }

        AnsiConsole.MarkupLine($"[cyan]Found {availableFiles.Count} document(s) in:[/] [dim]{currentDir}[/]");
        AnsiConsole.WriteLine();

        // Check which files already have knowledge bases
        var fileChoices = availableFiles.Select(filePath =>
        {
            var fileName = Path.GetFileName(filePath);
            var kbPath = AppConfig.GetKnowledgeBasePath(filePath);
            var hasKB = File.Exists(kbPath);
            return new
            {
                FilePath = filePath,
                DisplayName = hasKB ? $"{fileName} [green]✓ (indexed)[/]" : fileName,
                HasKB = hasKB
            };
        }).ToList();

        // Multi-select documents
        var selectedDisplayNames = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cyan]Select documents to load:[/]")
                .PageSize(15)
                .Required()
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm | [green]✓[/] = already indexed)[/]")
                .AddChoices(fileChoices.Select(f => f.DisplayName)!));

        // Map back to file paths (strip markup for comparison)
        var selectedPaths = selectedDisplayNames
            .Select(displayName =>
            {
                // Remove markup to match original filename
                var cleanName = displayName
                    .Replace(" [green]✓ (indexed)[/]", "")
                    .Trim();
                return fileChoices.FirstOrDefault(f => Path.GetFileName(f.FilePath) == cleanName)?.FilePath;
            })
            .Where(path => path != null)
            .Select(path => path!)
            .ToList();

        config.SelectedDocuments = selectedPaths;
        config.Save();

        AnsiConsole.WriteLine();

        // 3. Build knowledge bases if missing
        var documentsToProcess = new List<(string filePath, string kbPath)>();
        int existingKBCount = 0;
        int newKBCount = 0;
        
        foreach (var filePath in selectedPaths)
        {
            var kbPath = AppConfig.GetKnowledgeBasePath(filePath);
            documentsToProcess.Add((filePath, kbPath));

            if (!File.Exists(kbPath))
            {
                newKBCount++;
                AnsiConsole.MarkupLine($"[yellow]⚙ Building knowledge base for:[/] [cyan]{Path.GetFileName(filePath)}[/]");
                AnsiConsole.WriteLine();

                await IngestionBuilder.BuildDatabaseAsync(filePath, config.ApiKey, kbPath);
            }
            else
            {
                existingKBCount++;
                AnsiConsole.MarkupLine($"[green]✓[/] Using cached knowledge base for [cyan]{Path.GetFileName(filePath)}[/]");
            }
        }

        if (existingKBCount > 0 || newKBCount > 0)
        {
            AnsiConsole.WriteLine();
            if (existingKBCount > 0)
                AnsiConsole.MarkupLine($"[dim]Used {existingKBCount} cached knowledge base(s)[/]");
            if (newKBCount > 0)
                AnsiConsole.MarkupLine($"[dim]Built {newKBCount} new knowledge base(s)[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[dim]Loading Documents[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        // 4. Load all documents into multi-search engine
        var multiEngine = new MultiBookSearchEngine();
        multiEngine.LoadDocuments(config.ApiKey, documentsToProcess);

        if (multiEngine.LoadedDocumentCount == 0)
        {
            AnsiConsole.MarkupLine("[red]❌ No documents could be loaded. Exiting.[/]");
            return;
        }

        // 5. Main menu loop
        bool running = true;
        while (running)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("dim"));
            AnsiConsole.WriteLine();

            // Show loaded documents info
            AnsiConsole.MarkupLine($"[dim]Loaded documents: {multiEngine.LoadedDocumentCount}[/]");
            
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "💬 Talk to Assistant",
                        "🔍 Search Documents",
                        "📚 Reload/Change Documents",
                        "⚙️  Settings",
                        "❌ Exit"
                    }));

            AnsiConsole.WriteLine();

            if (choice.StartsWith("💬"))
            {
                await TalkToAssistantAsync(config.ApiKey, multiEngine, documentsToProcess);
            }
            else if (choice.StartsWith("🔍"))
            {
                await SearchDocumentsAsync(multiEngine);
            }
            else if (choice.StartsWith("📚"))
            {
                AnsiConsole.MarkupLine("[yellow]Restarting to select documents...[/]");
                AnsiConsole.WriteLine();
                // Restart the application by returning and letting Main run again
                return;
            }
            else if (choice.StartsWith("⚙"))
            {
                await SettingsMenuAsync(config);
            }
            else if (choice.StartsWith("❌"))
            {
                running = false;
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Goodbye! 👋[/]");
    }

    static async Task SearchDocumentsAsync(MultiBookSearchEngine multiEngine)
    {
        AnsiConsole.Write(new Rule("[bold cyan]🔍 SEARCH MODE[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Searching across {multiEngine.LoadedDocumentCount} document(s)[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var query = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]Search:[/] [dim](or 'exit' to return)[/]")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            var results = await multiEngine.SearchAllAsync(query);

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

                    table.AddRow(
                        $"[cyan]{hit.BookSource}[/]",
                        $"[dim]{hit.Page}[/]",
                        $"[{scoreColor}]{hit.Score:P1}[/]",
                        $"[dim]{truncatedText}[/]"
                    );
                }

                AnsiConsole.Write(table);
            }

            AnsiConsole.WriteLine();
        }
    }

    static async Task TalkToAssistantAsync(string apiKey, MultiBookSearchEngine multiEngine, List<(string filePath, string kbPath)> documents)
    {
        AnsiConsole.Write(new Rule("[bold cyan]💬 TALK TO ASSISTANT[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        // Initialize the assistant
        AnsiConsole.MarkupLine("[dim]Initializing AI assistant...[/]");
        try
        {
            await DocumentAssistant.Initialize(apiKey, multiEngine, documents);
            AnsiConsole.MarkupLine("[green]✓[/] Assistant ready!");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to initialize assistant: {ex.Message}[/]");
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]Loaded {multiEngine.LoadedDocumentCount} document(s). Ask me anything about them![/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var userMessage = AnsiConsole.Prompt(
                new TextPrompt<string>("[cyan]You:[/] [dim](or 'exit' to return)[/]")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(userMessage) || userMessage.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            AnsiConsole.WriteLine();

            try
            {
                bool firstToken = true;
                var thinkingTask = Task.Run(async () =>
                {
                    // Show thinking animation
                    var dots = "";
                    while (firstToken)
                    {
                        AnsiConsole.Markup($"\r[bold green]Assistant:[/] [dim]thinking{dots}[/]   ");
                        dots = dots.Length >= 3 ? "" : dots + ".";
                        await Task.Delay(400);
                    }
                });

                await foreach (var token in DocumentAssistant.ChatAsync(userMessage))
                {
                    if (firstToken)
                    {
                        // Stop animation and clear the line
                        firstToken = false;
                        await Task.Delay(100); // Let animation stop
                        AnsiConsole.Markup("\r                                        \r"); // Clear line
                        AnsiConsole.Markup($"[bold green]Assistant:[/] ");
                    }
                    
                    AnsiConsole.Markup($"[green]{token.EscapeMarkup()}[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"\n[red]Error: {ex.Message}[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();
        }
    }

    static async Task SettingsMenuAsync(AppConfig config)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Settings[/]")
                .AddChoices(new[] {
                    "🔑 Update API Key",
                    "📂 Show Loaded Documents",
                    "🗑️  Clear Knowledge Base Cache",
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
            if (config.SelectedDocuments.Count > 0)
            {
                AnsiConsole.MarkupLine("[cyan]Currently loaded documents:[/]");
                foreach (var doc in config.SelectedDocuments)
                {
                    AnsiConsole.MarkupLine($"  [dim]•[/] {Path.GetFileName(doc)}");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]⚠ No documents loaded[/]");
            }
        }
        else if (choice.StartsWith("🗑"))
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Antty",
                "cache"
            );

            if (Directory.Exists(cacheDir))
            {
                var confirm = AnsiConsole.Confirm("[yellow]Are you sure you want to clear all cached knowledge bases?[/]");
                if (confirm)
                {
                    Directory.Delete(cacheDir, true);
                    AnsiConsole.MarkupLine("[green]✓[/] Cache cleared!");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]Cache is already empty[/]");
            }
        }

        await Task.CompletedTask;
    }
}
