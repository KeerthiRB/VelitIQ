using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace VelitIQ.Infrastructure.Data;

/// <summary>
/// Builds genuine binary .xlsx (with Excel formulas), .docx (with OpenXML headings & tables),
/// .csv, and .pdf (OneNote export layout) artifacts for bootstrap seeding and automated E2E testing.
/// </summary>
public static class SampleArtifactBuilder
{
    public static MemoryStream BuildNostroExcelWorkbook()
    {
        var wb = new XLWorkbook();

        var ws1 = wb.Worksheets.Add("Nostro_Balances");
        ws1.Cell(1, 1).Value = "Account_Code";
        ws1.Cell(1, 2).Value = "Correspondent_Bank";
        ws1.Cell(1, 3).Value = "Currency";
        ws1.Cell(1, 4).Value = "Ledger_Balance_USD";
        ws1.Cell(1, 5).Value = "Statement_Balance_USD";
        ws1.Cell(1, 6).Value = "Unreconciled_Break_USD";
        ws1.Cell(1, 7).Value = "Break_Owner";

        var rows = new (string Code, string Bank, string Ccy, double Ledger, double Stmt, string Owner)[]
        {
            ("NOSTRO-USD-101", "JPMorgan Chase NY", "USD", 14500000, 14485000, "SWIFT_Ops_Desk"),
            ("NOSTRO-EUR-204", "Deutsche Bank Frankfurt", "EUR", 9820000, 9820000, "FX_Settlement"),
            ("NOSTRO-GBP-309", "Barclays London", "GBP", 6410000, 6368000, "Treasury_Liquidity"),
            ("NOSTRO-JPY-412", "MUFG Tokyo", "JPY", 4150000, 4150000, "APAC_Clearing"),
            ("NOSTRO-CAD-518", "RBC Toronto", "CAD", 3290000, 3267500, "NorthAmerica_Wire")
        };

        for (int i = 0; i < rows.Length; i++)
        {
            int r = i + 2;
            ws1.Cell(r, 1).Value = rows[i].Code;
            ws1.Cell(r, 2).Value = rows[i].Bank;
            ws1.Cell(r, 3).Value = rows[i].Ccy;
            ws1.Cell(r, 4).Value = rows[i].Ledger;
            ws1.Cell(r, 5).Value = rows[i].Stmt;
            ws1.Cell(r, 6).FormulaA1 = $"=D{r}-E{r}";
            ws1.Cell(r, 7).Value = rows[i].Owner;
        }

        ws1.Cell(8, 1).Value = "TOTAL_PORTFOLIO";
        ws1.Cell(8, 4).FormulaA1 = "=SUM(D2:D6)";
        ws1.Cell(8, 5).FormulaA1 = "=SUM(E2:E6)";
        ws1.Cell(8, 6).FormulaA1 = "=SUM(F2:F6)";

        var ws2 = wb.Worksheets.Add("Break_Escalation_Rules");
        ws2.Cell(1, 1).Value = "Severity_Tier";
        ws2.Cell(1, 2).Value = "Threshold_USD";
        ws2.Cell(1, 3).Value = "SLA_Hours";
        ws2.Cell(1, 4).Value = "Approver_DL";

        ws2.Cell(2, 1).Value = "Tier-1 Minor";
        ws2.Cell(2, 2).Value = 10000;
        ws2.Cell(2, 3).Value = 24;
        ws2.Cell(2, 4).Value = "treasury-recon@horizonbank.com";

        ws2.Cell(3, 1).Value = "Tier-2 Material";
        ws2.Cell(3, 2).Value = 25000;
        ws2.Cell(3, 3).Value = 4;
        ws2.Cell(3, 4).Value = "vp-settlements@horizonbank.com";

        ws2.Cell(4, 1).Value = "Tier-3 Executive";
        ws2.Cell(4, 2).Value = 100000;
        ws2.Cell(4, 3).Value = 1;
        ws2.Cell(4, 4).Value = "cfo-treasury-alert@horizonbank.com";

        wb.RecalculateAllFormulas();

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    public static MemoryStream BuildQuantRiskExcelWorkbook()
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Counterparty_VaR_Limits");
        ws.Cell(1, 1).Value = "Desk_Code";
        ws.Cell(1, 2).Value = "Asset_Class";
        ws.Cell(1, 3).Value = "Daily_VaR_99_USD";
        ws.Cell(1, 4).Value = "Approved_Limit_USD";
        ws.Cell(1, 5).Value = "Headroom_USD";
        ws.Cell(1, 6).Value = "Model_Engine";

        var desks = new (string Desk, string Asset, double Var99, double Limit, string Engine)[]
        {
            ("RATES-NY-01", "Interest Rate Swaps", 4250000, 5000000, "MUREX_HVAR_V4"),
            ("FX-G10-02", "G10 Spot & Forwards", 2890000, 3500000, "CALYPSO_MC_SIM"),
            ("CREDIT-IG-03", "Investment Grade CDS", 3120000, 3250000, "RISK_GRID_PRD_09")
        };

        for (int i = 0; i < desks.Length; i++)
        {
            int r = i + 2;
            ws.Cell(r, 1).Value = desks[i].Desk;
            ws.Cell(r, 2).Value = desks[i].Asset;
            ws.Cell(r, 3).Value = desks[i].Var99;
            ws.Cell(r, 4).Value = desks[i].Limit;
            ws.Cell(r, 5).FormulaA1 = $"=D{r}-C{r}";
            ws.Cell(r, 6).Value = desks[i].Engine;
        }

        wb.RecalculateAllFormulas();
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    public static MemoryStream BuildCutoffCalendarCsv()
    {
        const string csv = """
            Payment_Rail,Region,Standard_Cutoff_EST,December_YearEnd_Cutoff_EST,Clearing_System,Escalation_Queue
            FEDWIRE_FUNDS,United States,18:00 EST,16:30 EST,FRB_FEDLINE_PRD,wire-ops@horizonbank.com
            SWIFT_MT103,Global Cross-Border,17:00 EST,15:30 EST,SWIFT_ALLIANCE_GW,swift-support@horizonbank.com
            CHIPS_HIGH_VAL,United States,17:00 EST,16:00 EST,TCH_CHIPS_NODE2,wire-ops@horizonbank.com
            SEPA_INSTANT,Eurozone,23:59 EST,23:59 EST,EBA_STEP2_CONN,emea-clearing@horizonbank.com
            ACH_NACHA_BATCH,United States,19:30 EST,18:00 EST,PRD-ACH-ENG-02,ach-settlement@horizonbank.com
            """;
        return new MemoryStream(Encoding.UTF8.GetBytes(csv));
    }

    public static MemoryStream BuildCoreBankingWordRunbook()
    {
        var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            AppendHeading(body, "Core General Ledger (GL) EOD Batch & Kafka Recovery Runbook", "Heading1");
            AppendParagraph(body, "Author: Rahul Menon (Principal Platform Architect) | Target Successor: Meera Krishnan | System Tier: Tier-0 Mission Critical.");

            AppendHeading(body, "1. Architecture & Nightly Batch Window", "Heading2");
            AppendParagraph(body, "The Core Banking General Ledger posting pipeline runs daily at 21:30 EST via CTRL-M job GL_EOD_POST_MASTER on host PRD-GL-04. Upstream sub-ledger events stream continuously from KAFKA_LEDGER_FEED into staging table GL_RECON_STG in PostgreSQL cluster PRD-PG-CORE-01.");

            AppendHeading(body, "2. Failure Scenarios & Step-by-Step Recovery", "Heading2");
            AppendParagraph(body, "If GL_EOD_POST_MASTER aborts with error code ERR_UNBALANCED_JOURNAL_904, do NOT force-complete the CTRL-M job. Execute diagnostic script /opt/horizon/gl/verify_suspense_balance.sh under service account svc_gl_batch and move out-of-balance entries above $500 to suspense account GL_9999_SUSPENSE with Dual-Control approval from Finance Controllers (gl-controllers@horizonbank.com).");

            AppendHeading(body, "3. Critical Microservice & Batch Inventory", "Heading2");
            var tbl = new Table();
            tbl.AppendChild(BuildTableRow("Component", "Host / Topic", "SLA Window", "L3 Escalation"));
            tbl.AppendChild(BuildTableRow("GL_EOD_POST_MASTER", "PRD-GL-04", "21:30 - 23:15 EST", "core-ledger-oncall@horizonbank.com"));
            tbl.AppendChild(BuildTableRow("KAFKA_LEDGER_FEED", "kafka-prd-broker-01..05", "Real-time < 5s lag", "streaming-platform@horizonbank.com"));
            tbl.AppendChild(BuildTableRow("REGULATORY_EXTRACT_FDIC", "PRD-REG-02", "Daily at 04:00 EST", "reg-tech-support@horizonbank.com"));
            body.AppendChild(tbl);

            mainPart.Document.Save();
        }

        ms.Position = 0;
        return ms;
    }

