using System.Globalization;
using System.Text;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Parsers;

/// <summary>
/// RFC-4180 compliant streaming CSV / TSV / semicolon-delimited tabular parser.
/// Preserves quoted fields, escaped quotes, numeric types, and prepares both structured SQL rows and semantic chunks.
/// </summary>
public sealed class CsvDocumentParser : IDocumentFormatParser
{
    public bool CanParse(ArtifactFormat format, string extension) =>
        format == ArtifactFormat.Csv || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase);

    public async Task<DocumentParseResult> ParseAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(ct);

        var firstLine = content.Split('\n').FirstOrDefault() ?? string.Empty;
        char delimiter = new[] { ',', ';', '\t', '|' }
            .OrderByDescending(d => firstLine.Count(ch => ch == d))
            .First();

        var parsedRecords = ParseRfc4180Records(content, delimiter);
        var headers = new List<string>();
        var rows = new List<Dictionary<string, object?>>();

        if (parsedRecords.Count > 0)
        {
            var rawHeaders = parsedRecords[0];
            for (int i = 0; i < rawHeaders.Count; i++)
            {
                var h = string.IsNullOrWhiteSpace(rawHeaders[i]) ? $"Column_{i + 1}" : rawHeaders[i].Trim();
                if (headers.Contains(h, StringComparer.OrdinalIgnoreCase)) h = $"{h}_{i + 1}";
                headers.Add(h);
            }

            for (int rIdx = 1; rIdx < parsedRecords.Count; rIdx++)
            {
                var fields = parsedRecords[rIdx];
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                bool anyValue = false;

                for (int cIdx = 0; cIdx < headers.Count; cIdx++)
                {
                    var val = cIdx < fields.Count ? fields[cIdx].Trim() : null;
                    if (!string.IsNullOrEmpty(val)) anyValue = true;
                    dict[headers[cIdx]] = ParseTypedValue(val);
                }

                if (anyValue) rows.Add(dict);
            }
        }

        var sheetName = Path.GetFileNameWithoutExtension(fileName);
        var columns = TextChunkingHelper.BuildColumnSchemas(headers, rows);
        var summaryMd = TextChunkingHelper.BuildSheetSummaryMarkdown(fileName, sheetName, columns, [], rows);

        var chunks = new List<ParsedSectionChunk>
        {
            new("SpreadsheetSummary", $"{fileName} > Schema & Metrics", $"CSV: {sheetName}", summaryMd)
        };

        const int batchSize = 25;
        for (int i = 0; i < rows.Count; i += batchSize)
        {
            var batch = rows.Skip(i).Take(batchSize).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"CSV `{fileName}` rows {i + 1}–{i + batch.Count}:");
            foreach (var r in batch)
            {
                var kvs = headers
                    .Where(h => r.TryGetValue(h, out var v) && v is not null && !string.IsNullOrWhiteSpace(v.ToString()))
                    .Select(h => $"{h}: {r[h]}");
                sb.AppendLine("- " + string.Join(" | ", kvs));
            }
            chunks.Add(new ParsedSectionChunk(
                "DocumentBody",
                $"{fileName} > Rows {i + 1}-{i + batch.Count}",
                $"CSV: {sheetName} (Rows {i + 1}-{i + batch.Count})",
                sb.ToString()));
        }

        var sheetData = new ParsedSheetData(sheetName, 0, columns, [], rows, summaryMd);
        return new DocumentParseResult(1, 0, 0, chunks, [sheetData]);
    }

    private static List<List<string>> ParseRfc4180Records(string text, char delimiter)
    {
        var records = new List<List<string>>();
        var currentRecord = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    currentRecord.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    continue;
                }
                else if (c == '\n')
                {
                    currentRecord.Add(field.ToString());
                    field.Clear();
                    if (currentRecord.Any(f => !string.IsNullOrWhiteSpace(f)))
                        records.Add(currentRecord);
                    currentRecord = [];
                }
                else
                {
                    field.Append(c);
                }
            }
        }

        if (field.Length > 0 || currentRecord.Count > 0)
        {
            currentRecord.Add(field.ToString());
            if (currentRecord.Any(f => !string.IsNullOrWhiteSpace(f)))
                records.Add(currentRecord);
        }

        return records;
    }

    private static object? ParseTypedValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleanNum = raw.Replace("$", "").Replace(",", "").Trim();
        if ((!raw.StartsWith('0') || raw == "0" || raw.StartsWith("0.")) &&
            double.TryParse(cleanNum, NumberStyles.Float, CultureInfo.InvariantCulture, out var numVal))
        {
            return numVal;
        }
        return raw;
    }
}
