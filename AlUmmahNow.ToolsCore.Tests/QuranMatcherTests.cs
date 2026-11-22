using System.Text.Json;
using AlUmmahNow.ToolsCore.Transcription;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>
/// A tiny hand-typed Quran (Imla'i script, words only): Al-Fatihah, 112–114, Ya-Sin 1–5 and Ar-Rahman 1–18
/// (55:13 = 55:16 = 55:18, the ambiguity case). The same fixture as AlUmmahNow
/// apps/processor/src/__tests__/fixtures/quran-mini.ts. Never fetched from Quran Foundation, and never used
/// for anything but these matcher tests (QF terms: no ML datasets or eval sets).
/// </summary>
internal static class MiniQuran
{
    public static readonly SortedDictionary<int, string[]> Surahs = new()
    {
        [1] =
        [
            "بسم الله الرحمن الرحيم",
            "الحمد لله رب العالمين",
            "الرحمن الرحيم",
            "مالك يوم الدين",
            "إياك نعبد وإياك نستعين",
            "اهدنا الصراط المستقيم",
            "صراط الذين أنعمت عليهم غير المغضوب عليهم ولا الضالين",
        ],
        [36] = ["يس", "والقرآن الحكيم", "إنك لمن المرسلين", "على صراط مستقيم", "تنزيل العزيز الرحيم"],
        [55] =
        [
            "الرحمن",
            "علم القرآن",
            "خلق الإنسان",
            "علمه البيان",
            "الشمس والقمر بحسبان",
            "والنجم والشجر يسجدان",
            "والسماء رفعها ووضع الميزان",
            "ألا تطغوا في الميزان",
            "وأقيموا الوزن بالقسط ولا تخسروا الميزان",
            "والأرض وضعها للأنام",
            "فيها فاكهة والنخل ذات الأكمام",
            "والحب ذو العصف والريحان",
            "فبأي آلاء ربكما تكذبان",
            "خلق الإنسان من صلصال كالفخار",
            "وخلق الجان من مارج من نار",
            "فبأي آلاء ربكما تكذبان",
            "رب المشرقين ورب المغربين",
            "فبأي آلاء ربكما تكذبان",
        ],
        [112] = ["قل هو الله أحد", "الله الصمد", "لم يلد ولم يولد", "ولم يكن له كفوا أحد"],
        [113] = ["قل أعوذ برب الفلق", "من شر ما خلق", "ومن شر غاسق إذا وقب", "ومن شر النفاثات في العقد", "ومن شر حاسد إذا حسد"],
        [114] = ["قل أعوذ برب الناس", "ملك الناس", "إله الناس", "من شر الوسواس الخناس", "الذي يوسوس في صدور الناس", "من الجنة والناس"],
    };

    /// <summary>(location, Imla'i word) rows in Quran order, as the Content Sync job produces them.</summary>
    public static IEnumerable<(QuranLocation, string)> Rows()
    {
        foreach (var (surah, ayat) in Surahs)
            for (var a = 0; a < ayat.Length; a++)
            {
                var words = ayat[a].Split(' ');
                for (var w = 0; w < words.Length; w++) yield return (new QuranLocation(surah, a + 1, w + 1), words[w]);
            }
    }

    public static QuranIndexFile File() => QuranIndex.Build(Rows(), "test-v1", "2026-11-13T00:00:00.000Z");

    /// <summary>ASR-style words, 400 ms each, from a space-separated string.</summary>
    public static List<AsrWord> Asr(string text, long startMs = 0) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select((t, i) => new AsrWord(t, startMs + i * 400, startMs + i * 400 + 380))
            .ToList();
}

/// <summary>The C# copy of the zero-mistake matcher, on the same cases as the server's quran-matcher.test.ts.</summary>
public class QuranMatcherTests
{
    static readonly QuranIndex Index = new(MiniQuran.File());

    static List<(string, QuranMatchClass)> Refs(string text) =>
        QuranMatcher.Match(Index, MiniQuran.Asr(text)).Select(m => (m.Range.ToString(), m.Class)).ToList();

    [Theory]
    [InlineData("ٱلرَّحْمَٰنِ", "الرحمن")]
    [InlineData("إِيَّاكَ", "اياك")]
    [InlineData("الصَّلَوٰةَ", "الصلوه")]
    [InlineData("مُؤْمِنٌ", "ممن")]
    [InlineData("عَلَىٰ", "علي")]
    [InlineData("hello ۝", "")]
    public void Normalises_the_way_the_index_does(string word, string expected) =>
        Assert.Equal(expected, QuranMatcher.Normalise(word));

    [Fact]
    public void Parses_and_formats_ranges_and_refuses_reversed_or_cross_surah_ones()
    {
        Assert.Equal("67:1:1-67:5:12", QuranRange.Parse("67:1:1-67:5:12")!.Value.ToString());
        Assert.Equal("67:1:1-67:5:12", QuranRange.Parse(" 67:1:1-67:5:12 ")!.Value.ToString());
        Assert.Null(QuranRange.Parse("67:5:1-67:1:1"));
        Assert.Null(QuranRange.Parse("67:1:1-68:1:1"));
        Assert.Null(QuranRange.Parse("115:1:1-115:1:2"));
        Assert.Null(QuranRange.Parse("2:287:1-2:287:2"));
        Assert.Null(QuranRange.Parse("<b>"));
    }

