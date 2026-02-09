using MaIN.Core.Hub;
using MaIN.Core.Hub.Contexts;
using MaIN.Core.Hub.Utils;
using MaIN.Domain.Configuration;
using Spectre.Console;
using System.Text.Json;
using MaIN.Core.Hub.Contexts.Interfaces.AgentContext;
using MaIN.Domain.Entities;
using MaIN.Services.Services;

namespace Antty;

/// <summary>
/// Tool arguments for searching documents
/// </summary>
public class SearchDocumentsArgs
{
    public string query { get; set; } = string.Empty;
    public int maxResults { get; set; } = 5;
}

/// <summary>
/// Tool arguments for reading a specific page
/// </summary>
public class ReadPageArgs
{
    public string documentName { get; set; } = string.Empty;
    public int pageNumber { get; set; }
}

/// <summary>
/// AI Assistant for conversational document queries using MaIN.NET
/// </summary>
public class DocumentAssistant
{
    private static MultiBookSearchEngine? _searchEngine;
    private static List<(string filePath, string kbPath)>? _documents;
    private static IAgentContextExecutor? _assistantAgent;
    private static List<Message> _conversationHistory = new();
    
    /// <summary>
    /// Callback for tool execution logs (e.g. "Searching for...")
    /// </summary>
    public static Action<string>? ToolLog { get; set; }

    private static void LogTool(string message)
    {
        if (ToolLog != null) ToolLog(message);
        else AnsiConsole.MarkupLine(message);
    }

