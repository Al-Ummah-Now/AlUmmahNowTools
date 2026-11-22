using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AlUmmahNow.ToolsCore.Transcription;

// A line-for-line port of AlUmmahNow packages/shared-types/src/quran.ts (normaliseQuranWord,
// QuranIndex, wordSimilarity, matchQuran): the "zero-mistake" matcher (AUN-CAPTIONS spec §5.5),
// measured at 0 wrong ayat and 0 extra words. Both copies run the same fixture tests
// (QuranMatcherTests.cs mirrors apps/processor/src/__tests__/quran-matcher.test.ts); change them together.
//
// TA-13, Quran Foundation terms (2026-11-21): the app turns the Quran spans of a local transcript into
// verse-key references with this BEFORE anything is saved to disk or uploaded. A match is a reference
// (surah:ayah:word range), never text. The index comes from admin-api (GET /api/admin/import/quran-index),
// is kept in memory only and dropped at its expiresAt.
//
// NOT FOR MACHINE LEARNING (QF Developer Terms, Must NOT item 4): the index is QF Derived Material for
// runtime matching only. It must never be used to train, fine-tune or evaluate an ML/AI model (ASR
// included): no datasets, no eval sets, no ground truth. Tests use the hand-typed fixture only.

/// <summary>One Quran word: surah, ayah and the word's QF position in the ayah (1-based, words only).</summary>
public readonly record struct QuranLocation(int Surah, int Ayah, int Word)
{
    public int Pack() => Surah * 1_000_000 + Ayah * 1_000 + Word;
    public static QuranLocation Unpack(long n) => new((int)(n / 1_000_000), (int)(n % 1_000_000 / 1_000), (int)(n % 1_000));
    public static int Compare(QuranLocation a, QuranLocation b) =>
        a.Surah != b.Surah ? a.Surah - b.Surah : a.Ayah != b.Ayah ? a.Ayah - b.Ayah : a.Word - b.Word;
}

/// <summary>An inclusive word range, possibly across ayat of one surah.</summary>
public readonly record struct QuranRange(QuranLocation From, QuranLocation To)
{
    /// <summary>"67:1:1-67:5:12", the marker format of published Quran cues and of the transcript upload.</summary>
    public override string ToString() => $"{From.Surah}:{From.Ayah}:{From.Word}-{To.Surah}:{To.Ayah}:{To.Word}";

    static readonly Regex Shape = new(@"^(\d{1,3}):(\d{1,3}):(\d{1,3})-(\d{1,3}):(\d{1,3}):(\d{1,3})$", RegexOptions.CultureInvariant);

    /// <summary>Parses "67:1:1-67:5:12"; null when malformed, reversed or across surahs.</summary>
    public static QuranRange? Parse(string? s)
    {
        var m = Shape.Match((s ?? "").Trim());
        if (!m.Success) return null;
        int G(int i) => int.Parse(m.Groups[i].Value, System.Globalization.CultureInfo.InvariantCulture);
        static bool Valid(QuranLocation l) => l.Surah is >= 1 and <= 114 && l.Ayah is >= 1 and <= 286 && l.Word >= 1;
        var from = new QuranLocation(G(1), G(2), G(3));
        var to = new QuranLocation(G(4), G(5), G(6));
        if (!Valid(from) || !Valid(to) || from.Surah != to.Surah || QuranLocation.Compare(from, to) > 0) return null;
        return new QuranRange(from, to);
    }
}

/// <summary>The index as admin-api serves it: normalised words space-joined, locations packed as surah·10⁶ + ayah·10³ + word.</summary>
public sealed record QuranIndexFile(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("syncedAt")] string SyncedAt,
    [property: JsonPropertyName("words")] string Words,
    [property: JsonPropertyName("locs")] long[] Locs);

/// <summary>One ASR word with its time (ms).</summary>
public readonly record struct AsrWord(string Text, long StartMs, long EndMs);

