using Antty;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MaIN.Core;
using MaIN.Core.Hub;
using MaIN.Domain.Configuration;

partial class Program
{
    static async Task Main(string[] args)
    {
        // Enable UTF-8 encoding for emoji support
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddMaIN(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var serviceProvider = services.BuildServiceProvider();
        serviceProvider.UseMaIN();

        AIHub.Extensions.DisableLLamaLogs();

        // Display fancy header
        AnsiConsole.Write(
            new FigletText("Antty")
                .Centered()
                .Color(Color.Cyan1));

        AnsiConsole.Write(new Rule("[dim]Semantic Search powered by MaIN.NET[/]").RuleStyle("dim"));
        AnsiConsole.WriteLine();

        // Load configuration
        var config = AppConfig.Load();

        // Configure providers and models (can be called again when switching)
        var (embeddingProvider, backendType, localModelName) = await ConfigureProvidersAsync(config);

        if (embeddingProvider == null)
        {
            AnsiConsole.MarkupLine("[red]Configuration failed. Exiting.[/]");
            return;
        }

        AnsiConsole.WriteLine();

        // 3. Scan current directory for supported documents
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
            var kbPath = AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider);
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

        // 4. Build knowledge bases if missing
        var documentsToProcess = new List<(string filePath, string kbPath)>();
        int existingKBCount = 0;
        int newKBCount = 0;

        foreach (var filePath in selectedPaths)
        {
            var kbPath = AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider);
            documentsToProcess.Add((filePath, kbPath));

            if (!File.Exists(kbPath))
            {
                newKBCount++;
                AnsiConsole.MarkupLine($"[yellow]⚙ Building knowledge base for:[/] [cyan]{Path.GetFileName(filePath)}[/]");
                AnsiConsole.WriteLine();

                await IngestionBuilder.BuildDatabaseAsync(filePath, embeddingProvider, kbPath);
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

        // 5. Load all documents into multi-search engine
        var multiEngine = new MultiBookSearchEngine();
        multiEngine.LoadDocuments(embeddingProvider, documentsToProcess);

        if (multiEngine.LoadedDocumentCount == 0)
        {
            AnsiConsole.MarkupLine("[red]❌ No documents could be loaded. Exiting.[/]");
            return;
        }

        // 6. Main menu loop
        bool running = true;

        // Prompt to start assistant removed per user request

        while (running)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("dim"));
            AnsiConsole.WriteLine();

            // Show loaded documents info
            AnsiConsole.MarkupLine($"[dim]Loaded documents: {multiEngine.LoadedDocumentCount}[/]");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[cyan]What would you like to do?[/] [dim](Embeddings: {config.EmbeddingProvider} | Model: {config.ChatBackend} - {config.ChatModel})[/]")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "💬 Talk to Assistant",
                        "🔍 Search Documents",
                        "🔧 Switch Local/Cloud",
                        "📚 Reload/Change Documents",
                        "⚙️  Settings",
                        "❌ Exit"
                    }));

            AnsiConsole.WriteLine();

            if (choice.StartsWith("💬"))
            {
                await TalkToAssistantAsync(config, multiEngine, documentsToProcess, backendType, localModelName);
            }
            else if (choice.StartsWith("🔍"))
            {
                await SearchDocumentsAsync(multiEngine);
            }
            else if (choice.StartsWith("🔧"))
            {
                // Switch embedding provider - full reconfiguration
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Switching will reconfigure both embedding provider and AI model...[/]");

                var shouldSwitch = AnsiConsole.Confirm("[cyan]Continue?[/]", true);
                if (!shouldSwitch)
                {
                    continue;
                }

                // Remember old provider to check if embeddings changed
                var oldEmbeddingProvider = config.EmbeddingProvider;

                // Dispose old embedding provider
                embeddingProvider?.Dispose();

                // Run full configuration again
                var (newEmbeddingProvider, newBackendType, newLocalModelName) = await ConfigureProvidersAsync(config);

                if (newEmbeddingProvider == null)
                {
                    AnsiConsole.MarkupLine("[red]Configuration failed. Keeping previous settings.[/]");
                    continue;
                }

                // Update configuration
                embeddingProvider = newEmbeddingProvider;
                backendType = newBackendType;
                localModelName = newLocalModelName;

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green]✓[/] Provider switched successfully!");
                AnsiConsole.WriteLine();

                // Check if any selected documents are missing the index for the NEW provider
                var missingIndicesCount = config.SelectedDocuments
                    .Count(filePath => !File.Exists(AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider)));

                if (missingIndicesCount > 0)
                {
                    // Some or all indices are missing
                    AnsiConsole.MarkupLine($"[yellow]⚠[/] Embedding provider changed. {missingIndicesCount} document(s) need to be indexed for [cyan]{config.EmbeddingProvider}[/].");
                    var shouldReload = AnsiConsole.Confirm("[cyan]Index missing documents now?[/]", true);

                    if (shouldReload)
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.Write(new Rule("[dim]Loading Knowledge Bases[/]").RuleStyle("dim"));
                        AnsiConsole.WriteLine();

                        var newDocumentsToProcess = new List<(string filePath, string kbPath)>();
                        int rebuilt = 0;
                        int usedCache = 0;

                        foreach (var filePath in config.SelectedDocuments)
                        {
                            var kbPath = AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider);
                            newDocumentsToProcess.Add((filePath, kbPath));

                            if (!File.Exists(kbPath))
                            {
                                AnsiConsole.MarkupLine($"[yellow]⚙ Indexing:[/] [cyan]{Path.GetFileName(filePath)}[/]");
                                await IngestionBuilder.BuildDatabaseAsync(filePath, embeddingProvider, kbPath);
                                rebuilt++;
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"[green]✓[/] Using existing cache for:[/] [cyan]{Path.GetFileName(filePath)}[/]");
                                usedCache++;
                            }
                        }

                        AnsiConsole.WriteLine();
                        if (rebuilt > 0)
                            AnsiConsole.MarkupLine($"[green]✓[/] Indexed {rebuilt} new document(s) with {config.EmbeddingProvider} embeddings");
                        if (usedCache > 0)
                            AnsiConsole.MarkupLine($"[dim]✓ Used {usedCache} existing {config.EmbeddingProvider} cache(s)[/]");
                        AnsiConsole.WriteLine();

                        // Reload multiEngine with new KBs
                        AnsiConsole.Write(new Rule("[dim]Reloading Documents[/]").RuleStyle("dim"));
                        AnsiConsole.WriteLine();

                        multiEngine.LoadDocuments(embeddingProvider, newDocumentsToProcess);
                        documentsToProcess = newDocumentsToProcess;

                        AnsiConsole.MarkupLine($"[green]✓[/] Loaded {multiEngine.LoadedDocumentCount} document(s)");
                    }
                    else
                    {
                        // User chose not to re-index missing ones, but we should still reload what we have
                        // (MultiBookSearchEngine handles missing files gracefully or we just reload old mappings)
                        AnsiConsole.MarkupLine("[dim]Skipping re-indexing. Search might not work correctly for unindexed files.[/]");
                        var partialDocuments = config.SelectedDocuments
                            .Select(filePath => (filePath, AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider)))
                            .ToList();

                        multiEngine.LoadDocuments(embeddingProvider, partialDocuments);
                        documentsToProcess = partialDocuments;
                    }
                }
                else
                {
                    // All indices exist! Just reload silently or with a quick check mark
                    AnsiConsole.MarkupLine($"[green]✓[/] All documents already have indices for [cyan]{config.EmbeddingProvider}[/]. Reloading...");

                    var cachedDocuments = config.SelectedDocuments
                        .Select(filePath => (filePath, AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider)))
                        .ToList();

                    multiEngine.LoadDocuments(embeddingProvider, cachedDocuments);
                    documentsToProcess = cachedDocuments;

                    AnsiConsole.MarkupLine($"[green]✓[/] Loaded {multiEngine.LoadedDocumentCount} document(s)");
                }

                // Prompt to start assistant removed per user request
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

    static async Task TalkToAssistantAsync(AppConfig config, MultiBookSearchEngine multiEngine, List<(string filePath, string kbPath)> documents, BackendType backendType, string modelName)
    {
        AnsiConsole.Write(new Rule("[bold cyan]💬 TALK TO ASSISTANT[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        // Initialize the assistant
        AnsiConsole.MarkupLine("[dim]Initializing AI assistant...[/]");
        try
        {
            await DocumentAssistant.Initialize(config, multiEngine, documents, backendType, modelName);
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

            var firstContentSignal = new TaskCompletionSource<bool>();
            var pendingLogs = new List<string>();
            var logLock = new object();
            var printer = new StreamingMarkdownPrinter(() => firstContentSignal.TrySetResult(true));
            using var cts = new CancellationTokenSource();
            var responseEnumeration = DocumentAssistant.ChatAsync(userMessage, cts.Token).GetAsyncEnumerator();

            void FlushToolLogs()
            {
                lock (logLock)
                {
                    if (pendingLogs.Count == 0) return;

                    var table = new Table().HideHeaders().NoBorder();
                    table.AddColumn("content");
                    foreach (var log in pendingLogs)
                    {
                        // Foolproof strip of all residual [tags] to prevent literal markup visibility
                        var clean = System.Text.RegularExpressions.Regex.Replace(log, @"\[.*?\]", "");
                        clean = clean.Trim();

                        // Using a dark grey and dimming to make it feel 'smaller' and less intrusive
                        table.AddRow(new Text(clean, new Style(foreground: Color.Grey37, decoration: Decoration.Dim)));
                    }

                    var panel = new Panel(table)
                    {
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(foreground: Color.Yellow, decoration: Decoration.Dim),
                        Padding = new Padding(1, 0),
                        Expand = false
                    };

                    AnsiConsole.Write(panel);
                    pendingLogs.Clear();
                }
            }
            try
            {
                // 1. Initial Header
                AnsiConsole.Write(new Rule("[green]Assistant[/]").LeftJustified());
                AnsiConsole.WriteLine();

                DocumentAssistant.ToolLog = (msg) =>
                {
                    lock (logLock) pendingLogs.Add(msg);
                    firstContentSignal.TrySetResult(true);
                };

                // 2. Continuous Thinking - Keep spinner active until visible output appears
                bool hasNext = false;
                bool currentTokenAppended = false;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("thinking...", async ctx =>
                    {
                        while (true)
                        {
                            hasNext = await responseEnumeration.MoveNextAsync();
                            if (!hasNext) break;

                            currentTokenAppended = false;

                            // Exit early if we have logs or printer flushed a line
                            if (pendingLogs.Count > 0 || firstContentSignal.Task.IsCompleted)
                                break;

                            printer.Append(responseEnumeration.Current);
                            currentTokenAppended = true;

                            if (firstContentSignal.Task.IsCompleted)
                                break;
                        }
                    });

                // 3. Process remaining turn sequentially
                try
                {
                    while (true)
                    {
                        if (pendingLogs.Count > 0)
                        {
                            FlushToolLogs();
                        }

                        if (!hasNext) break;

                        if (!currentTokenAppended)
                        {
                            printer.Append(responseEnumeration.Current);
                        }
                        currentTokenAppended = false;

                        hasNext = await responseEnumeration.MoveNextAsync();
                    }
                }
                finally
                {
                    // Final flush for any trailing logs
                    FlushToolLogs();
                    DocumentAssistant.ToolLog = null;
                    printer.Finish();
                }
            }
            catch (Exception ex)
            {
                cts.Cancel(); // Stop background tools immediately
                DocumentAssistant.ToolLog = null; // Prevent leaks
                AnsiConsole.MarkupLine($"[bold red]Error:[/] [dim]({backendType})[/] {ex.Message}");
                if (ex.InnerException != null)
                    AnsiConsole.MarkupLine($"[dim red]Inner: {ex.InnerException.Message}[/]");
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

    static async Task<string> DownloadNomicModelAsync()
    {
        var modelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antty",
            "models"
        );

        if (!Directory.Exists(modelsDir))
            Directory.CreateDirectory(modelsDir);

        var modelPath = Path.Combine(modelsDir, "nomicv2.gguf");

        if (!File.Exists(modelPath))
        {
            AnsiConsole.MarkupLine("[yellow]⬇ Downloading Nomic embedding model (first time only)...[/]");
            AnsiConsole.MarkupLine("[dim]From: https://huggingface.co/Inza124/Nomic[/]");
            AnsiConsole.WriteLine();

            await AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn()
                )
                .StartAsync(async ctx =>
                {
                    var downloadTask = ctx.AddTask("[cyan]Downloading nomicv2.gguf[/]");

                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromMinutes(10);

                    var url = "https://huggingface.co/Inza124/Nomic/resolve/main/nomicv2.gguf";

                    using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    downloadTask.MaxValue = totalBytes;

                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        downloadTask.Value = totalRead;
                    }
                });

            var fileSizeMB = new FileInfo(modelPath).Length / (1024.0 * 1024.0);
            AnsiConsole.MarkupLine($"[green]✓[/] Model downloaded: [cyan]{fileSizeMB:F1} MB[/]");
            AnsiConsole.WriteLine();
        }
        else
        {
            var fileSizeMB = new FileInfo(modelPath).Length / (1024.0 * 1024.0);
            AnsiConsole.MarkupLine($"[green]✓[/] Using cached Nomic model ([cyan]{fileSizeMB:F1} MB[/])");
            AnsiConsole.WriteLine();
        }

        return modelPath;
    }

    static async Task DownloadLocalModelAsync(string modelName)
    {
        var modelInfo = modelName switch
        {
            "gemma3:4b" => ("gemma3-4b.gguf", "https://huggingface.co/Inza124/Gemma3-4b/resolve/main/gemma3-4b.gguf"),
            "llama3.1:8b" => ("Llama3.1-maIN.gguf", "https://huggingface.co/Inza124/Llama3.1_8b/resolve/main/Llama3.1-maIN.gguf"),
            "qwen3:14b" => ("Qwen3-14b.gguf", "https://huggingface.co/Inza124/Qwen3-14b/resolve/main/Qwen3-14b.gguf"),
            _ => ("gemma3-4b.gguf", "https://huggingface.co/Inza124/Gemma3-4b/resolve/main/gemma3-4b.gguf")
        };

        var (fileName, url) = modelInfo;

        var modelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Antty",
            "models"
        );

        if (!Directory.Exists(modelsDir))
            Directory.CreateDirectory(modelsDir);

        var modelPath = Path.Combine(modelsDir, fileName);

        // Check if already downloaded
        if (File.Exists(modelPath))
        {
            var fileSizeMB = new FileInfo(modelPath).Length / (1024.0 * 1024.0);
            AnsiConsole.MarkupLine($"[green]✓[/] Using cached {fileName} ([cyan]{fileSizeMB:F1} MB[/])");
            AnsiConsole.WriteLine();

            // Still need to register with MaIN.NET
            await AIHub.Model().DownloadAsync(modelName);
            return;
        }

        // Download model
        AnsiConsole.MarkupLine($"[yellow]⬇ Downloading {fileName} (first time only)...[/]");
        AnsiConsole.MarkupLine($"[dim]From: {url}[/]");
        AnsiConsole.WriteLine();

        await AIHub.Model().DownloadAsync(modelName);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Configure embedding provider and AI model (reusable for initial setup and mid-session switching)
    /// </summary>
    /// <returns>Tuple of (embeddingProvider, useLocalAI, localModelName)</returns>
    /// <summary>
    /// Configure embedding provider and AI model (Chat Backend)
    /// </summary>
    /// <returns>Tuple of (embeddingProvider, backendType, modelName)</returns>
    /// <summary>
    /// Configure embedding provider and AI model (Chat Backend)
    /// </summary>
    /// <returns>Tuple of (embeddingProvider, backendType, modelName)</returns>
    static async Task<(Antty.Embedding.IEmbeddingProvider?, BackendType, string)> ConfigureProvidersAsync(AppConfig config)
    {
        AnsiConsole.Write(new Rule("[bold cyan]🤖 ASSISTANT CONFIGURATION[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        var modeChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Choose Operating Mode:[/]")
                .PageSize(10)
                .AddChoices(new[] {
                    "💻 Local (Offline) - Ollama + Local Embeddings",
                    "☁️  Cloud (Online) - Cloud Models + OpenAI Embeddings"
                }));

        bool isLocalMode = modeChoice.StartsWith("💻");
        BackendType backendType = BackendType.Ollama;
        string modelName = "";

        if (isLocalMode)
        {
            // --- LOCAL MODE ---
            AnsiConsole.MarkupLine("[dim]Configuring for fully offline use...[/]");
            config.EmbeddingProvider = "local";

            // 1. Setup Local Embeddings
            config.LocalModelPath = await DownloadNomicModelAsync();

            // 2. Setup Local Chat (Ollama)
            backendType = BackendType.Ollama;
            if (!await OllamaManager.EnsureOllamaReadyAsync())
            {
                AnsiConsole.MarkupLine("[red]Cannot proceed without Ollama.[/]");
                return (null, backendType, "");
            }

            modelName = await ConfigureOllamaModelAsync(config);
            if (string.IsNullOrEmpty(modelName)) return (null, backendType, "");
        }
        else
        {
            // --- CLOUD MODE ---
            AnsiConsole.MarkupLine("[dim]Configuring for cloud-powered performance...[/]");
            config.EmbeddingProvider = "openai";

            // 1. Setup OpenAI Key (Required for Embeddings)
            if (string.IsNullOrWhiteSpace(config.ApiKey) || config.ApiKey.StartsWith("sk-YOUR"))
            {
                config.ApiKey = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan]Enter OpenAI API Key (for Embeddings):[/]")
                        .PromptStyle("green")
                        .Secret());
            }

            // 2. Choose Chat Provider
            var cloudProvider = AnsiConsole.Prompt(
                 new SelectionPrompt<string>()
                     .Title("[cyan]Select Chat Provider:[/]")
                     .PageSize(10)
                     .AddChoices(new[] {
                         "OpenAI",
                         "Anthropic",
                         "Google Gemini",
                         "DeepSeek",
                         "XAI (Grok)",
                         "Groq"
                     }));

            string PromptKey(string name, string current)
            {
                if (!string.IsNullOrWhiteSpace(current) && !current.StartsWith("sk-YOUR")) return current;
                return AnsiConsole.Prompt(new TextPrompt<string>($"[cyan]Enter {name} API Key:[/]").Secret());
            }

            string SelectModel(Dictionary<string, string> options)
            {
                var choices = options.Keys.ToList();
                choices.Add("⌨️  Enter custom model ID...");
                var res = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[cyan]Select Model:[/]").AddChoices(choices));
                if (res.Contains("Enter custom")) return AnsiConsole.Prompt(new TextPrompt<string>("[cyan]Enter Model ID:[/]").Validate(s => !string.IsNullOrWhiteSpace(s)));
                return options[res];
            }

            if (cloudProvider == "OpenAI")
            {
                backendType = BackendType.OpenAi;
                modelName = SelectModel(new Dictionary<string, string> {
                    { "GPT-5.2 (Flagship)", "gpt-5.2" },
                    { "o3 (Reasoning)", "o3" },
                    { "GPT-5 Nano (Light)", "gpt-5-nano" },
                    { "GPT-4o (Omni)", "gpt-4o" },
                    { "o1 (Preview)", "o1" }
                });
            }
            else if (cloudProvider == "Anthropic")
            {
                backendType = BackendType.Anthropic;
                config.AnthropicKey = PromptKey("Anthropic", config.AnthropicKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "Claude 4.5 Sonnet", "claude-sonnet-4-5-20250929" },
                    { "Claude 4.5 Haiku", "claude-haiku-4-5-20251001" },
                    { "Claude 4.5 Opus", "claude-opus-4-5-20251101" },
                    { "Claude 3.7 Sonnet", "claude-3-7-sonnet" }
                });
            }
            else if (cloudProvider == "Google Gemini")
            {
                backendType = BackendType.Gemini;
                config.GeminiKey = PromptKey("Google Gemini", config.GeminiKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "Gemini 3.0 Pro", "gemini-3.0-pro-preview" },
                    { "Gemini 2.5 Pro", "gemini-2.5-pro" },
                    { "Gemini 2.5 Flash", "gemini-2.5-flash" }
                });
            }
            else if (cloudProvider == "DeepSeek")
            {
                backendType = BackendType.DeepSeek;
                config.DeepSeekKey = PromptKey("DeepSeek", config.DeepSeekKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "DeepSeek R1 (Reasoner)", "deepseek-reasoner" },
                    { "DeepSeek V3 (Chat)", "deepseek-chat" }
                });
            }
            else if (cloudProvider == "XAI (Grok)")
            {
                backendType = BackendType.Xai;
                config.XaiKey = PromptKey("XAI (Grok)", config.XaiKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "Grok-3", "grok-3" },
                    { "Grok-2", "grok-2-1212" }
                });
            }
            else if (cloudProvider == "Groq")
            {
                backendType = BackendType.GroqCloud;
                config.GroqKey = PromptKey("Groq", config.GroqKey);
                modelName = SelectModel(new Dictionary<string, string> {
                    { "Llama 3.3 70B", "llama-3.3-70b-versatile" },
                    { "Llama 3.1 8B", "llama-3.1-8b-instant" },
                    { "Mixtral 8x7B", "mixtral-8x7b-32768" }
                });
            }
        }

        config.ChatBackend = backendType.ToString();
        config.ChatModel = modelName;
        config.Save();
        AnsiConsole.WriteLine();

        // Create embedding provider
        var embeddingProvider = isLocalMode
            ? (Antty.Embedding.IEmbeddingProvider)new Antty.Embedding.LocalEmbeddingProvider(config.LocalModelPath)
            : new Antty.Embedding.OpenAIEmbeddingProvider(config.ApiKey);

        return (embeddingProvider, backendType, modelName);
    }

    /// <summary>
    /// Helper to configure Ollama Model
    /// </summary>
    static async Task<string> ConfigureOllamaModelAsync(AppConfig config)
    {
        var modelChoices = new List<string> {
             "💻 Granite4:3b (3B params, fast)",
             "💻 Llama3.1:8b (8B params, balanced)",
             "💻 Qwen3:14b (14B params, capable)"
         };
        foreach (var c in config.CustomOllamaModels) modelChoices.Add($"📦 {c} (custom)");
        modelChoices.Add("⌨️  Enter custom model name...");

        var localChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
               .Title("[cyan]Select Local Ollama Model:[/]")
               .PageSize(10)
               .AddChoices(modelChoices));

        string modelName = "";
        if (localChoice.Contains("Enter custom"))
        {
            modelName = AnsiConsole.Prompt(new TextPrompt<string>("[cyan]Enter Ollama model name:[/]").Validate(s => !string.IsNullOrWhiteSpace(s)));
            if (!config.CustomOllamaModels.Contains(modelName)) { config.CustomOllamaModels.Add(modelName); }
        }
        else if (localChoice.Contains("(custom)"))
        {
            modelName = localChoice.Replace("📦 ", "").Replace(" (custom)", "").Trim();
        }
        else
        {
            if (localChoice.Contains("Granite")) modelName = "granite4:3b";
            else if (localChoice.Contains("Llama")) modelName = "llama3.1:8b";
            else if (localChoice.Contains("Qwen")) modelName = "qwen3:14b";
            else modelName = "llama3.1:8b";
        }

        if (!await OllamaManager.IsModelInstalledAsync(modelName))
        {
            AnsiConsole.MarkupLine($"[yellow]Model {modelName} not found locally.[/]");
            if (AnsiConsole.Confirm($"Download {modelName}?"))
            {
                if (!await OllamaManager.PullModelAsync(modelName))
                {
                    AnsiConsole.MarkupLine("[red]Failed to download model.[/]");
                    return ""; // Fail
                }
            }
            else
            {
                return ""; // Fail
            }
        }
        AnsiConsole.MarkupLine($"[green]✓[/] Using local model: [cyan]{modelName}[/]");
        return modelName;
    }

    /// <summary>
    /// Advanced Markdown Renderer that creates Panels for code blocks
    /// </summary>
    static Spectre.Console.Rendering.IRenderable RenderMarkdown(string text)
    {
        var parts = new List<Spectre.Console.Rendering.IRenderable>();

        // Split by code blocks: ```language ... ```
        var segments = System.Text.RegularExpressions.Regex.Split(text, @"(```[\s\S]*?```)");

        foreach (var segment in segments)
        {
            if (segment.StartsWith("```") && segment.EndsWith("```") && segment.Length >= 6)
            {
                // Code block processing
                var content = segment.Substring(3, segment.Length - 6);

                // Handle language identifier
                var firstLineEnd = content.IndexOf('\n');
                if (firstLineEnd >= 0)
                {
                    var possibleLang = content.Substring(0, firstLineEnd).Trim();
                    // Basic check if it's a lang tag
                    if (!string.IsNullOrWhiteSpace(possibleLang) && !possibleLang.Contains(' '))
                    {
                        content = content.Substring(firstLineEnd + 1);
                    }
                }

                // Escape content for Markup
                content = content.Replace("[", "[[").Replace("]", "]]");

                parts.Add(new Panel(new Markup($"[green]{content}[/]"))
                       .Border(BoxBorder.Rounded)
                       .BorderColor(Color.Grey)
                       .Expand());
            }
            else
            {
                // Normal text processing
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    var escaped = segment.Replace("[", "[[").Replace("]", "]]");
                    // Inline formatting
                    escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"`([^`]+)`", "[green]$1[/]");
                    escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"\*\*([^*]+)\*\*", "[bold]$1[/]");
                    escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"^#+\s+(.+)$", "[bold underline]$1[/]", System.Text.RegularExpressions.RegexOptions.Multiline);
                    escaped = System.Text.RegularExpressions.Regex.Replace(escaped, @"^\s*-\s+", "  • ", System.Text.RegularExpressions.RegexOptions.Multiline);

                    parts.Add(new Markup(escaped));
                }
            }
        }

        return new Rows(parts);
    }
}

