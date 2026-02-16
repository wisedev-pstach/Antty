using Antty.Core;
using Antty.Configuration;
using Antty.UI;
using MaIN.Domain.Configuration;
using Spectre.Console;

namespace Antty.Services;

public class AssistantChatService : IAssistantChatService
{
    public async Task TalkToAssistantAsync(
        AppConfig config,
        MultiBookSearchEngine multiSearchEngine,
        List<(string filePath, string kbPath)> documents,
        BackendType backendType,
        string modelName)
    {
        AnsiConsole.Write(new Rule("[bold cyan]💬 TALK TO ASSISTANT[/]").RuleStyle("cyan"));
        AnsiConsole.WriteLine();

        try
        {
            await DocumentAssistant.Initialize(config, multiSearchEngine, documents, backendType, modelName);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]❌ Failed to initialize assistant: {ex.Message}[/]");
            return;
        }

        AnsiConsole.WriteLine();
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
            var printer = new StreamingMarkdownPrinter(() => firstContentSignal.TrySetResult(true));
            using var cts = new CancellationTokenSource();
            var responseEnumeration = DocumentAssistant.ChatAsync(userMessage, cts.Token).GetAsyncEnumerator();

            void DisplayToolLog(string msg)
            {
                var clean = System.Text.RegularExpressions.Regex.Replace(msg, @"\[.*?\]", "");
                clean = clean.Trim();

                AnsiConsole.MarkupLine($"  [steelblue1 italic]│ {clean.EscapeMarkup()}[/]");
            }

            try
            {
                AnsiConsole.Write(new Rule("[green]Assistant[/]").LeftJustified());
                AnsiConsole.WriteLine();

                DocumentAssistant.ToolLog = (msg) =>
                {
                    DisplayToolLog(msg);
                    firstContentSignal.TrySetResult(true);
                };

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

                            if (firstContentSignal.Task.IsCompleted)
                                break;

                            printer.Append(responseEnumeration.Current);
                            currentTokenAppended = true;

                            if (firstContentSignal.Task.IsCompleted)
                                break;
                        }
                    });

                try
                {
                    while (true)
                    {
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
                    DocumentAssistant.ToolLog = null;
                    printer.Finish();
                }
            }
            catch (Exception ex)
            {
                cts.Cancel();
                DocumentAssistant.ToolLog = null;
                AnsiConsole.MarkupLine($"[bold red]Error:[/] [dim]({backendType})[/] {ex.Message}");
                if (ex.InnerException != null)
                    AnsiConsole.MarkupLine($"[dim red]Inner: {ex.InnerException.Message}[/]");
            }
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine();
        }
    }
}
