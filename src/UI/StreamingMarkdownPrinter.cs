using Spectre.Console;

namespace Antty.UI;

public class StreamingMarkdownPrinter
{
    private System.Text.StringBuilder _lineBuffer = new System.Text.StringBuilder();
    private System.Text.StringBuilder _rawBuffer = new System.Text.StringBuilder();
    private System.Text.StringBuilder _fullContent = new System.Text.StringBuilder();
    private Action? _onFirstPrint;
    private bool _inCodeBlock = false;
    public bool HasFlushedLine { get; private set; } = false;

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

        foreach (var c in token)
        {
            if (c == '\n')
            {
                var line = _lineBuffer.ToString();
                PrintFormattedLine(line);
                _lineBuffer.Clear();
                HasFlushedLine = true;
            }
            else
            {
                _lineBuffer.Append(c);
            }
        }
    }

    public void Finish()
    {
        if (_lineBuffer.Length > 0)
        {
            var line = _lineBuffer.ToString();
            PrintFormattedLine(line);
        }
    }

    private void PrintFormattedLine(string line)
    {
        if (_onFirstPrint != null)
        {
            _onFirstPrint();
            _onFirstPrint = null;
        }

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
            var escaped = line.Replace("[", "[[").Replace("]", "]]");
            AnsiConsole.MarkupLine($"[grey]│[/] [green]{escaped}[/]");
            return;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            AnsiConsole.WriteLine();
            return;
        }

        if (!line.Contains('`') && !line.Contains("**"))
        {
            AnsiConsole.WriteLine(line);
            return;
        }

        try
        {
            var formatted = line.Replace("[", "[[").Replace("]", "]]");

            if (line.Contains('`'))
                formatted = System.Text.RegularExpressions.Regex.Replace(formatted, @"`([^`]+)`", "[green]$1[/]");

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