    [Fact]
    public void Word_similarity_follows_difflib_ratio()
    {
        Assert.Equal(1, QuranMatcher.WordSimilarity("الرحمن", "الرحمن"));
        Assert.Equal(0.75, QuranMatcher.WordSimilarity("abcd", "bcde"), 6);
        Assert.Equal(0, QuranMatcher.WordSimilarity("ab", "cd"));
    }

    [Fact]
    public void The_served_index_parses_and_must_be_whole()
    {
        var json = JsonSerializer.Serialize(MiniQuran.File());
        var again = new QuranIndex(JsonSerializer.Deserialize<QuranIndexFile>(json)!);
        Assert.Equal(Index.Words.Count, again.Words.Count);
        Assert.Equal(new QuranLocation(1, 1, 1), again.Locs[0]);
        var file = MiniQuran.File();
        Assert.Throws<InvalidDataException>(() => new QuranIndex(file with { Locs = file.Locs[1..] }));
        Assert.Throws<InvalidDataException>(() => new QuranIndex(file with { V = 2 }));
    }

    [Fact]
    public void A_whole_surah_with_one_misheard_word_is_confirmed_with_its_times()
    {
        var words = MiniQuran.Asr("قال الشيخ قل هو الله احد الله الصبر لم يلد ولم يولد ولم يكن له كفوا احد ثم قال", 1000);
        var all = QuranMatcher.Match(Index, words);
        var m = Assert.Single(all);
        Assert.Equal("112:1:1-112:4:5", m.Range.ToString());
        Assert.Equal(QuranMatchClass.Confirmed, m.Class);
        Assert.Equal(1, m.Occurrences);
        Assert.Equal(2, m.AsrStart);
        Assert.Equal(words[2].StartMs, m.StartMs);
        Assert.Equal(words[m.AsrEnd - 1].EndMs, m.EndMs);
        // «الصبر» was skipped: not aligned, so not Quran
        Assert.DoesNotContain(7, m.MatchedWords);
        Assert.Equal(m.MatchedWords, m.ExactWords);
        Assert.Equal(14, m.MatchedWords.Count);
    }

    [Fact]
    public void Near_matches_align_but_are_not_exact_and_spoken_letters_map_to_every_input_word()
    {
        var m = QuranMatcher.Match(Index, MiniQuran.Asr("قل هو الله احد الله الصمت لم يلد ولم يولد"))[0];
        Assert.Contains(5, m.MatchedWords);
        Assert.DoesNotContain(5, m.ExactWords);
        var y = QuranMatcher.Match(Index, MiniQuran.Asr("يا سين والقران الحكيم انك لمن المرسلين"))[0];
        Assert.Equal([0, 1, 2], y.ExactWords.Take(3));
    }

    [Fact]
    public void Spoken_letters_fold_to_the_written_muqattaat() =>
        Assert.Equal([("36:1:1-36:3:3", QuranMatchClass.Confirmed)], Refs("يا سين والقران الحكيم انك لمن المرسلين"));

    [Fact]
    public void A_repeated_ayah_alone_is_never_placed()
    {
        var m = QuranMatcher.Match(Index, MiniQuran.Asr("فباي الاء ربكما تكذبان"))[0];
        Assert.Equal(QuranMatchClass.Review, m.Class);
        Assert.Equal(3, m.Occurrences);
        Assert.Equal(["55:13:1-55:13:4", "55:16:1-55:16:4", "55:18:1-55:18:4"], m.Alternatives.Select(a => a.ToString()));
    }

    [Fact]
    public void A_repeated_ayah_right_after_a_confirmed_span_is_placed_by_verse_order() =>
        Assert.Equal(
            [("55:11:1-55:12:4", QuranMatchClass.Confirmed), ("55:13:1-55:13:4", QuranMatchClass.Context)],
            Refs("فيها فاكهة والنخل ذات الاكمام والحب ذو العصف والريحان قال الشيخ فباي الاء ربكما تكذبان"));

    [Fact]
    public void The_basmala_opens_the_surah_recited_after_it()
    {
        var ms = QuranMatcher.Match(Index, MiniQuran.Asr("بسم الله الرحمن الرحيم قل هو الله احد الله الصمد لم يلد ولم يولد"));
        Assert.Equal([QuranMatchClass.Basmala, QuranMatchClass.Confirmed], ms.Select(m => m.Class));
        Assert.Equal(112, ms[0].BasmalaFor);
    }

    [Fact]
    public void A_basmala_with_no_surah_after_it_is_only_a_suggestion() =>
        Assert.Equal([("1:1:1-1:1:4", QuranMatchClass.Review)], Refs("بسم الله الرحمن الرحيم نبدأ الدرس اليوم"));

    [Fact]
    public void Al_Fatihah_recited_in_full_is_one_confirmed_span() =>
        Assert.Equal([("1:1:1-1:4:3", QuranMatchClass.Confirmed)], Refs("بسم الله الرحمن الرحيم الحمد لله رب العالمين الرحمن الرحيم مالك يوم الدين"));

    [Fact]
    public void Ordinary_speech_matches_nothing_and_short_fragments_are_dropped()
    {
        Assert.Empty(Refs("السلام عليكم ورحمة الله اليوم نتكلم عن الصبر"));
        Assert.Empty(Refs("الله الصمد"));
    }

    [Fact]
    public void A_short_unique_span_is_a_suggestion_never_confirmed() =>
        Assert.Equal([("114:4:1-114:4:4", QuranMatchClass.Review)], Refs("هذا كلام من شر الوسواس الخناس ثم نكمل"));
}