/// <summary>
/// confirmed: unique, (≥ 6 words and agreement ≥ 0.9) or (≥ 20 words and ≥ 0.75); context: an ambiguous
/// span resolved by verse order; basmala: the basmala just before ayah 1 of a confirmed surah; review:
/// possible, ambiguous or an unplaced basmala (a reference suggestion, never text).
/// </summary>
public enum QuranMatchClass { Confirmed, Context, Basmala, Review }

public sealed record QuranMatch(
    QuranRange Range,
    QuranMatchClass Class,
    double Confidence,
    int Occurrences,
    IReadOnlyList<QuranRange> Alternatives,
    int? BasmalaFor,
    IReadOnlyList<int> MatchedWords,
    IReadOnlyList<int> ExactWords,
    int AsrStart,
    int AsrEnd,
    long StartMs,
    long EndMs);

/// <summary>The in-memory index: words, locations and the 3-gram seed map. Runtime matching only.</summary>
public sealed class QuranIndex
{
    internal const int N = 3;
    public IReadOnlyList<string> Words => words;
    public IReadOnlyList<QuranLocation> Locs => locs;
    public string Version { get; }
    public string SyncedAt { get; }
    internal readonly string[] words;
    internal readonly QuranLocation[] locs;
    readonly Dictionary<string, List<int>> seeds = new(StringComparer.Ordinal);

    public QuranIndex(QuranIndexFile file)
    {
        if (file.V != 1) throw new InvalidDataException($"Unsupported Quran index version {file.V}");
        words = string.IsNullOrEmpty(file.Words) ? [] : file.Words.Split(' ');
        if (words.Length != file.Locs.Length) throw new InvalidDataException("Quran index words and locations differ in length");
        locs = file.Locs.Select(QuranLocation.Unpack).ToArray();
        Version = file.Version;
        SyncedAt = file.SyncedAt;
        for (var i = 0; i + N <= words.Length; i++)
        {
            var key = Key(words, i);
            if (seeds.TryGetValue(key, out var list)) list.Add(i);
            else seeds[key] = [i];
        }
    }

    /// <summary>Builds the serialised index from (location, Imla'i word) rows in Quran order (tests; the server builds the real one).</summary>
    public static QuranIndexFile Build(IEnumerable<(QuranLocation Loc, string Imlaei)> rows, string version, string syncedAt)
    {
        var w = new List<string>();
        var l = new List<long>();
        foreach (var (loc, imlaei) in rows)
        {
            var n = QuranMatcher.Normalise(imlaei);
            if (n.Length == 0) continue;
            w.Add(n);
            l.Add(loc.Pack());
        }
        return new QuranIndexFile(1, version, syncedAt, string.Join(' ', w), l.ToArray());
    }

    static string Key(IReadOnlyList<string> seq, int i) => i + N <= seq.Count ? string.Join(' ', seq[i], seq[i + 1], seq[i + 2]) : "";

    internal IReadOnlyList<int> SeedsFor(IReadOnlyList<string> h, int i) =>
        seeds.TryGetValue(Key(h, i), out var list) ? list : [];

    /// <summary>Every index position where the exact word sequence occurs.</summary>
    internal List<int> Positions(IReadOnlyList<string> seq)
    {
        if (seq.Count < N || !seeds.TryGetValue(Key(seq, 0), out var list)) return [];
        return list.Where(q => Enumerable.Range(0, seq.Count).All(k => q + k < words.Length && words[q + k] == seq[k])).ToList();
    }
}

public static class QuranMatcher
{
    // Harakat, Quranic annotation marks (small high letters, pause marks), superscript alef, tatweel
    static readonly Regex Marks = new("[ؐ-ًؚ-ٰٟۖ-ۭـ]", RegexOptions.CultureInvariant);
    static readonly Regex Alefs = new("[إأآٱا]", RegexOptions.CultureInvariant);
    static readonly Regex Hamzas = new("[ؤئء]", RegexOptions.CultureInvariant);
    static readonly Regex NotLetter = new("[^ء-ي]", RegexOptions.CultureInvariant);

