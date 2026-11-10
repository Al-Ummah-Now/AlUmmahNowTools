using AlUmmahNow.Data.Search;
using FluentAssertions;
using Xunit;

namespace AlUmmahNow.Tests.Search;

/// <summary>The importer's search box suggestions (channel filters, a new watch's channel name).</summary>
public class SuggestionMatcherTests
{
    private static readonly string[] Channels =
    {
        "Islamic Lectures", "Lectures Archive", "Quran Recitation", "Friday Khutbah", "Seerah lectures", "أحمد الشيخ", "مدرسة النور",
    };

    [Fact]
    public void Ranks_exact_then_starts_with_then_word_start_then_contains()
    {
        SuggestionMatcher.Suggest(Channels, "lectures").Should().Equal("Lectures Archive", "Islamic Lectures", "Seerah lectures");
        SuggestionMatcher.Suggest(new[] { "ab", "xab", "x ab", "ab x" }, "ab").Should().Equal("ab", "ab x", "x ab", "xab");
    }

    [Fact]
    public void Ignores_case_and_extra_spaces()
    {
        SuggestionMatcher.Suggest(Channels, "  QURAN   rec ").Should().Equal("Quran Recitation");
    }

    [Fact]
    public void Matches_arabic_without_diacritics_and_with_folded_letters()
    {
        SuggestionMatcher.Suggest(Channels, "احمد").Should().Equal("أحمد الشيخ");
        SuggestionMatcher.Suggest(Channels, "أَحْمَد").Should().Equal("أحمد الشيخ");
        SuggestionMatcher.Suggest(Channels, "مدرسه").Should().Equal("مدرسة النور");
        SuggestionMatcher.Normalize("مـــحـــمـــد").Should().Be("محمد");
    }

    [Fact]
    public void Empty_query_lists_the_first_candidates_and_skips_blanks_and_duplicates()
    {
        SuggestionMatcher.Suggest(new[] { "b", null, " ", "a", "b" }, "").Should().Equal("b", "a");
        SuggestionMatcher.Suggest(Enumerable.Range(0, 20).Select(i => $"c{i}"), null).Should().HaveCount(SuggestionMatcher.DefaultLimit);
    }

    [Fact]
    public void Returns_nothing_when_nothing_matches_and_respects_the_limit()
    {
        SuggestionMatcher.Suggest(Channels, "zzz").Should().BeEmpty();
        SuggestionMatcher.Suggest(Channels, "e", 2).Should().HaveCount(2);
    }
}
