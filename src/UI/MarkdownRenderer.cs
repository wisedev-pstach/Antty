using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text.RegularExpressions;

namespace Antty.UI;

public static class MarkdownRenderer
{
    public static IRenderable RenderMarkdown(string text)
    {
        var parts = new List<IRenderable>();

        var segments = Regex.Split(text, @"(```[\s\S]*?```)");

        foreach (var segment in segments)
        {
            if (segment.StartsWith("```") && segment.EndsWith("```") && segment.Length >= 6)
            {
                var content = segment.Substring(3, segment.Length - 6);

                var firstLineEnd = content.IndexOf('\n');
                if (firstLineEnd >= 0)
                {
                    var possibleLang = content.Substring(0, firstLineEnd).Trim();
                    if (!string.IsNullOrWhiteSpace(possibleLang) && !possibleLang.Contains(' '))
                    {
                        content = content.Substring(firstLineEnd + 1);
                    }
                }

                content = content.Replace("[", "[[").Replace("]", "]]");

                parts.Add(new Panel(new Markup($"[green]{content}[/]"))
                       .Border(BoxBorder.Rounded)
                       .BorderColor(Color.Grey)
                       .Expand());
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(segment))
                {
                    var escaped = segment.Replace("[", "[[").Replace("]", "]]");
                    escaped = Regex.Replace(escaped, @"`([^`]+)`", "[green]$1[/]");
                    escaped = Regex.Replace(escaped, @"\*\*([^*]+)\*\*", "[bold]$1[/]");
                    escaped = Regex.Replace(escaped, @"^#+\s+(.+)$", "[bold underline]$1[/]", RegexOptions.Multiline);
                    escaped = Regex.Replace(escaped, @"^\s*-\s+", "  • ", RegexOptions.Multiline);

                    parts.Add(new Markup(escaped));
                }
            }
        }

        return new Rows(parts);
    }
}
