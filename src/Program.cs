using Antty;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MaIN.Core;
using MaIN.Core.Hub;

partial class Program
{
    static async Task Main(string[] args)
    {
        // Enable UTF-8 encoding for emoji support
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Initialize MaIN.NET framework for console apps
        // TEMPORARILY DISABLED TO DEBUG
        // var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        // services.AddMaIN(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        // var serviceProvider = services.BuildServiceProvider();
        // serviceProvider.UseMaIN();
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
        var (embeddingProvider, useLocalAI, localModelName) = await ConfigureProvidersAsync(config);
        
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

        // Prompt to start assistant immediately after loading
        if (multiEngine.LoadedDocumentCount > 0)
        {
            AnsiConsole.WriteLine();
            if (AnsiConsole.Confirm("[cyan]Start chatting with the assistant?[/]", true))
            {
                await TalkToAssistantAsync(config.ApiKey, multiEngine, documentsToProcess, useLocalAI, localModelName);
            }
        }

        while (running)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("dim"));
            AnsiConsole.WriteLine();

            // Show loaded documents info
            AnsiConsole.MarkupLine($"[dim]Loaded documents: {multiEngine.LoadedDocumentCount}[/]");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[cyan]What would you like to do?[/] [dim](Embeddings: {config.EmbeddingProvider})[/]")
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
                await TalkToAssistantAsync(config.ApiKey, multiEngine, documentsToProcess, useLocalAI, localModelName);
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
                var (newEmbeddingProvider, newUseLocalAI, newLocalModelName) = await ConfigureProvidersAsync(config);
                
                if (newEmbeddingProvider == null)
                {
                    AnsiConsole.MarkupLine("[red]Configuration failed. Keeping previous settings.[/]");
                    continue;
                }

