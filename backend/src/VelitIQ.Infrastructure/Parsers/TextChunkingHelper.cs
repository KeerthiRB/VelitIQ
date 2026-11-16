using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Parsers;

public static class TextChunkingHelper
{
    public static List<ParsedSectionChunk> SplitSectionIntoChunks(
        string text,
        string sourceType,
        string sectionPath,
        string pageOrSheetName,
        int maxChars = 1100,
        int overlapChars = 180,
        Dictionary<string, object?>? metadata = null)
    {
        var results = new List<ParsedSectionChunk>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        var normalized = Regex.Replace(text.Replace("\r\n", "\n"), @"\n{3,}", "\n\n").Trim();
        if (normalized.Length <= maxChars)
        {
            results.Add(new ParsedSectionChunk(sourceType, sectionPath, pageOrSheetName, normalized, metadata));
            return results;
        }

        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var para in paragraphs)
        {
            if (current.Length > 0 && current.Length + para.Length + 2 > maxChars)
            {
                var chunkText = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    results.Add(new ParsedSectionChunk(sourceType, sectionPath, pageOrSheetName, chunkText, metadata));
                }

                var tail = chunkText.Length > overlapChars
                    ? chunkText[^overlapChars..]
                    : chunkText;
                var newlineIdx = tail.IndexOf('\n');
                if (newlineIdx >= 0 && newlineIdx < tail.Length - 20)
                    tail = tail[(newlineIdx + 1)..];

                current.Clear();
                current.Append(tail).Append("\n\n");
            }

            if (para.Length > maxChars)
            {
                // Split very large single paragraph on sentence or line boundaries
                var lines = para.Split('\n');
                foreach (var line in lines)
                {
                    if (current.Length + line.Length + 1 > maxChars && current.Length > 100)
                    {
                        results.Add(new ParsedSectionChunk(sourceType, sectionPath, pageOrSheetName, current.ToString().Trim(), metadata));
                        current.Clear();
                    }
                    current.AppendLine(line);
                }
            }
            else
            {
                if (current.Length > 0) current.Append("\n\n");
                current.Append(para);
            }
        }

        if (current.Length > 0)
        {
            var remaining = current.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(remaining))
                results.Add(new ParsedSectionChunk(sourceType, sectionPath, pageOrSheetName, remaining, metadata));
        }

        return results;
    }

    public static List<TabularColumnSchema> BuildColumnSchemas(
        IReadOnlyList<string> headers,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var schemas = new List<TabularColumnSchema>();

        foreach (var header in headers)
        {
            var nonNullStrings = rows
                .Select(r => r.TryGetValue(header, out var v) && v is not null ? v.ToString()?.Trim() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();

            var nums = new List<double>();
            int dateCount = 0;
            int boolCount = 0;

            foreach (var s in nonNullStrings)
            {
                var cleaned = s.Replace("$", "").Replace(",", "").Trim();
                if (double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    nums.Add(d);
                else if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    dateCount++;
                else if (bool.TryParse(s, out _))
                    boolCount++;
            }

            string inferredType = "string";
            if (nonNullStrings.Count > 0)
            {
                if (nums.Count >= nonNullStrings.Count * 0.75) inferredType = "number";
                else if (dateCount >= nonNullStrings.Count * 0.75) inferredType = "date";
                else if (boolCount >= nonNullStrings.Count * 0.75) inferredType = "boolean";
            }

            schemas.Add(new TabularColumnSchema
            {
                Name = header,
                NormalizedName = Regex.Replace(header.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_'),
                DataType = inferredType,
                SampleValues = nonNullStrings.Distinct().Take(4).ToList(),
                NumericMin = inferredType == "number" && nums.Count > 0 ? Math.Round(nums.Min(), 2) : null,
                NumericMax = inferredType == "number" && nums.Count > 0 ? Math.Round(nums.Max(), 2) : null,
                NumericSum = inferredType == "number" && nums.Count > 0 ? Math.Round(nums.Sum(), 2) : null
            });
        }

        return schemas;
    }

    public static string BuildSheetSummaryMarkdown(
        string fileName,
        string sheetName,
        IReadOnlyList<TabularColumnSchema> columns,
        IReadOnlyList<SpreadsheetFormulaInfo> formulas,
        IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### Spreadsheet Summary: `{fileName}` — Sheet `{sheetName}`");
        sb.AppendLine($"- **Total Data Rows:** {rows.Count}");
        sb.AppendLine($"- **Columns ({columns.Count}):** {string.Join(", ", columns.Select(c => $"`{c.Name}` ({c.DataType})"))}");

        var numCols = columns.Where(c => c.DataType == "number" && c.NumericSum.HasValue).ToList();
        if (numCols.Count > 0)
        {
            sb.AppendLine("- **Key Numeric Metrics:**");
            foreach (var nc in numCols)
            {
                sb.AppendLine($"  - `{nc.Name}`: Sum = {nc.NumericSum:N2}, Min = {nc.NumericMin:N2}, Max = {nc.NumericMax:N2}");
            }
        }

        if (formulas.Count > 0)
        {
            sb.AppendLine($"- **Detected Excel Formulas ({formulas.Count}):**");
            foreach (var f in formulas.Take(12))
            {
                sb.AppendLine($"  - Cell `{f.CellAddress}`: `={f.Formula.TrimStart('=')}` (Cached Value: `{f.CachedValue}`)");
            }
        }

        if (rows.Count > 0 && columns.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("#### Sample Records:");
            var previewCols = columns.Take(8).Select(c => c.Name).ToList();
            sb.AppendLine("| " + string.Join(" | ", previewCols) + " |");
            sb.AppendLine("| " + string.Join(" | ", previewCols.Select(_ => "---")) + " |");
            foreach (var r in rows.Take(8))
            {
                var vals = previewCols.Select(c => r.TryGetValue(c, out var v) && v is not null ? v.ToString()!.Replace("|", "\\|").Replace("\n", " ") : "");
                sb.AppendLine("| " + string.Join(" | ", vals) + " |");
            }
        }

        return sb.ToString();
    }
}
