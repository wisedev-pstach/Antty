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
    private static List<MaIN.Domain.Entities.Message> _conversationHistory = new();

    /// <summary>
    /// Initialize the document assistant with loaded documents
    /// </summary>
    public static async Task Initialize(string apiKey, MultiBookSearchEngine searchEngine, List<(string filePath, string kbPath)> documents, bool useLocalAI, string localModelName)
    {
        _searchEngine = searchEngine;
        _documents = documents;
        _conversationHistory.Clear(); 
        AIHub.Extensions.DisableNotificationsLogs();

        if (useLocalAI)
        {
            _assistantAgent = await AIHub.Agent()
                .WithModel(localModelName)
                .WithBackend(BackendType.Ollama)  
                .WithKnowledge(KnowledgeBuilder.Instance.DisablePersistence())
                .WithInitialPrompt(GetSystemPrompt())
                .WithId("AnttyDocAssistant")
                .WithTools(new ToolsConfigurationBuilder()
                    .AddTool<SearchDocumentsArgs>(
                        "search_documents",
                        "INITIAL DISCOVERY ONLY: Use this ONCE at the start to find which document and page contains relevant information. Returns brief passages with document name and page number. After getting results, ALWAYS use read_page to get full context instead of searching again.",
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
                        "PRIMARY TOOL: After search_documents gives you a page number, ALWAYS use this to read the full page content. This gives you complete information. Use this for follow-up questions instead of searching again. Much more reliable than search for getting detailed information.",
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
        else
        {
            // Cloud AI with BackendType.OpenAi
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", apiKey);

            _assistantAgent = await AIHub.Agent()
                .WithModel("gpt-5")
                .WithBackend(BackendType.OpenAi)
                .WithKnowledge(KnowledgeBuilder.Instance.DisablePersistence())
                .WithInitialPrompt(GetSystemPrompt())
                .WithId("AnttyDocAssistant")
                .WithTools(new ToolsConfigurationBuilder()
                    .AddTool<SearchDocumentsArgs>(
                        "search_documents",
                        "INITIAL DISCOVERY ONLY: Use this ONCE at the start to find which document and page contains relevant information. Returns brief passages with document name and page number. After getting results, ALWAYS use read_page to get full context instead of searching again.",
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
                        "PRIMARY TOOL: After search_documents gives you a page number, ALWAYS use this to read the full page content. This gives you complete information. Use this for follow-up questions instead of searching again. Much more reliable than search for getting detailed information.",
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
    }

    /// <summary>
    /// Chat with the assistant (streaming)
    /// </summary>
    public static async IAsyncEnumerable<string> ChatAsync(string userMessage)
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
            });

        _ = processTask.ContinueWith(_ => channel.Writer.Complete());

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
    /// Tool: Search documents semantically
    /// </summary>
    private static async Task<object> SearchDocuments(SearchDocumentsArgs args)
    {
        if (_searchEngine == null)
            return "Error: Search engine not initialized";

        // Tool message on new line
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]🔍 Searching for: {args.query}...[/]");

        var results = await _searchEngine.SearchAllAsync(args.query);
        var topResults = results.Take(args.maxResults).ToList();

        if (topResults.Count == 0)
            return "No relevant information found in the documents.";

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

        AnsiConsole.MarkupLine($"[dim]✓ Found {topResults.Count} result(s)[/]");

        return new
        {
            result = string.Join("\n", resultLines)
        };
    }

    /// <summary>
    /// Tool: Read complete page content
    /// </summary>
    private static async Task<object> ReadPage(ReadPageArgs args)
    {
        if (_documents == null)
            return "Error: Documents not loaded";

        // Tool message on new line
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]📖 Reading page {args.pageNumber} from {args.documentName}...[/]");

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
            var chunks = JsonSerializer.Deserialize<List<RawChunk>>(json);

            if (chunks == null || chunks.Count == 0)
                return "Knowledge base is empty.";

            // Find all chunks from the specified page
            var pageChunks = chunks.Where(c => c.PageNumber == args.pageNumber).ToList();

            if (pageChunks.Count == 0)
                return $"Page {args.pageNumber} not found in {args.documentName}. The document may have fewer pages.";

            // Combine all chunks from this page
            var pageContent = string.Join("\n", pageChunks.Select(c => c.Content));

            AnsiConsole.MarkupLine($"[dim]✓ Read {pageChunks.Count} section(s) from page {args.pageNumber}[/]");
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

    private static string GetSystemPrompt() => """
        You are a helpful document research assistant. You answer questions based on the user's documents.

        CRITICAL RULES - NEVER BREAK THESE:
        1. NEVER mention "tools", "search_documents", "read_page" or any technical implementation
        2. NEVER say "I tried to search" or "the search returned an error" or "serialization issue"
        3. NEVER expose internal errors or failures to the user
        4. If you encounter an error, just say: "I couldn't find that information in the documents."
        5. Act like you're directly reading and searching the documents yourself, not using tools

        WORKFLOW (internal only, don't mention this to users):
        1. Use search_documents ONCE to find relevant pages
        2. Use read_page to read full content from those pages
        3. For follow-ups, read the same pages again instead of searching

        RESPONSE STYLE:
        - Natural and conversational
        - Always cite sources: "According to [document name], page [number]..."
        - If info not in docs, say: "I don't see that information in the loaded documents."
        - Never mention errors, tools, or how you work internally

        Your goal is to help users understand and find information in their documents through natural conversation.
        """;
}
