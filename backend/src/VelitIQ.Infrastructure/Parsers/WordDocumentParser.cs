using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Parsers;

public sealed class WordDocumentParser : IDocumentFormatParser
{
    public bool CanParse(ArtifactFormat format, string extension) =>
        format == ArtifactFormat.Word ||
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentParseResult> ParseAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        using var wordDoc = WordprocessingDocument.Open(fileStream, false);
        var mainPart = wordDoc.MainDocumentPart;
        var body = mainPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult(new DocumentParseResult(0, 0, 0, [], []));
        }

        int imageCount = mainPart!.ImageParts.Count();
        var headingStack = new string?[4];
        headingStack[0] = Path.GetFileNameWithoutExtension(fileName);

        var chunks = new List<ParsedSectionChunk>();
        var sectionBuffer = new StringBuilder();
        int sectionCounter = 1;

        void FlushBuffer()
        {
            if (sectionBuffer.Length == 0) return;
            var path = string.Join(" > ", headingStack.Where(s => !string.IsNullOrWhiteSpace(s)));
            var split = TextChunkingHelper.SplitSectionIntoChunks(
                sectionBuffer.ToString(),
                "DocumentBody",
                path,
                $"Section {sectionCounter}");
            chunks.AddRange(split);
            sectionBuffer.Clear();
        }

        foreach (var element in body.Elements())
        {
            if (element is Paragraph para)
            {
                var text = para.InnerText?.Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;

                var headingLevel = DetectHeadingLevel(para);
                if (headingLevel.HasValue)
                {
                    FlushBuffer();
                    sectionCounter++;
                    int lvl = Math.Clamp(headingLevel.Value, 1, 3);
                    headingStack[lvl] = text;
                    for (int k = lvl + 1; k < headingStack.Length; k++) headingStack[k] = null;
                    sectionBuffer.AppendLine($"## {text}");
                }
                else
                {
                    sectionBuffer.AppendLine(text);
                    sectionBuffer.AppendLine();
                }
            }
            else if (element is Table table)
            {
                var mdTable = ConvertWordTableToMarkdown(table);
                if (!string.IsNullOrWhiteSpace(mdTable))
                {
                    sectionBuffer.AppendLine(mdTable);
                    sectionBuffer.AppendLine();
                }
            }
        }

        FlushBuffer();
        return Task.FromResult(new DocumentParseResult(Math.Max(1, sectionCounter), 0, imageCount, chunks, []));
    }

    private static int? DetectHeadingLevel(Paragraph para)
    {
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(styleId.AsSpan(7), out var hNum))
            {
                return hNum;
            }
            if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 1;
            if (styleId.Equals("Subtitle", StringComparison.OrdinalIgnoreCase)) return 2;
        }

        return null;
    }

    private static string ConvertWordTableToMarkdown(Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        bool headerWritten = false;
        int colCount = 0;

        foreach (var row in rows)
        {
            var cells = row.Elements<TableCell>()
                .Select(c => (c.InnerText ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim())
                .ToList();

            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            if (!headerWritten)
            {
                colCount = cells.Count;
                sb.AppendLine("| " + string.Join(" | ", cells) + " |");
                sb.AppendLine("| " + string.Join(" | ", cells.Select(_ => "---")) + " |");
                headerWritten = true;
            }
            else
            {
                while (cells.Count < colCount) cells.Add(string.Empty);
                sb.AppendLine("| " + string.Join(" | ", cells.Take(colCount)) + " |");
            }
        }

        return sb.ToString();
    }
}
