using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>
/// Builds a scrubbed, bounded diagnostic log for an import queue item to upload to
/// <c>POST /api/admin/import/diagnostics</c> (stored in R2 with a 30-day expiry).
/// Local folder paths, drive letters, UNC share names, emails, bearer tokens and query secrets
/// are stripped before anything leaves this computer.
/// </summary>
public static class DiagnosticLog
{
    /// <summary>Maximum UTF-8 payload size sent to admin-api (well under the 512 KB server cap).</summary>
    public const int MaxBytes = 256 * 1024;

    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly Regex EmailPattern = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", Options, Timeout);
    private static readonly Regex BearerPattern = new(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", Options, Timeout);
    private static readonly Regex JwtPattern = new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex QuerySecretPattern = new(@"([?&](?:token|access_token|refresh_token|key|api_key|apikey|sig|signature|auth|secret|password)=)[^&\s""']+", Options, Timeout);
    private static readonly Regex ApiKeyPattern = new(@"\b(?:re|sk|pk|ghp|gho|ghu|ghs)_[A-Za-z0-9_-]{12,}\b", RegexOptions.CultureInvariant, Timeout);

    /// <summary>Scrubs local paths, emails, tokens and query secrets from one line.</summary>
    public static string ScrubLine(string? text, IEnumerable<string>? files = null)
    {
        var cleaned = LocalPaths.Clean(text ?? "", "[path redacted]", files);
        return ScrubSecrets(cleaned).Replace('\r', ' ').Replace('\n', ' ');
    }

    internal static string ScrubSecrets(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        try
        {
            text = BearerPattern.Replace(text, "Bearer [redacted]");
            text = JwtPattern.Replace(text, "[jwt]");
            text = QuerySecretPattern.Replace(text, "$1[redacted]");
            text = ApiKeyPattern.Replace(text, "[key]");
            text = EmailPattern.Replace(text, "[email]");
            return text;
        }
        catch (RegexMatchTimeoutException)
        {
            return "[redacted]";
        }
    }

    /// <summary>Builds a scrubbed diagnostic upload for one queue item.</summary>
    public static DiagnosticUpload Build(QueueItem item, string release, string? reference = null, IReadOnlyList<LogLine>? lines = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var files = LocalPaths.Of(item.Video).ToList();
        var logLines = lines ?? item.Log;
        var current = item.CurrentStep;
        var header = new StringBuilder();
        header.AppendLine("# Al Ummah Now Tools Diagnostic Log");
        header.Append("app=tools");
        if (!string.IsNullOrWhiteSpace(release)) header.Append(" release=").Append(ScrubSecrets(release.Trim()));
        header.Append(" os=").Append(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "linux");
        header.Append(" arch=").Append(RuntimeInformation.ProcessArchitecture);
        header.Append(" phase=").Append(item.Phase);
        header.Append(" encodeHere=").Append(item.ServerEncodes ? "false" : "true");
        if (item.Video.Size > 0) header.Append(" fileSize=").Append(item.Video.Size.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(item.ServerItemId)) header.Append(" itemId=").Append(item.ServerItemId);
        if (!string.IsNullOrWhiteSpace(item.VideoId)) header.Append(" videoId=").Append(item.VideoId);
        if (current is not null)
        {
            header.Append(" step=").Append(current.Kind);
            header.Append(" attempts=").Append(current.Attempts.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(current.LastErrorCode)) header.Append(" errorCode=").Append(current.LastErrorCode);
        }
        header.AppendLine();
        header.AppendLine("---");

        var headerText = header.ToString();
        var headerBytes = Encoding.UTF8.GetByteCount(headerText);
        var formatted = new List<string>(logLines.Count);
        foreach (var entry in logLines)
        {
            var code = string.IsNullOrWhiteSpace(entry.Code) ? "-" : entry.Code;
            var body = ScrubLine(entry.Text, files);
            formatted.Add($"{entry.At.ToUniversalTime():O} [{entry.Level.ToString().ToUpperInvariant()}] {code} {body}");
        }

        // Keep the newest entries if the log ever exceeds MaxBytes
        var kept = new List<string>(formatted.Count);
        var budget = Math.Max(0, MaxBytes - headerBytes - 64);
        var used = 0;
        for (var i = formatted.Count - 1; i >= 0; i--)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(formatted[i]) + 1;
            if (used + lineBytes > budget) break;
            used += lineBytes;
            kept.Add(formatted[i]);
        }
        kept.Reverse();

        var sb = new StringBuilder(headerBytes + used + 64);
        sb.Append(headerText);
        if (kept.Count < formatted.Count)
        {
            sb.AppendLine($"# truncated: kept newest {kept.Count} of {formatted.Count} entries");
        }
        foreach (var line in kept) sb.AppendLine(line);

        return new DiagnosticUpload(
            Ref: string.IsNullOrWhiteSpace(reference) ? null : reference,
            Release: string.IsNullOrWhiteSpace(release) ? null : release,
            EntryCount: kept.Count,
            Content: sb.ToString());
    }
}
