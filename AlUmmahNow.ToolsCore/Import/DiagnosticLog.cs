using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>
/// Builds a scrubbed, bounded diagnostic log for an import queue item to upload to
/// <c>POST /api/admin/import/diagnostics</c> (stored in R2 with a 30-day expiry).
/// Local folder paths (absolute and relative), drive letters, UNC share names, emails,
/// bearer/API-key headers and query secrets are stripped before anything leaves this computer.
/// </summary>
public static class DiagnosticLog
{
    /// <summary>Maximum UTF-8 payload size sent to admin-api (well under the 512 KB server cap).</summary>
    public const int MaxBytes = 256 * 1024;
    private const int MaxHeaderBytes = 4096;

    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly Regex WebAddressPattern = new(@"\bhttps?://[^\s\\]+", Options, Timeout);
    private static readonly Regex RelativePathPattern = new(@"(?:^|(?<=[\s""'(:=]))(?:\.{1,2}/[^\s""')]+|[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)+)", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex EmailPattern = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", Options, Timeout);
    private static readonly Regex BearerPattern = new(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", Options, Timeout);
    private static readonly Regex HeaderSecretPattern = new(@"\b((?:x-api-key|x-internal-api-key|authorization)\s*[:=]\s*)[^\s,;]+", Options, Timeout);
    private static readonly Regex JwtPattern = new(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex QuerySecretPattern = new(@"([?&](?:token|access_token|refresh_token|key|api_key|apikey|sig|signature|auth|secret|password|x-amz-signature|x-amz-credential|x-amz-security-token)=)[^&\s""']+", Options, Timeout);
    private static readonly Regex ApiKeyPattern = new(@"\b(?:re|sk|pk|ghp|gho|ghu|ghs)_[A-Za-z0-9_-]{12,}\b", RegexOptions.CultureInvariant, Timeout);

    /// <summary>Scrubs local paths (absolute and relative), emails, tokens and query secrets from one line.</summary>
    public static string ScrubLine(string? text, IEnumerable<string>? files = null)
    {
        var cleaned = LocalPaths.Clean(text ?? "", "[path redacted]", files);
        var noRelPaths = ScrubRelativePaths(cleaned);
        return ScrubSecrets(noRelPaths).Replace('\r', ' ').Replace('\n', ' ');
    }

    internal static string ScrubRelativePaths(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('/')) return text;
        try
        {
            // Keep http(s) URLs intact while stripping relative path segments outside URLs
            var sb = new StringBuilder(text.Length);
            var pos = 0;
            foreach (Match m in WebAddressPattern.Matches(text))
            {
                if (m.Index > pos) sb.Append(RelativePathPattern.Replace(text[pos..m.Index], "[path]"));
                sb.Append(m.Value);
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) sb.Append(RelativePathPattern.Replace(text[pos..], "[path]"));
            return sb.ToString();
        }
        catch (RegexMatchTimeoutException)
        {
            return "[path redacted]";
        }
    }

    internal static string ScrubSecrets(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        try
        {
            text = BearerPattern.Replace(text, "Bearer [redacted]");
            text = HeaderSecretPattern.Replace(text, "$1[redacted]");
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

    private static string TruncateUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
        var bytes = Encoding.UTF8.GetBytes(value);
        var len = maxBytes;
        // Back up past UTF-8 continuation bytes so we never split a multi-byte character
        while (len > 0 && (bytes[len - 1] & 0xC0) == 0x80) len--;
        if (len > 0 && (bytes[len - 1] & 0x80) != 0) len--;
        return Encoding.UTF8.GetString(bytes, 0, Math.Max(0, len));
    }

    /// <summary>Builds a scrubbed diagnostic upload for one queue item (always LF-delimited and strictly &lt;= MaxBytes).</summary>
    public static DiagnosticUpload Build(QueueItem item, string release, string? reference = null, IReadOnlyList<LogLine>? lines = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var files = LocalPaths.Of(item.Video).ToList();
        var logLines = lines ?? item.Log.ToList();
        var current = item.CurrentStep;
        var safeRelease = string.IsNullOrWhiteSpace(release) ? "" : TruncateUtf8(ScrubLine(release.Trim()), 80);

        var header = new StringBuilder();
        header.Append("# Al Ummah Now Tools Diagnostic Log\n");
        header.Append("app=tools");
        if (safeRelease.Length > 0) header.Append(" release=").Append(safeRelease);
        header.Append(" os=").Append(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" : "linux");
        header.Append(" arch=").Append(RuntimeInformation.ProcessArchitecture);
        header.Append(" phase=").Append(item.Phase);
        header.Append(" encodeHere=").Append(item.ServerEncodes ? "false" : "true");
        if (item.Video.Size > 0) header.Append(" fileSize=").Append(item.Video.Size.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(item.ServerItemId)) header.Append(" itemId=").Append(TruncateUtf8(ScrubLine(item.ServerItemId), 128));
        if (!string.IsNullOrWhiteSpace(item.VideoId)) header.Append(" videoId=").Append(TruncateUtf8(ScrubLine(item.VideoId), 128));
        if (current is not null)
        {
            header.Append(" step=").Append(current.Kind);
            header.Append(" attempts=").Append(current.Attempts.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(current.LastErrorCode)) header.Append(" errorCode=").Append(TruncateUtf8(ScrubLine(current.LastErrorCode), 64));
        }
        header.Append("\n---\n");

        var headerText = header.ToString();
        if (Encoding.UTF8.GetByteCount(headerText) > MaxHeaderBytes)
        {
            headerText = TruncateUtf8(headerText, MaxHeaderBytes - 5) + "\n---\n";
        }
        var headerBytes = Encoding.UTF8.GetByteCount(headerText);

        var formatted = new List<string>(logLines.Count);
        foreach (var entry in logLines)
        {
            var code = string.IsNullOrWhiteSpace(entry.Code) ? "-" : entry.Code;
            var body = ScrubLine(entry.Text, files);
            formatted.Add($"{entry.At.ToUniversalTime():O} [{entry.Level.ToString().ToUpperInvariant()}] {code} {body}");
        }

        // Reserve space for the truncation banner when needed and use strict 1-byte '\n' endings on every OS
        const int bannerReserve = 80;
        var kept = new List<string>(formatted.Count);
        var budget = Math.Max(0, MaxBytes - headerBytes - bannerReserve);
        var used = 0;
        for (var i = formatted.Count - 1; i >= 0; i--)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(formatted[i]) + 1; // +1 for '\n'
            if (used + lineBytes > budget) break;
            used += lineBytes;
            kept.Add(formatted[i]);
        }
        kept.Reverse();

        var sb = new StringBuilder(headerBytes + used + bannerReserve);
        sb.Append(headerText);
        if (kept.Count < formatted.Count)
        {
            sb.Append($"# truncated: kept newest {kept.Count} of {formatted.Count} entries\n");
        }
        foreach (var line in kept)
        {
            sb.Append(line).Append('\n');
        }

        var content = sb.ToString();
        if (Encoding.UTF8.GetByteCount(content) > MaxBytes) content = TruncateUtf8(content, MaxBytes);

        return new DiagnosticUpload(
            Ref: string.IsNullOrWhiteSpace(reference) ? null : reference,
            Release: safeRelease.Length > 0 ? safeRelease : null,
            EntryCount: kept.Count,
            Content: content);
    }
}