                // Update configuration
                embeddingProvider = newEmbeddingProvider;
                useLocalAI = newUseLocalAI;
                localModelName = newLocalModelName;

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green]✓[/] Provider switched successfully!");
                AnsiConsole.WriteLine();
                
                // Check if embedding provider actually changed (local <-> openai)
                bool embeddingProviderChanged = oldEmbeddingProvider != config.EmbeddingProvider;

                if (embeddingProviderChanged)
                {
                    // Embedding provider changed - need to re-index
                    AnsiConsole.MarkupLine("[yellow]⚠[/] Embedding provider changed. Documents need to be re-indexed.");
                    var shouldReload = AnsiConsole.Confirm("[cyan]Re-index documents now?[/]", true);
                    
                    if (shouldReload)
                    {
                        AnsiConsole.WriteLine();
                        AnsiConsole.Write(new Rule("[dim]Rebuilding Knowledge Bases[/]").RuleStyle("dim"));
                        AnsiConsole.WriteLine();

                        // Rebuild knowledge bases with new provider
                        var newDocumentsToProcess = new List<(string filePath, string kbPath)>();
                        int rebuilt = 0;

                        foreach (var filePath in config.SelectedDocuments)
                        {
                            var kbPath = AppConfig.GetKnowledgeBasePath(filePath, config.EmbeddingProvider);
                            newDocumentsToProcess.Add((filePath, kbPath));

                            // Always rebuild when switching embedding providers
                            AnsiConsole.MarkupLine($"[yellow]⚙ Re-indexing:[/] [cyan]{Path.GetFileName(filePath)}[/]");
                            await IngestionBuilder.BuildDatabaseAsync(filePath, embeddingProvider, kbPath);
                            rebuilt++;
                        }

                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine($"[green]✓[/] Re-indexed {rebuilt} document(s) with {config.EmbeddingProvider} embeddings");
                        AnsiConsole.WriteLine();

                        // Reload multiEngine with new KBs
                        AnsiConsole.Write(new Rule("[dim]Reloading Documents[/]").RuleStyle("dim"));
                        AnsiConsole.WriteLine();

                        multiEngine.LoadDocuments(embeddingProvider, newDocumentsToProcess);
                        documentsToProcess = newDocumentsToProcess;

                        AnsiConsole.MarkupLine($"[green]✓[/] Loaded {multiEngine.LoadedDocumentCount} document(s)");
                    }
                }
                else
                {
                    // Only AI model changed - no need to re-index
                    AnsiConsole.MarkupLine("[dim]AI model changed, but embeddings are the same. No re-indexing needed.[/]");
                    
                    // However, we still need to reload multiEngine with the new embedding provider instance
                    // (we disposed the old one and created a new one)
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[dim]Reloading documents with new provider instance...[/]");
                    multiEngine.LoadDocuments(embeddingProvider, documentsToProcess);
                    AnsiConsole.MarkupLine($"[green]✓[/] Loaded {multiEngine.LoadedDocumentCount} document(s)");
                }

                // Prompt to start assistant after switching
                if (multiEngine.LoadedDocumentCount > 0)
                {
                    AnsiConsole.WriteLine();
                    if (AnsiConsole.Confirm("[cyan]Start chatting with the assistant?[/]", true))
                    {
                        await TalkToAssistantAsync(config.ApiKey, multiEngine, documentsToProcess, useLocalAI, localModelName);
                    }
                }
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

    static async Task TalkToAssistantAsync(string apiKey, MultiBookSearchEngine multiEngine, List<(string filePath, string kbPath)> documents, bool useLocalAI, string localModelName)
    {
        AnsiConsole.Write(new Rule("[bold cyan]💬 TALK TO ASSISTANT[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        // Initialize the assistant
        AnsiConsole.MarkupLine("[dim]Initializing AI assistant...[/]");
        try
        {
            await DocumentAssistant.Initialize(apiKey, multiEngine, documents, useLocalAI, localModelName);
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
                    // Show thinking animation as a temporary status line (no "Assistant:" prefix)
                    var spinner = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
                    int spinnerIndex = 0;
                    while (firstToken)
                    {
                        AnsiConsole.Markup($"\r[dim]{spinner[spinnerIndex]} thinking...[/]   ");
                        spinnerIndex = (spinnerIndex + 1) % spinner.Length;
                        await Task.Delay(100);
                    }
                });

                await foreach (var token in DocumentAssistant.ChatAsync(userMessage))
                {
                    if (firstToken)
                    {
                        // Stop animation and clear the thinking indicator completely
                        firstToken = false;
                        await Task.Delay(50); // Let animation stop
                        AnsiConsole.Markup("\r                                                  \r"); // Clear line completely
                        AnsiConsole.Markup($"[bold green]Assistant:[/] "); // Now show "Assistant:" label
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
    static async Task<(Antty.Embedding.IEmbeddingProvider?, bool, string)> ConfigureProvidersAsync(AppConfig config)
    {
        // 1. Choose Embedding Provider
        AnsiConsole.Write(new Rule("[bold cyan]🔧 EMBEDDING CONFIGURATION[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        var providerChoice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Choose embedding provider:[/]")
                .PageSize(10)
                .AddChoices(new[] {
                    "🌐 OpenAI (Cloud, requires API key)",
                    "💻 Local (Offline, uses GGUF models)"
                }));

        bool useLocalProvider = providerChoice.StartsWith("💻");

        if (useLocalProvider)
        {
            config.EmbeddingProvider = "local";
            config.LocalModelPath = await DownloadNomicModelAsync();
        }
        else
        {
            config.EmbeddingProvider = "openai";

            // Check if API key is configured
            if (config.ApiKey == "sk-YOUR-OPENAI-KEY-HERE" || string.IsNullOrWhiteSpace(config.ApiKey))
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
        }

        config.Save();
        AnsiConsole.WriteLine();

        // 2. Create embedding provider based on user choice
        var embeddingProvider = useLocalProvider
            ? (Antty.Embedding.IEmbeddingProvider)new Antty.Embedding.LocalEmbeddingProvider(config.LocalModelPath)
            : new Antty.Embedding.OpenAIEmbeddingProvider(config.ApiKey);

        AnsiConsole.WriteLine();

        // 3. Choose AI Model (for DocumentAssistant)
        AnsiConsole.Write(new Rule("[bold cyan]🤖 AI MODEL CONFIGURATION[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        bool useLocalAI;
        string localModelName = "";

        if (useLocalProvider)
        {
            // Local embeddings → only show local AI models 
            // Build model choices including saved custom models
            var modelChoices = new List<string>
            {
                "💻 Granite4:3b (3B params, small, fast)",
                "💻 Llama3.1:8b (8B params, balanced)",
                "💻 Qwen3:14b (14B params, most capable)"
            };

            // Add previously used custom models
            foreach (var customModel in config.CustomOllamaModels.Distinct())
            {
                modelChoices.Add($"📦 {customModel} (custom)");
            }

            // Add "Enter custom model" option
            modelChoices.Add("⌨️  Enter custom model name...");

            var modelChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]Choose local AI model for document assistant:[/]")
                    .PageSize(15)
                    .AddChoices(modelChoices));

            useLocalAI = true;

            if (modelChoice.Contains("Enter custom"))
            {
                // User wants to enter a custom model
                localModelName = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan]Enter Ollama model name (e.g., mistral, llama2):[/]")
                        .PromptStyle("yellow")
                        .ValidationErrorMessage("[red]Model name cannot be empty[/]")
                        .Validate(name => !string.IsNullOrWhiteSpace(name)));

                // Save to custom models list
                if (!config.CustomOllamaModels.Contains(localModelName))
                {
                    config.CustomOllamaModels.Add(localModelName);
                    config.Save();
                }
            }
            else if (modelChoice.Contains("(custom)"))
            {
                // Extract custom model name
                localModelName = modelChoice.Replace("📦 ", "").Replace(" (custom)", "").Trim();
            }
            else
            {
                // Standard model selection
                localModelName = modelChoice switch
                {
                    var s when s.Contains("Granite4:3b") => "granite4:3b",
                    var s when s.Contains("Llama3.1:8b") => "llama3.1:8b",
                    var s when s.Contains("Qwen3:14b") => "qwen3:14b",
                    _ => "granite4:3b"
                };
            }

            // Ensure Ollama is installed and running
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold cyan]🦙 OLLAMA SETUP[/]").RuleStyle("cyan"));
            AnsiConsole.WriteLine();

            if (!await OllamaManager.EnsureOllamaReadyAsync())
            {
                AnsiConsole.MarkupLine("[red]Cannot proceed without Ollama. Please install it and try again.[/]");
                return (null, false, "");
            }

            AnsiConsole.WriteLine();

            // Check if model is already installed
            if (!await OllamaManager.IsModelInstalledAsync(localModelName))
            {
                AnsiConsole.MarkupLine($"[yellow]Model {localModelName} not found locally[/]");

                var shouldDownload = AnsiConsole.Confirm(
                    $"[cyan]Would you like to download {localModelName}?[/]",
                    true);

                if (shouldDownload)
                {
                    if (!await OllamaManager.PullModelAsync(localModelName))
                    {
                        AnsiConsole.MarkupLine("[red]✗[/] Failed to download model");
                        return (null, false, "");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Cannot proceed without a model[/]");
                    return (null, false, "");
                }
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Model {localModelName} is ready");
            }
        }
        else
        {
            // OpenAI embeddings → only show OpenAI AI model
            useLocalAI = false;
        }

        return (embeddingProvider, useLocalAI, localModelName);
    }
}
