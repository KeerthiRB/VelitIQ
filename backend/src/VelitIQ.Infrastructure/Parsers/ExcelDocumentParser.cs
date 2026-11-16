using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using ExcelDataReader;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Parsers;

public sealed class ExcelDocumentParser : IDocumentFormatParser
{
    static ExcelDocumentParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public bool CanParse(ArtifactFormat format, string extension) =>
        format == ArtifactFormat.Excel ||
        extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xls", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentParseResult> ParseAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".xls", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ParseLegacyXls(fileStream, fileName));
        }

        return Task.FromResult(ParseModernXlsx(fileStream, fileName));
    }

    private static DocumentParseResult ParseModernXlsx(Stream fileStream, string fileName)
    {
        using var workbook = new XLWorkbook(fileStream);
        var chunks = new List<ParsedSectionChunk>();
        var sheets = new List<ParsedSheetData>();
        int totalFormulas = 0;
        int sheetIdx = 0;

        foreach (var ws in workbook.Worksheets)
        {
            var range = ws.RangeUsed();
            if (range is null) continue;

            var formulas = new List<SpreadsheetFormulaInfo>();
            foreach (var cell in range.CellsUsed(c => c.HasFormula))
            {
                var formulaText = cell.FormulaA1;
                var refSheets = Regex.Matches(formulaText, @"([A-Za-z0-9_']+)!")
                    .Select(m => m.Groups[1].Value.Trim('\''))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string cached = string.Empty;
                try { cached = cell.CachedValue.ToString(); } catch { /* ignore uncached formula eval */ }

                formulas.Add(new SpreadsheetFormulaInfo
                {
                    CellAddress = cell.Address.ToStringRelative(),
                    Formula = "=" + formulaText.TrimStart('='),
                    CachedValue = cached,
                    ReferencedSheets = refSheets
                });
            }

            totalFormulas += formulas.Count;

            // Identify header row (first row with >= 2 populated cells, or first used row)
            var usedRows = range.RowsUsed().ToList();
            if (usedRows.Count == 0) continue;

            var headerRow = usedRows.FirstOrDefault(r => r.CellsUsed().Count() >= 2) ?? usedRows[0];
            int firstCol = range.FirstColumn().ColumnNumber();
            int lastCol = Math.Min(range.LastColumn().ColumnNumber(), firstCol + 49);

            var headers = new List<string>();
            var colNumbers = new List<int>();
            for (int col = firstCol; col <= lastCol; col++)
            {
                var rawHeader = ws.Cell(headerRow.RowNumber(), col).GetFormattedString()?.Trim();
                var h = string.IsNullOrWhiteSpace(rawHeader) ? $"Column_{col - firstCol + 1}" : rawHeader;
                if (headers.Contains(h, StringComparer.OrdinalIgnoreCase)) h = $"{h}_{col}";
                headers.Add(h);
                colNumbers.Add(col);
            }

            var dataRows = new List<Dictionary<string, object?>>();
            foreach (var row in usedRows.Where(r => r.RowNumber() > headerRow.RowNumber()))
            {
                var rowDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                bool anyPopulated = false;
                for (int i = 0; i < colNumbers.Count; i++)
                {
                    var cell = ws.Cell(row.RowNumber(), colNumbers[i]);
                    if (cell.IsEmpty())
                    {
                        rowDict[headers[i]] = null;
                        continue;
                    }

                    anyPopulated = true;
                    rowDict[headers[i]] = ExtractCellTypedValue(cell);
                }
                if (anyPopulated) dataRows.Add(rowDict);
            }

            var colSchemas = TextChunkingHelper.BuildColumnSchemas(headers, dataRows);
            var summaryMd = TextChunkingHelper.BuildSheetSummaryMarkdown(fileName, ws.Name, colSchemas, formulas, dataRows);

            chunks.Add(new ParsedSectionChunk(
                "SpreadsheetSummary",
                $"{fileName} > Sheet '{ws.Name}' > Schema & Summary",
                $"Sheet: {ws.Name}",
                summaryMd));

            if (formulas.Count > 0)
            {
                var fBuilder = new StringBuilder();
                fBuilder.AppendLine($"### Excel Formula Catalog — `{fileName}` / Sheet `{ws.Name}`");
                foreach (var f in formulas.Take(50))
                {
                    var crossRef = f.ReferencedSheets.Count > 0 ? $" [References: {string.Join(", ", f.ReferencedSheets)}]" : "";
                    fBuilder.AppendLine($"- `{f.CellAddress}`: `{f.Formula}` -> Value `{f.CachedValue}`{crossRef}");
                }
                chunks.Add(new ParsedSectionChunk(
                    "FormulaCatalog",
                    $"{fileName} > Sheet '{ws.Name}' > Formulas & Business Logic",
                    $"Sheet: {ws.Name} (Formulas)",
                    fBuilder.ToString()));
            }

            // Add row-batch semantic chunks for text lookup
            const int batchSize = 25;
            for (int i = 0; i < dataRows.Count; i += batchSize)
            {
                var batch = dataRows.Skip(i).Take(batchSize).ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"Workbook `{fileName}` | Sheet `{ws.Name}` | Rows {i + 1}–{i + batch.Count}:");
                foreach (var r in batch)
                {
                    var parts = headers
                        .Where(h => r.TryGetValue(h, out var v) && v is not null && !string.IsNullOrWhiteSpace(v.ToString()))
                        .Select(h => $"{h}: {r[h]}");
                    sb.AppendLine("- " + string.Join(" | ", parts));
                }
                chunks.Add(new ParsedSectionChunk(
                    "DocumentBody",
                    $"{fileName} > Sheet '{ws.Name}' > Rows {i + 1}-{i + batch.Count}",
                    $"Sheet: {ws.Name} (Rows {i + 1}-{i + batch.Count})",
                    sb.ToString()));
            }

            sheets.Add(new ParsedSheetData(ws.Name, sheetIdx++, colSchemas, formulas, dataRows, summaryMd));
        }

        return new DocumentParseResult(sheets.Count, totalFormulas, 0, chunks, sheets);
    }

    private static object? ExtractCellTypedValue(IXLCell cell)
    {
        try
        {
            var val = cell.HasFormula ? cell.CachedValue : cell.Value;
            if (val.IsBlank) return null;
            if (val.IsNumber) return Math.Round(val.GetNumber(), 4);
            if (val.IsBoolean) return val.GetBoolean();
            if (val.IsDateTime) return val.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return val.ToString().Trim();
        }
        catch
        {
            return cell.GetFormattedString()?.Trim();
        }
    }

    private static DocumentParseResult ParseLegacyXls(Stream fileStream, string fileName)
    {
        using var reader = ExcelReaderFactory.CreateReader(fileStream);
        var ds = reader.AsDataSet();
        var chunks = new List<ParsedSectionChunk>();
        var sheets = new List<ParsedSheetData>();
        int idx = 0;

        foreach (DataTable table in ds.Tables)
        {
            if (table.Rows.Count == 0) continue;
            var headers = new List<string>();
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var h = table.Rows[0][c]?.ToString()?.Trim();
                headers.Add(string.IsNullOrWhiteSpace(h) ? $"Column_{c + 1}" : h);
            }

            var rows = new List<Dictionary<string, object?>>();
            for (int r = 1; r < table.Rows.Count; r++)
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                bool any = false;
                for (int c = 0; c < headers.Count; c++)
                {
                    var cellVal = table.Rows[r][c];
                    if (cellVal is not null && cellVal != DBNull.Value && !string.IsNullOrWhiteSpace(cellVal.ToString()))
                    {
                        any = true;
                        dict[headers[c]] = cellVal;
                    }
                    else
                    {
                        dict[headers[c]] = null;
                    }
                }
                if (any) rows.Add(dict);
            }

            var schemas = TextChunkingHelper.BuildColumnSchemas(headers, rows);
            var summary = TextChunkingHelper.BuildSheetSummaryMarkdown(fileName, table.TableName, schemas, [], rows);
            chunks.Add(new ParsedSectionChunk("SpreadsheetSummary", $"{fileName} > {table.TableName}", $"Sheet: {table.TableName}", summary));
            sheets.Add(new ParsedSheetData(table.TableName, idx++, schemas, [], rows, summary));
        }

        return new DocumentParseResult(sheets.Count, 0, 0, chunks, sheets);
    }
}
