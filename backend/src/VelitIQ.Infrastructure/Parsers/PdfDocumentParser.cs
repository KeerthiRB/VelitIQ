using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Parsers;

/// <summary>
/// Layout-preserving PDF parser designed for both standard banking PDFs and OneNote-exported PDFs
/// (reconstructing spatial horizontal lines, wide canvas blocks, and counting embedded screenshots/diagrams).
/// </summary>
public sealed class PdfDocumentParser : IDocumentFormatParser
{
    public bool CanParse(ArtifactFormat format, string extension) =>
        format == ArtifactFormat.Pdf ||
        extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentParseResult> ParseAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        using var pdf = PdfDocument.Open(fileStream);
        var chunks = new List<ParsedSectionChunk>();
        var extractedImages = new List<byte[]>();
        int totalDiagrams = 0;
        var docTitle = Path.GetFileNameWithoutExtension(fileName);

        foreach (var page in pdf.GetPages())
        {
            var pageText = ReconstructSpatialPageText(page);
            int pageImagesCount = 0;

            try
            {
                foreach (var img in page.GetImages())
                {
                    pageImagesCount++;
                    if (extractedImages.Count < 6 && img.TryGetPng(out var pngBytes) && pngBytes is { Length: > 512 })
                    {
                        extractedImages.Add(pngBytes);
                    }
                }
            }
            catch
            {
                // Ignore unsupported inline image encodings while still extracting full page text
            }

            totalDiagrams += pageImagesCount;

            if (string.IsNullOrWhiteSpace(pageText) && pageImagesCount > 0)
            {
                pageText = $"[Visual / Diagram Page {page.Number} in `{fileName}`: Contains {pageImagesCount} embedded diagram(s) or OneNote screenshot(s). Configure Azure AI Foundry Vision deployment (`gpt-4o`) for automated visual diagram transcription.]";
            }
            else if (pageImagesCount > 0)
            {
                pageText += $"\n\n[Note: Page {page.Number} also contains {pageImagesCount} embedded diagram/screenshot artifact(s) from PDF/OneNote export.]";
            }

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                var pageChunks = TextChunkingHelper.SplitSectionIntoChunks(
                    pageText,
                    "DocumentBody",
                    $"{docTitle} > Page {page.Number}",
                    $"Page {page.Number}",
                    metadata: new Dictionary<string, object?>
                    {
                        ["pageNumber"] = page.Number,
                        ["embeddedImageCount"] = pageImagesCount
                    });

                chunks.AddRange(pageChunks);
            }
        }

        return Task.FromResult(new DocumentParseResult(
            pdf.NumberOfPages,
            0,
            totalDiagrams,
            chunks,
            [],
            extractedImages));
    }

    private static string ReconstructSpatialPageText(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0) return page.Text?.Trim() ?? string.Empty;

        // Group words by approximate baseline Y coordinate (within 3.5 pt tolerance) from top to bottom
        var lines = new List<(double Y, List<Word> Words)>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var y = word.BoundingBox.Bottom;
            var existingLine = lines.FirstOrDefault(l => Math.Abs(l.Y - y) <= 3.5);
            if (existingLine.Words is not null)
            {
                existingLine.Words.Add(word);
            }
            else
            {
                lines.Add((y, [word]));
            }
        }

        var sb = new StringBuilder();
        double? prevY = null;

        foreach (var line in lines.OrderByDescending(l => l.Y))
        {
            if (prevY.HasValue && (prevY.Value - line.Y) > 18.0)
            {
                sb.AppendLine();
            }

            var orderedWords = line.Words.OrderBy(w => w.BoundingBox.Left).ToList();
            var lineBuilder = new StringBuilder();
            double? prevRight = null;

            foreach (var w in orderedWords)
            {
                if (prevRight.HasValue)
                {
                    var gap = w.BoundingBox.Left - prevRight.Value;
                    // Wide horizontal gap indicates a table column or OneNote side-by-side canvas block
                    lineBuilder.Append(gap > 24.0 ? "   |   " : " ");
                }
                lineBuilder.Append(w.Text);
                prevRight = w.BoundingBox.Right;
            }

            sb.AppendLine(lineBuilder.ToString().TrimEnd());
            prevY = line.Y;
        }

        return sb.ToString().Trim();
    }
}
