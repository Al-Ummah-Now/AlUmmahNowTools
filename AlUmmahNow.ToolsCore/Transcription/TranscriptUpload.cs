using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlUmmahNow.ToolsCore.Transcription;

/// <summary>aun-core's transcript (<c>aun_media::transcribe::Transcript</c>, snake_case JSON).</summary>
public sealed record CoreTranscript(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("segments")] IReadOnlyList<CoreTranscriptSegment> Segments)
{
    public static CoreTranscript Parse(string json) =>
        JsonSerializer.Deserialize<CoreTranscript>(json) ?? throw new InvalidDataException("The transcript is empty");
}

public sealed record CoreTranscriptSegment(
    [property: JsonPropertyName("start_ms")] long StartMs,
    [property: JsonPropertyName("end_ms")] long EndMs,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("words")] IReadOnlyList<CoreTranscriptWord>? Words);

public sealed record CoreTranscriptWord(
    [property: JsonPropertyName("start_ms")] long StartMs,
    [property: JsonPropertyName("end_ms")] long EndMs,
    [property: JsonPropertyName("text")] string Text);

/// <summary>A Quran span matched here: a reference only, never the words (QF terms 2026-11-21).</summary>
public sealed record TranscriptQuranRef(
    [property: JsonPropertyName("range")] string Range,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("alternatives"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Alternatives);

/// <summary>
/// A segment of the transcript upload (admin-api POST /items/:id/transcript, api-videos validateOnDeviceTranscript):
/// speech with its text, or a Quran span with its reference and no text at all.
/// </summary>
public sealed record UploadSegment(
    [property: JsonPropertyName("startMs")] long StartMs,
    [property: JsonPropertyName("endMs")] long EndMs,
    [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
    [property: JsonPropertyName("quran"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TranscriptQuranRef? Quran);

/// <summary>What the step keeps on disk and sends: never a Quran word, only references.</summary>
public sealed record TranscriptUpload(string Language, IReadOnlyList<UploadSegment> Segments, int QuranSpans)
{
    /// <summary>The server's limits (shared-types ON_DEVICE_TRANSCRIPT_LIMITS)</summary>
    public const int MaxSegments = 20_000;
    public const int MaxSegmentChars = 2_000;
    public const int MaxTotalChars = 2_000_000;
    public const long MaxEndMs = 24L * 60 * 60 * 1000;
    /// <summary>shared-types CAPTION_LIMITS.alternatives</summary>
    public const int MaxAlternatives = 10;

    /// <summary>
    /// The upload of a local transcript: every span the matcher finds (any class, suggestions included) becomes a
    /// reference segment in place of its words, so no Quran text is saved or sent; the server's claim job turns the
    /// references into the draft's Quran cues. The speech around a span keeps its segment, cut at the span.
    /// </summary>
    public static TranscriptUpload From(CoreTranscript transcript, QuranIndex index)
    {
        // Every word with its segment; a segment without word timings is spread evenly, as the server does
        var words = new List<(AsrWord Word, int Seg)>();
        for (var s = 0; s < transcript.Segments.Count; s++)
        {
            var seg = transcript.Segments[s];
            var timed = (seg.Words ?? []).Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            if (timed.Count > 0)
            {
                foreach (var w in timed) words.Add((new AsrWord(w.Text.Trim(), w.StartMs, w.EndMs), s));
                continue;
            }
            var parts = (seg.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var step = parts.Length > 0 ? Math.Max(0, seg.EndMs - seg.StartMs) / (double)parts.Length : 0;
            for (var i = 0; i < parts.Length; i++)
                words.Add((new AsrWord(parts[i], (long)Math.Round(seg.StartMs + i * step), (long)Math.Round(seg.StartMs + (i + 1) * step)), s));
        }

        var matches = QuranMatcher.Match(index, words.Select(w => w.Word).ToList()).OrderBy(m => m.AsrStart).ToList();
        var segments = new List<UploadSegment>();
        var i0 = 0;
        var m = 0;
        while (i0 < words.Count)
        {
            while (m < matches.Count && matches[m].AsrStart < i0) m++;
            if (m < matches.Count && matches[m].AsrStart == i0 && matches[m].AsrEnd > i0)
            {
                var span = matches[m];
                var end = Math.Min(span.AsrEnd, words.Count);
                var own = span.Range.ToString();
                var alternatives = span.Alternatives.Select(a => a.ToString()).Where(a => a != own).Take(MaxAlternatives).ToList();
                segments.Add(new UploadSegment(words[i0].Word.StartMs, words[end - 1].Word.EndMs, null,
                    new TranscriptQuranRef(own, StatusOf(span.Class), alternatives.Count > 0 ? alternatives : null)));
                i0 = end;
                m++;
                continue;
            }
            // Speech up to the next span or the end of this segment, in pieces within the server's segment limit
            var stopAt = m < matches.Count ? Math.Min(matches[m].AsrStart, words.Count) : words.Count;
            var j = i0;
            var text = new StringBuilder();
            while (j < stopAt && words[j].Seg == words[i0].Seg && text.Length + 1 + words[j].Word.Text.Length <= MaxSegmentChars)
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(words[j].Word.Text);
                j++;
            }
            if (j == i0)
            {
                // One word longer than a segment: cut, never dropped silently into the next span
                text.Append(words[i0].Word.Text.AsSpan(0, Math.Min(words[i0].Word.Text.Length, MaxSegmentChars)));
                j = i0 + 1;
            }
            segments.Add(new UploadSegment(words[i0].Word.StartMs, words[j - 1].Word.EndMs, text.ToString(), null));
            i0 = j;
        }
        return new TranscriptUpload(transcript.Language, Tidy(segments), segments.Count(s => s.Quran is not null));
    }

    static string StatusOf(QuranMatchClass c) => c switch
    {
        QuranMatchClass.Confirmed => "confirmed",
        QuranMatchClass.Context => "context",
        QuranMatchClass.Basmala => "basmala",
        _ => "review",
    };

    /// <summary>
    /// The server's rules: whole ms, start never before the previous start, end after start, within 24 hours, text
    /// collapsed and at most 2,000 characters, at most 20,000 segments and 2,000,000 characters.
    /// </summary>
    static IReadOnlyList<UploadSegment> Tidy(List<UploadSegment> segments)
    {
        var output = new List<UploadSegment>(segments.Count);
        long previousStart = 0;
        long total = 0;
        foreach (var s in segments)
        {
            var text = s.Text is null ? null : string.Join(' ', s.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (s.Quran is null && string.IsNullOrEmpty(text)) continue;
            var start = Math.Clamp(Math.Max(s.StartMs, previousStart), 0, MaxEndMs - 1);
            var end = Math.Clamp(Math.Max(s.EndMs, start + 1), start + 1, MaxEndMs);
            if (text is not null)
            {
                if (total + text.Length > MaxTotalChars) break;
                total += text.Length;
            }
            output.Add(s with { StartMs = start, EndMs = end, Text = text });
            previousStart = start;
            if (output.Count == MaxSegments) break;
        }
        return output;
    }
}