    /// <summary>
    /// Initialize the document assistant with loaded documents
    /// </summary>
    public static async Task Initialize(AppConfig config, MultiBookSearchEngine searchEngine, List<(string filePath, string kbPath)> documents, BackendType backendType, string modelName)
    {
        _searchEngine = searchEngine;
        _documents = documents;
        _conversationHistory.Clear(); 
        AIHub.Extensions.DisableNotificationsLogs();

        // Extract document names for system prompt
        var documentNames = documents.Select(d => Path.GetFileNameWithoutExtension(d.filePath)).ToList();

        // Set backend-specific environment variables for keys
        switch (backendType)
        {
            case BackendType.OpenAi:
                Environment.SetEnvironmentVariable("OPENAI_API_KEY", config.ApiKey);
                break;
            case BackendType.Anthropic:
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", config.AnthropicKey);
                break;
            case BackendType.Gemini:
                Environment.SetEnvironmentVariable("GEMINI_API_KEY", config.GeminiKey);
                break;
            case BackendType.DeepSeek:
                Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", config.DeepSeekKey);
                break;
            case BackendType.GroqCloud:
                Environment.SetEnvironmentVariable("GROQ_API_KEY", config.GroqKey);
                break;
            case BackendType.Xai:
                Environment.SetEnvironmentVariable("XAI_API_KEY", config.XaiKey);
                break;
        }

        // Initialize unified agent
        _assistantAgent = await AIHub.Agent()
            .WithModel(modelName)
            .WithBackend(backendType)
            .WithKnowledge(KnowledgeBuilder.Instance.DisablePersistence())
            .WithInitialPrompt(GetSystemPrompt(documentNames))
            .WithTools(new ToolsConfigurationBuilder()
                .AddTool<SearchDocumentsArgs>(
                    "search_documents",
                    "Search for information in documents (has built-in retry with keyword extraction). If this returns no results, you can try calling it again with a completely different query approach.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new
                            {
                                type = "string",
                                description = "The search query or question to find relevant information"
                            },
                            maxResults = new
                            {
                                type = "integer",
                                description = "Maximum number of results to return (1-10, default: 5)",
                                @default = 5,
                                minimum = 1,
                                maximum = 10
                            }
                        },
                        required = new[] { "query" }
                    },
                    SearchDocuments)
                .AddTool<ReadPageArgs>(
                    "read_page",
                    "Read the full content of a specific page from a document.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            documentName = new
                            {
                                type = "string",
                                description = "Name of the document (e.g., 'architecture', 'api-docs')"
                            },
                            pageNumber = new
                            {
                                type = "integer",
                                description = "Page number to read",
                                minimum = 1
                            }
                        },
                        required = new[] { "documentName", "pageNumber" }
                    },
                    ReadPage)
                .WithToolChoice("auto")
                .Build())
            .WithSteps(
                StepBuilder.Instance.Answer().Build())
            .CreateAsync();
    }

    /// <summary>
    /// Chat with the assistant (streaming)
    /// </summary>
    public static async IAsyncEnumerable<string> ChatAsync(string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_assistantAgent == null)
            throw new InvalidOperationException("Assistant not initialized. Call Initialize() first.");

        // Add user message to conversation history
        var userMsg = new MaIN.Domain.Entities.Message
        {
            Content = userMessage,
            Role = "user",
            Time = DateTime.UtcNow,
            Type = MessageType.CloudLLM
        };
        _conversationHistory.Add(userMsg);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
        var assistantResponse = new System.Text.StringBuilder();

        var processTask = _assistantAgent.ProcessAsync(
            _conversationHistory, // Use full conversation history
            tokenCallback: (token) =>
            {
                channel.Writer.TryWrite(token.Text);
                return Task.CompletedTask;
            },
            toolCallback: (toolUse) =>
            {
                // Tool callbacks handled internally
                return Task.CompletedTask;
            }
        );

        _ = processTask.ContinueWith(t => 
        {
            if (t.IsFaulted)
                channel.Writer.Complete(t.Exception?.InnerException ?? t.Exception);
            else if (t.IsCanceled)
                channel.Writer.Complete(new OperationCanceledException());
            else
                channel.Writer.Complete();
        });

        await foreach (var text in channel.Reader.ReadAllAsync())
        {
            assistantResponse.Append(text);
            yield return text;
        }

        // Add assistant response to conversation history
        if (assistantResponse.Length > 0)
        {
            _conversationHistory.Add(new MaIN.Domain.Entities.Message
            {
                Content = assistantResponse.ToString(),
                Role = "assistant",
                Time = DateTime.UtcNow,
                Type = MessageType.CloudLLM
            });
        }
    }

    /// <summary>
    /// Tool: Search documents semantically with automatic retry and fallback
    /// </summary>
    private static async Task<object> SearchDocuments(SearchDocumentsArgs args)
    {
        try
        {
            if (_searchEngine == null)
                return "Error: Search engine not initialized";

            // Tool message
            LogTool($"🔍 Searching for: {args.query}...");

            // Try original query first
            var results = await _searchEngine.SearchAllAsync(args.query, silent: true);
            var topResults = results.Take(args.maxResults).ToList();

            // If no results, try intelligent fallbacks
            if (topResults.Count == 0)
            {
                LogTool("   No results, trying with keywords only...");
                
                // Extract keywords (words longer than 3 chars, excluding common words)
                var commonWords = new HashSet<string> { "the", "and", "for", "with", "that", "this", "from", "what", "how", "why", "when", "where", "which", "about" };
                var keywords = args.query.ToLower()
                    .Split(new[] { ' ', ',', '.', '?', '!', ';', ':' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3 && !commonWords.Contains(w))
                    .ToList();

                if (keywords.Count > 0)
                {
                    // Try 1: Keywords only (most important words)
                    var keywordQuery = string.Join(" ", keywords);
                    results = await _searchEngine.SearchAllAsync(keywordQuery, silent: true);
                    topResults = results.Take(args.maxResults).ToList();

                    if (topResults.Count == 0 && keywords.Count > 1)
                    {
                        LogTool("   Still no results, trying individual keywords...");
                        
                        // Try 2: Most important keyword alone
                        var mainKeyword = keywords.OrderByDescending(k => k.Length).First();
                        results = await _searchEngine.SearchAllAsync(mainKeyword, silent: true);
                        topResults = results.Take(args.maxResults).ToList();
                    }
                }
            }

            if (topResults.Count == 0)
            {
                LogTool("   ✗ No results after multiple attempts");
                return "No relevant information found in the documents after trying multiple search strategies. The information may not be in the loaded documents, or try rephrasing your question with different keywords.";
            }

            // Format results as simple text to avoid serialization issues
            var resultLines = new List<string>();
            resultLines.Add($"Found {topResults.Count} relevant passage(s):\n");

            for (int i = 0; i < topResults.Count; i++)
            {
                var result = topResults[i];
                resultLines.Add($"[{i + 1}] Document: {result.BookSource}, Page: {result.Page}");
                resultLines.Add($"    Content: {result.Text}");
                resultLines.Add($"    Relevance: {result.Score:P0}\n");
            }

            LogTool($"✓ Found {topResults.Count} result(s)");
            return new
            {
                result = string.Join("\n", resultLines)
            };
        }
        catch (Exception ex)
        {
            LogTool($"Error executing search: {ex.Message}");
            return $"Error executing search: {ex.Message}";
        }
    }

    /// <summary>
    /// Tool: Read complete page content
    /// </summary>
    private static async Task<object> ReadPage(ReadPageArgs args)
    {
        if (_documents == null)
            return "Error: Documents not loaded";

        // Tool message
        LogTool($"📖 Reading page {args.pageNumber} from {args.documentName}...");

        // Find the document
        var doc = _documents.FirstOrDefault(d =>
            Path.GetFileNameWithoutExtension(d.filePath).Equals(args.documentName, StringComparison.OrdinalIgnoreCase));

        if (doc == default)
            return $"Document '{args.documentName}' not found. Available documents: {string.Join(", ", _documents.Select(d => Path.GetFileNameWithoutExtension(d.filePath)))}";

        if (!File.Exists(doc.kbPath))
            return "Knowledge base not found for this document.";

        try
        {
            // Load the knowledge base
            var json = await File.ReadAllTextAsync(doc.kbPath);
            
            List<RawChunk> chunks;
            
            // Try new format first (KnowledgeBase with Metadata)
            try
            {
                var kb = JsonSerializer.Deserialize<KnowledgeBase>(json);
                chunks = kb?.Chunks ?? new List<RawChunk>();
            }
            catch (JsonException)
            {
                // Fall back to old format (direct List<RawChunk>)
                chunks = JsonSerializer.Deserialize<List<RawChunk>>(json) ?? new List<RawChunk>();
            }

            if (chunks.Count == 0)
                return "Knowledge base is empty.";

            // Find all chunks from the specified page
            var pageChunks = chunks.Where(c => c.PageNumber == args.pageNumber).ToList();

            if (pageChunks.Count == 0)
                return $"Page {args.pageNumber} not found in {args.documentName}. The document may have fewer pages.";

            // Combine all chunks from this page
            var pageContent = string.Join("\n", pageChunks.Select(c => c.Content));

            LogTool($"✓ Read {pageChunks.Count} section(s) from page {args.pageNumber}");
            return new
            {
                result = $"Content from page {args.pageNumber} of {args.documentName}:\n\n{pageContent}"
            };
        }
        catch (Exception ex)
        {
            return $"Error reading page: {ex.Message}";
        }
    }

    private static string GetSystemPrompt(List<string> documentNames) => $"""
        You are a helpful research assistant. Answer user questions using their documents.
        
        LOADED DOCUMENTS:
        {string.Join("\n", documentNames.Select(d => $"- {d}"))}

        Guidelines:
        - Always try your best to find an answer in the loaded documents
        - search_documents has built-in retry, but you can call it multiple times with different queries if needed
        - Don't give up - if one search fails, try again with different phrasing or keywords
        - Use read_page to get full context from relevant pages (from the documents listed above)
        - Cite sources: "According to [document], page [number]..."
        - Never mention tools or technical details
        - Be conversational and helpful
        """;
}