    /// <summary>Strip diacritics and tatweel; unify alef forms; ى → ي, ة → ه; drop ؤ ئ ء; Arabic letters only.</summary>
    public static string Normalise(string? word)
    {
        var s = Marks.Replace(word ?? "", "");
        s = Alefs.Replace(s, "ا");
        s = s.Replace('ى', 'ي').Replace('ة', 'ه');
        s = Hamzas.Replace(s, "");
        return NotLetter.Replace(s, "");
    }

    /// <summary>difflib.SequenceMatcher(None, a, b).ratio() for short words (no junk heuristics).</summary>
    public static double WordSimilarity(string a, string b)
    {
        if (a == b) return 1;
        var total = a.Length + b.Length;
        if (total == 0) return 1;
        int Matches(int alo, int ahi, int blo, int bhi)
        {
            int bestI = alo, bestJ = blo, bestK = 0;
            for (var i = alo; i < ahi; i++)
                for (var j = blo; j < bhi; j++)
                {
                    var k = 0;
                    while (i + k < ahi && j + k < bhi && a[i + k] == b[j + k]) k++;
                    if (k > bestK) { bestI = i; bestJ = j; bestK = k; }
                }
            if (bestK == 0) return 0;
            return bestK + Matches(alo, bestI, blo, bestJ) + Matches(bestI + bestK, ahi, bestJ + bestK, bhi);
        }
        return 2.0 * Matches(0, a.Length, 0, b.Length) / total;
    }

    const double Sim = 0.7;
    static bool Ws(string a, string b) => a == b || WordSimilarity(a, b) >= Sim;

    /// <summary>Spoken muqatta'at → the letters as written («يا سين» → يس). Longest first.</summary>
    static readonly (string[] Seq, string Rep)[] SpokenLetters = new (string[] Seq, string Rep)[]
    {
        (["الف", "لام", "ميم", "صاد"], "المص"),
        (["الف", "لام", "ميم", "را"], "المر"),
        (["كاف", "ها", "يا", "عين", "صاد"], "كهيعص"),
        (["الف", "لام", "ميم"], "الم"),
        (["الف", "لام", "را"], "الر"),
        (["طا", "سين", "ميم"], "طسم"),
        (["عين", "سين", "قاف"], "عسق"),
        (["يا", "سين"], "يس"),
        (["حا", "ميم"], "حم"),
        (["طا", "ها"], "طه"),
        (["طا", "سين"], "طس"),
    }
    .Select(x => (x.Seq.Select(Normalise).ToArray(), Normalise(x.Rep)))
    // Array.prototype.sort is stable, and so is OrderBy
    .OrderByDescending(x => x.Item1.Length)
    .ToArray();

    static readonly string[] Basmala = new[] { "بسم", "الله", "الرحمن", "الرحيم" }.Select(Normalise).ToArray();

    sealed class Raw
    {
        public int Hs, He, Qs, Qe, M, L, Occ;
        public List<int> Cands = [];
        public List<int> Hits = [];
        public List<bool> Exact = [];
    }

    enum Cls { Basmala, Ambiguous, Confirmed, Possible, Drop, Context, BasmalaOk }

    readonly record struct Mark(int He, int Qe, int M);
    readonly record struct Folded(string W, int A, int B, long StartMs, long EndMs);

    /// <summary>
    /// Greedy alignment from (i, q): fuzzy word match, one-word skips, stop after 3 misses in a row or at the
    /// end of the surah. A checkpoint at every matched word lets the caller keep the longest prefix that
    /// still clears the agreement bar.
    /// </summary>
    static (List<Mark> Marks, List<int> Hits, List<bool> Exact) Extend(string[] h, int i, int q, QuranIndex index)
    {
        var w = index.words;
        var surah = index.locs[q].Surah;
        bool InSurah(int k) => k < w.Length && index.locs[k].Surah == surah;
        int hi = i, qi = q, m = 0, miss = 0;
        var marks = new List<Mark>();
        var hits = new List<int>();
        var exact = new List<bool>();
        while (hi < h.Length && InSurah(qi))
        {
            if (Ws(h[hi], w[qi]))
            {
                m++;
                hits.Add(hi);
                exact.Add(h[hi] == w[qi]);
                hi++;
                qi++;
                marks.Add(new Mark(hi, qi, m));
                miss = 0;
                continue;
            }
            if (hi + 1 < h.Length && Ws(h[hi + 1], w[qi])) hi++;
            else if (InSurah(qi + 1) && Ws(h[hi], w[qi + 1])) qi++;
            else { hi++; qi++; }
            if (++miss > 2) break;
        }
        return (marks, hits, exact);
    }