    private static void AppendHeading(Body body, string text, string styleId)
    {
        var p = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
            new Run(new Text(text)));
        body.AppendChild(p);
    }

    private static void AppendParagraph(Body body, string text)
    {
        body.AppendChild(new Paragraph(new Run(new Text(text))));
    }

    private static TableRow BuildTableRow(params string[] cells)
    {
        var tr = new TableRow();
        foreach (var c in cells)
        {
            tr.AppendChild(new TableCell(new Paragraph(new Run(new Text(c)))));
        }
        return tr;
    }

    /// <summary>
    /// Generates a valid, standards-compliant multi-line PDF 1.4 document representing an exported OneNote notebook
    /// so PdfPig extracts every line with exact coordinates.
    /// </summary>
    public static MemoryStream BuildOneNoteExportedPdf(string title, IReadOnlyList<string> lines)
    {
        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine("BT");
        contentBuilder.AppendLine("/F1 13 Tf");
        contentBuilder.AppendLine("45 750 Td");
        contentBuilder.AppendLine($"({EscapePdfString(title)}) Tj");
        contentBuilder.AppendLine("/F1 10 Tf");

        foreach (var line in lines)
        {
            contentBuilder.AppendLine("0 -16 Td");
            contentBuilder.AppendLine($"({EscapePdfString(line)}) Tj");
        }
        contentBuilder.AppendLine("ET");

        var streamBytes = Encoding.ASCII.GetBytes(contentBuilder.ToString());

        using var ms = new MemoryStream();
        void WriteAscii(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        WriteAscii("%PDF-1.4\n");

        var offsets = new List<long> { 0 };

        // Obj 1: Catalog
        offsets.Add(ms.Position);
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        // Obj 2: Pages
        offsets.Add(ms.Position);
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Obj 3: Page
        offsets.Add(ms.Position);
        WriteAscii("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        // Obj 4: Font
        offsets.Add(ms.Position);
        WriteAscii("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        // Obj 5: Content Stream
        offsets.Add(ms.Position);
        WriteAscii($"5 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
        ms.Write(streamBytes);
        WriteAscii("\nendstream\nendobj\n");

        long xrefStart = ms.Position;
        WriteAscii($"xref\n0 {offsets.Count}\n");
        WriteAscii("0000000000 65535 f \n");
        for (int i = 1; i < offsets.Count; i++)
        {
            WriteAscii($"{offsets[i]:D10} 00000 n \n");
        }

        WriteAscii($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");

        var outMs = new MemoryStream(ms.ToArray());
        outMs.Position = 0;
        return outMs;
    }

    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