/// <summary>
/// Hybrid streaming printer - buffers tokens, applies markdown on complete lines
/// </summary>
public class StreamingMarkdownPrinter
{
    private System.Text.StringBuilder _lineBuffer = new System.Text.StringBuilder();
    private System.Text.StringBuilder _rawBuffer = new System.Text.StringBuilder();
    private System.Text.StringBuilder _fullContent = new System.Text.StringBuilder();
    private Action? _onFirstPrint;
    private bool _inCodeBlock = false;

    public StreamingMarkdownPrinter(Action? onFirstPrint = null)
    {
        _onFirstPrint = onFirstPrint;
    }

    public string GetFullContent() => _fullContent.ToString();
    public string GetLastLine() => _lineBuffer.ToString();

    public void Append(string token)
    {
        _fullContent.Append(token);
        _rawBuffer.Append(token);

        // Process character by character to detect newlines
        foreach (var c in token)
        {
            if (c == '\n')
            {
                // We have a complete line - print it with markdown formatting
                var line = _lineBuffer.ToString();
                PrintFormattedLine(line);
                _lineBuffer.Clear();
            }
            else
            {
                _lineBuffer.Append(c);
            }
        }
    }

    public void Finish()
    {
        // Print any remaining partial line
        if (_lineBuffer.Length > 0)
        {
            var line = _lineBuffer.ToString();
            PrintFormattedLine(line);
        }
    }

