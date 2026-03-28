using Antty.Models;
using Antty.Embedding;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;
using VersOne.Epub;
using Spectre.Console;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace Antty.Core;

public static class IngestionBuilder
{
    public static async Task BuildDatabaseAsync(string filePath, IEmbeddingProvider provider, string outputPath)
    {
        AnsiConsole.Write(new Rule("[bold yellow]🚀 STARTING INGESTION[/]").RuleStyle("yellow"));
        AnsiConsole.WriteLine();

        var chunks = new List<RawChunk>();
        int globalId = 0;

        var extractedText = await ExtractTextFromFileAsync(filePath);

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn()
            )
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[cyan]Processing {Path.GetFileName(filePath)}[/]", maxValue: extractedText.Count);

                foreach (var (pageNumber, text) in extractedText)
                {
                    var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var para in paragraphs)
                    {
                        string cleanText = para.Replace("\n", " ").Trim();

                        if (cleanText.Length < 30) continue;
                        if (int.TryParse(cleanText, out _)) continue;

                        chunks.Add(new RawChunk(
                            Id: globalId++,
                            PageNumber: pageNumber,
                            Content: cleanText,
                            Vector: Array.Empty<float>()
                        ));
                    }

                    task.Increment(1);
                    await Task.Delay(1);
                }
            });

        AnsiConsole.MarkupLine($"[green]✓[/] Extracted [bold cyan]{chunks.Count}[/] valid paragraphs.");
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
                var task = ctx.AddTask("[yellow]Generating embeddings[/]", maxValue: chunks.Count);

                int batchSize = 10;
                for (int i = 0; i < chunks.Count; i += batchSize)
                {
                    var batch = chunks.Skip(i).Take(batchSize).ToList();
                    var batchTexts = batch.Select(c => c.Content).ToList();

                    try
                    {
                        var embeddings = await provider.GenerateEmbeddingsAsync(batchTexts);

                        for (int j = 0; j < batch.Count; j++)
                        {
                            chunks[i + j] = chunks[i + j] with { Vector = embeddings[j] };
                        }

                        task.Increment(batch.Count);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error fetching embeddings: {Markup.Escape(ex.Message)}[/]");
                    }
                }
            });

        AnsiConsole.WriteLine();

        var knowledgeBase = new KnowledgeBase
        {
            Metadata = new KnowledgeBaseMetadata
            {
                Provider = provider.ProviderName,
                ModelName = provider.ModelName,
                Dimensions = provider.Dimensions,
                CreatedAt = DateTime.UtcNow
            },
            Chunks = chunks
        };

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        string jsonString = JsonSerializer.Serialize(knowledgeBase, jsonOptions);
        await File.WriteAllTextAsync(outputPath, jsonString);

        var fileSize = new FileInfo(outputPath).Length / 1024;
        AnsiConsole.Write(new Panel(
            new Markup($"[green]✅ Database saved to[/] [bold cyan]{Path.GetFileName(outputPath)}[/]\n[dim]Provider: {provider.ProviderName} ({provider.ModelName})\nSize: {fileSize} KB[/]")
        )
        .BorderColor(Color.Green)
        .Padding(1, 0));

        AnsiConsole.WriteLine();
    }

    private static async Task<List<(int pageNumber, string text)>> ExtractTextFromFileAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var results = new List<(int, string)>();

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan bold"))
            .StartAsync($"[cyan]Reading {Path.GetFileName(filePath)}...[/]", async ctx =>
            {
                switch (extension)
                {
                    case ".pdf":
                        using (var document = PdfDocument.Open(filePath))
                        {
                            foreach (var page in document.GetPages())
                            {
                                results.Add((page.Number, page.Text));
                            }
                        }
                        break;

                    case ".txt":
                    case ".md":
                    case ".json":
                        var content = await File.ReadAllTextAsync(filePath);
                        var chunkSize = 2000;
                        for (int i = 0; i < content.Length; i += chunkSize)
                        {
                            var pageNum = (i / chunkSize) + 1;
                            var chunk = content.Substring(i, Math.Min(chunkSize, content.Length - i));
                            results.Add((pageNum, chunk));
                        }
                        break;

                    case ".docx":
                    {
                        using var doc = WordprocessingDocument.Open(filePath, false);
                        var body = doc.MainDocumentPart?.Document?.Body;
                        if (body == null) break;

                        var pageText = new StringBuilder();
                        int pageNum = 1;

                        foreach (var para in body.Descendants<W.Paragraph>())
                        {
                            bool hasPageBreak = para.Descendants<W.Break>()
                                .Any(b => b.Type?.Value == W.BreakValues.Page);

                            if (hasPageBreak && pageText.Length > 0)
                            {
                                results.Add((pageNum++, pageText.ToString().Trim()));
                                pageText.Clear();
                            }

                            var text = para.InnerText.Trim();
                            if (!string.IsNullOrEmpty(text))
                            {
                                pageText.AppendLine(text);
                                pageText.AppendLine();
                            }

                            if (pageText.Length >= 3000)
                            {
                                results.Add((pageNum++, pageText.ToString().Trim()));
                                pageText.Clear();
                            }
                        }

                        if (pageText.Length > 0)
                            results.Add((pageNum, pageText.ToString().Trim()));
                        break;
                    }

                    case ".pptx":
                    {
                        using var prs = PresentationDocument.Open(filePath, false);
                        var presentationPart = prs.PresentationPart;
                        if (presentationPart == null) break;

                        var slideIds = presentationPart.Presentation.SlideIdList?
                            .Elements<DocumentFormat.OpenXml.Presentation.SlideId>() ?? [];

                        int slideNum = 1;
                        foreach (var slideId in slideIds)
                        {
                            if (slideId.RelationshipId?.Value is not string relId) continue;
                            if (presentationPart.GetPartById(relId) is not SlidePart slidePart) continue;

                            var texts = slidePart.Slide
                                .Descendants<A.Text>()
                                .Select(t => t.Text.Trim())
                                .Where(t => !string.IsNullOrEmpty(t));

                            var slideText = string.Join("\n", texts);
                            if (!string.IsNullOrEmpty(slideText))
                                results.Add((slideNum, slideText));

                            slideNum++;
                        }
                        break;
                    }

                    case ".epub":
                    {
                        var book = await EpubReader.ReadBookAsync(filePath);
                        int chapterNum = 1;
                        const int epubChunkSize = 3000;

                        foreach (var item in book.ReadingOrder)
                        {
                            var html = item.Content ?? "";
                            var text = Regex.Replace(html, "<[^>]+>", " ");
                            text = System.Net.WebUtility.HtmlDecode(text);
                            text = Regex.Replace(text, @"\s{2,}", "\n").Trim();

                            if (text.Length < 100) continue;

                            // Split large chapters into sub-chunks to avoid token limits
                            for (int i = 0; i < text.Length; i += epubChunkSize)
                                results.Add((chapterNum, text.Substring(i, Math.Min(epubChunkSize, text.Length - i))));

                            chapterNum++;
                        }
                        break;
                    }

                    case ".csv":
                    {
                        var lines = await File.ReadAllLinesAsync(filePath);
                        if (lines.Length < 2) break;

                        var headers = ParseCsvLine(lines[0]);
                        const int rowsPerChunk = 50;

                        for (int i = 1; i < lines.Length; i += rowsPerChunk)
                        {
                            var chunkRows = lines.Skip(i).Take(rowsPerChunk)
                                .Select(line =>
                                {
                                    var values = ParseCsvLine(line);
                                    return string.Join(", ", headers.Zip(values, (h, v) => $"{h}: {v}"));
                                });

                            var chunk = string.Join("\n", chunkRows);
                            if (!string.IsNullOrWhiteSpace(chunk))
                                results.Add(((i / rowsPerChunk) + 1, chunk));
                        }
                        break;
                    }

                    default:
                        throw new NotSupportedException($"File format '{extension}' is not supported.");
                }

                await Task.CompletedTask;
            });

        AnsiConsole.MarkupLine($"[green]✓[/] Loaded [bold cyan]{results.Count}[/] sections from {Path.GetExtension(filePath).ToUpper()} file.");
        return results;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString().Trim());
        return result.ToArray();
    }
}