    static Cls ClassOf(QuranIndex index, Raw s)
    {
        var nq = s.Qe - s.Qs;
        var conf = (double)s.M / s.L;
        if (nq == Basmala.Length && Enumerable.Range(0, nq).All(k => index.words[s.Qs + k] == Basmala[k])) return Cls.Basmala;
        if (s.Occ > 1) return Cls.Ambiguous;
        if ((nq >= 6 && conf >= 0.9) || (nq >= 20 && conf >= 0.75)) return Cls.Confirmed;
        return nq >= 4 ? Cls.Possible : Cls.Drop;
    }

    // JavaScript's Math.round: halves go up (C#'s Math.Round would round them to even)
    static double Round2(double x) => Math.Floor(x * 100 + 0.5) / 100;

    /// <summary>
    /// Finds Quran spans in a stream of ASR words. Every returned span is a reference into the index; nothing
    /// here produces Quran text.
    /// </summary>
    public static IReadOnlyList<QuranMatch> Match(QuranIndex index, IReadOnlyList<AsrWord> input)
    {
        // Normalise, then fold spoken letters; keep each folded word's original index range and times
        var norm = new List<Folded>();
        for (var i = 0; i < input.Count; i++)
        {
            var w = Normalise(input[i].Text);
            if (w.Length > 0) norm.Add(new Folded(w, i, i + 1, input[i].StartMs, input[i].EndMs));
        }
        var words = new List<Folded>();
        for (var i = 0; i < norm.Count;)
        {
            var at = i;
            var hit = SpokenLetters.FirstOrDefault(x => x.Seq.Length > 1 && x.Seq.Select((w, k) => (w, k)).All(p => at + p.k < norm.Count && norm[at + p.k].W == p.w));
            if (hit.Seq is not null)
            {
                var last = norm[i + hit.Seq.Length - 1];
                words.Add(new Folded(hit.Rep, norm[i].A, last.B, norm[i].StartMs, last.EndMs));
                i += hit.Seq.Length;
            }
            else
            {
                words.Add(norm[i]);
                i++;
            }
        }
        var h = words.Select(x => x.W).ToArray();
        var W = index.words;
        IEnumerable<int> InputIndexes(int k) => Enumerable.Range(words[k].A, words[k].B - words[k].A);

        // Detection (§5.5 step 4): 3-gram seeds, extension to the right, best match wins
        var raw = new List<Raw>();
        for (var i = 0; i + QuranIndex.N <= h.Length;)
        {
            Raw? best = null;
            foreach (var q in index.SeedsFor(h, i).Take(300))
            {
                var (marks, hits, exact) = Extend(h, i, q, index);
                for (var k = marks.Count - 1; k >= 0; k--)
                {
                    var (he, qe, m) = marks[k];
                    var L = he - i;
                    if (L < 4) break;
                    if ((double)m / L < 0.75) continue;
                    if (best is null || m > best.M)
                        best = new Raw { Hs = i, He = he, Qs = q, Qe = qe, M = m, L = L, Hits = hits.Take(m).ToList(), Exact = exact.Take(m).ToList() };
                    break;
                }
            }
            if (best is not null)
            {
                raw.Add(best);
                i = best.He;
            }
            else i++;
        }

        // Extension to the left over garbled first words, then uniqueness (step 5)
        foreach (var s in raw)
        {
            while (s.Hs > 0 && s.Qs > 0 && index.locs[s.Qs - 1].Surah == index.locs[s.Qs].Surah && Ws(h[s.Hs - 1], W[s.Qs - 1])
                   && !raw.Any(o => !ReferenceEquals(o, s) && o.He > s.Hs - 1 && o.Hs <= s.Hs - 1))
            {
                s.Hs--;
                s.Qs--;
                s.Hits.Insert(0, s.Hs);
                s.Exact.Insert(0, h[s.Hs] == W[s.Qs]);
                s.M++;
                s.L++;
            }
            s.Cands = index.Positions(W[s.Qs..s.Qe]);
            s.Occ = s.Cands.Count == 0 ? 1 : s.Cands.Count;
        }

        // Classes, then verse-order context and the basmala (step 6)
        var cls = raw.Select(s => ClassOf(index, s)).ToArray();
        QuranLocation AyahOf(int q) => index.locs[q];
        for (var j = 0; j < raw.Count; j++)
        {
            if (cls[j] != Cls.Ambiguous) continue;
            var s = raw[j];
            QuranLocation? prev = null;
            for (var k = j - 1; k >= 0; k--)
                if (cls[k] is Cls.Confirmed or Cls.Context) { prev = AyahOf(raw[k].Qe - 1); break; }
            QuranLocation? next = null;
            for (var k = j + 1; k < raw.Count; k++)
                if (cls[k] == Cls.Confirmed) { next = AyahOf(raw[k].Qs); break; }
            var ok = s.Cands.Where(q =>
            {
                var at = AyahOf(q);
                if (prev is { } p && at.Surah == p.Surah && at.Ayah - p.Ayah >= 0 && at.Ayah - p.Ayah <= 2) return true;
                return next is { } n && at.Surah == n.Surah && n.Ayah - at.Ayah >= 0 && n.Ayah - at.Ayah <= 2;
            }).ToList();
            if (ok.Count == 1)
            {
                var L = s.Qe - s.Qs;
                s.Qs = ok[0];
                s.Qe = ok[0] + L;
                cls[j] = Cls.Context;
            }
        }
        var basmalaFor = new int?[raw.Count];
        for (var j = 0; j < raw.Count; j++)
        {
            if (cls[j] != Cls.Basmala) continue;
            Raw? nx = null;
            for (var k = j + 1; k < raw.Count; k++)
                if (cls[k] is Cls.Confirmed or Cls.Context) { nx = raw[k]; break; }
            QuranLocation? start = nx is null ? null : AyahOf(nx.Qs);
            // Al-Fatihah's basmala is its first ayah (a confirmed 1:1 span); elsewhere it opens ayah 1
            if (start is { } st && (st.Ayah == 1 || (st.Surah == 1 && st.Ayah == 2)) && st.Word == 1)
            {
                cls[j] = Cls.BasmalaOk;
                basmalaFor[j] = st.Surah;
            }
            else cls[j] = Cls.Possible;
        }

        var output = new List<QuranMatch>();
        for (var j = 0; j < raw.Count; j++)
        {
            var c = cls[j];
            if (c == Cls.Drop) continue;
            var s = raw[j];
            var klass = c switch
            {
                Cls.Confirmed => QuranMatchClass.Confirmed,
                Cls.Context => QuranMatchClass.Context,
                Cls.BasmalaOk => QuranMatchClass.Basmala,
                _ => QuranMatchClass.Review,
            };
            var first = words[s.Hs];
            var last = words[s.He - 1];
            output.Add(new QuranMatch(
                new QuranRange(AyahOf(s.Qs), AyahOf(s.Qe - 1)),
                klass,
                Round2((double)s.M / s.L),
                s.Occ,
                c == Cls.Ambiguous ? s.Cands.Take(10).Select(q => new QuranRange(AyahOf(q), AyahOf(q + (s.Qe - s.Qs) - 1))).ToList() : [],
                basmalaFor[j],
                s.Hits.SelectMany(InputIndexes).ToList(),
                s.Hits.Where((_, n) => s.Exact[n]).SelectMany(InputIndexes).ToList(),
                first.A,
                last.B,
                first.StartMs,
                last.EndMs));
        }
        return output;
    }
}