    private void PrintFormattedLine(string line)
    {
        // Signal first print
        if (_onFirstPrint != null)
        {
            _onFirstPrint();
            _onFirstPrint = null;
        }

        // Handle code blocks
        if (line.TrimStart().StartsWith("```"))
        {
            _inCodeBlock = !_inCodeBlock;
            if (_inCodeBlock)
            {
                var lang = line.Trim().Trim('`').Trim();
                if (string.IsNullOrWhiteSpace(lang)) lang = "code";
                AnsiConsole.MarkupLine($"[grey]╭───[/] [cyan]{lang}[/] [grey]──────[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[grey]╰────────────────[/]");
            }
            return;
        }

        if (_inCodeBlock)
        {
            // Code content
            var escaped = line.Replace("[", "[[").Replace("]", "]]");
            AnsiConsole.MarkupLine($"[grey]│[/] [green]{escaped}[/]");
            return;
        }

        // Regular text
        if (string.IsNullOrWhiteSpace(line))
        {
            AnsiConsole.WriteLine();
            return;
        }

        // Fast path - no markdown
        if (!line.Contains('`') && !line.Contains("**"))
        {
            AnsiConsole.WriteLine(line);
            return;
        }

        // Apply markdown formatting
        try
        {
            var formatted = line.Replace("[", "[[").Replace("]", "]]");

            // Inline code
            if (line.Contains('`'))
                formatted = System.Text.RegularExpressions.Regex.Replace(formatted, @"`([^`]+)`", "[green]$1[/]");

            // Bold
            if (line.Contains("**"))
                formatted = System.Text.RegularExpressions.Regex.Replace(formatted, @"\*\*([^*]+)\*\*", "[bold]$1[/]");

            AnsiConsole.MarkupLine(formatted);
        }
        catch
        {
            AnsiConsole.WriteLine(line);
        }
    }
}
