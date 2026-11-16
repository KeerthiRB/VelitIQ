using System.Text.RegularExpressions;
using VelitIQ.Core.Interfaces;
using VelitIQ.Core.Models;

namespace VelitIQ.Infrastructure.Security;

public sealed class SecretGuardrailScanner
{
    private static readonly (string RuleName, SecretSeverity Severity, Regex Pattern)[] Rules =
    [
        ("PrivateKeyMaterial", SecretSeverity.Critical, new Regex(@"-----BEGIN\s+(?:RSA\s+|EC\s+|OPENSSH\s+)?PRIVATE\s+KEY-----", RegexOptions.Compiled)),
        ("DatabaseOrStorageConnectionString", SecretSeverity.Critical, new Regex(@"(?:Password|Pwd|AccountKey|SharedAccessKey)\s*=\s*[^\s;'""]{6,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("CloudOrAiApiKey", SecretSeverity.Critical, new Regex(@"\b(?:sk-[A-Za-z0-9_-]{20,}|AIza[0-9A-Za-z_-]{32,}|ghp_[A-Za-z0-9]{30,})\b", RegexOptions.Compiled)),
        ("HardcodedCredentialAssignment", SecretSeverity.Warning, new Regex(@"\b(?:api_key|apikey|client_secret|service_password|db_password)\s*[:=]\s*['""]?[A-Za-z0-9!@#$%^&*_\-]{8,}['""]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("PotentialSsnPattern", SecretSeverity.Warning, new Regex(@"\b(?!000|666|9\d{2})\d{3}-(?!00)\d{2}-(?!0000)\d{4}\b", RegexOptions.Compiled))
    ];

    public List<SecretScanFinding> ScanParsedDocument(Guid documentId, DocumentParseResult parseResult)
    {
        var findings = new List<SecretScanFinding>();
        var seenSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in parseResult.Chunks)
        {
            foreach (var (ruleName, severity, regex) in Rules)
            {
                foreach (Match m in regex.Matches(chunk.Content))
                {
                    var redacted = RedactMatch(m.Value);
                    var sig = $"{ruleName}:{chunk.PageOrSheetName}:{redacted}";
                    if (!seenSignatures.Add(sig)) continue;

                    findings.Add(new SecretScanFinding
                    {
                        DocumentId = documentId,
                        RuleName = ruleName,
                        Severity = severity,
                        Location = string.IsNullOrWhiteSpace(chunk.PageOrSheetName) ? chunk.SectionPath : chunk.PageOrSheetName,
                        RedactedPreview = redacted
                    });

                    if (findings.Count >= 25) return findings;
                }
            }
        }

        return findings;
    }

    private static string RedactMatch(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "***";
        if (raw.Length <= 8) return new string('*', raw.Length);
        return $"{raw[..4]}****{raw[^3..]}";
    }
}
