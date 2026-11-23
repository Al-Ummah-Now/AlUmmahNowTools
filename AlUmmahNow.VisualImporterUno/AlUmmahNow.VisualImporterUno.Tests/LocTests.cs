using System.Globalization;
using AlUmmahNow.VisualImporterUno.Shell;
using FluentAssertions;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

[CollectionDefinition("CultureSensitive", DisableParallelization = true)]
public sealed class CultureSensitiveCollection { }

[Collection("CultureSensitive")]
public sealed class LocTests
{
    private static void WithCulture(string cultureName, Action test)
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);
            test();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData(0, false, "Other")]
    [InlineData(1, false, "One")]
    [InlineData(2, false, "Other")]
    [InlineData(15, false, "Other")]
    [InlineData(0, true, "Zero")]
    [InlineData(1, true, "One")]
    [InlineData(2, true, "Two")]
    [InlineData(3, true, "Few")]
    [InlineData(10, true, "Few")]
    [InlineData(11, true, "Many")]
    [InlineData(99, true, "Many")]
    [InlineData(100, true, "Other")]
    [InlineData(103, true, "Few")]
    [InlineData(111, true, "Many")]
    public void PluralCategory_FollowsEnglishAndArabicCldrRules(long count, bool arabic, string expected)
    {
        Loc.PluralCategory(count, arabic).Should().Be(expected);
    }

    [Fact]
    public void NumberAndDecimal_FormatEnglishWithGrouping_AndArabicWithIndicDigits()
    {
        WithCulture("en-US", () =>
        {
            Loc.IsArabic.Should().BeFalse();
            Loc.Number(4887).Should().Be("4,887");
            Loc.Decimal(12.5).Should().Be("12.5");
            Loc.Percent(42).Should().Be("42%");
            Loc.Bytes(1536).Should().Be("1.5 KB");
        });

        WithCulture("ar", () =>
        {
            Loc.IsArabic.Should().BeTrue();
            // Arabic numbers are never thousands-grouped and use Arabic-Indic digits + U+066B decimal separator
            Loc.Number(4887).Should().NotContain(",").And.NotContain("\u066C");
            Loc.Number(4887).Should().Be(Loc.ToArabicDigits("4887"));
            Loc.Decimal(12.5).Should().Contain("٫");
            Loc.Percent(42).Should().EndWith("٪");
            Loc.Bytes(1536).Should().EndWith("كيلوبايت");
        });
    }

    [Fact]
    public void Count_TruncatesToOneDecimalWithoutRoundingUp()
    {
        WithCulture("en-US", () =>
        {
            Loc.Count(999).Should().Be("999");
            Loc.Count(4899).Should().Be("4.8K");
            Loc.Count(1_290_000).Should().Be("1.2M");
            Loc.Count(2_590_000_000).Should().Be("2.5B");
        });

        WithCulture("ar", () =>
        {
            Loc.Count(4899).Should().EndWith(" ألف");
            Loc.Count(1_290_000).Should().EndWith(" م");
            Loc.Count(2_590_000_000).Should().EndWith(" مليار");
        });
    }

    [Fact]
    public void LanguagesAndGroups_UseLanguageSpecificSeparators()
    {
        WithCulture("en-US", () =>
        {
            Loc.Languages(new[] { "Arabic", "English" }).Should().Be("Arabic, English");
            Loc.Groups(new[] { "captions", "chapters" }).Should().Be("captions\u00A0· chapters");
        });

        WithCulture("ar", () =>
        {
            Loc.Languages(new[] { "العربية", "الإنجليزية" }).Should().Be("العربية والإنجليزية");
            Loc.Groups(new[] { "ترجمات", "فصول" }).Should().Be("ترجمات؛ فصول");
        });
    }

    [Fact]
    public void KeepLastWords_JoinsLastTwoWordsWithNoBreakSpace()
    {
        Loc.KeepLastWords("First sentence here. Second line").Should().Be("First sentence\u00A0here. Second\u00A0line");
        Loc.KeepLastWords("OneWord").Should().Be("OneWord");
    }

    [Fact]
    public void WithDetail_PlacesRawDetailOnItsOwnLineWhenPresent()
    {
        Loc.WithDetail("Import failed", null).Should().Be("Import failed");
        Loc.WithDetail("Import failed", "   ").Should().Be("Import failed");
        Loc.WithDetail("Import failed", "  disk full  ").Should().Be("Import failed\ndisk full");
    }

    [Fact]
    public void ZoneLabel_FormatsUtcUsAndOffsetZonesInBothLanguages()
    {
        var winter = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var summer = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.Utc;
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var customPlus3 = TimeZoneInfo.CreateCustomTimeZone("Custom/Plus3", TimeSpan.FromHours(3), "Plus3", "Plus3");

        Loc.ZoneLabel(winter, utc, arabic: false).Should().Be("UTC");
        Loc.ZoneLabel(winter, utc, arabic: true).Should().Be("غرينتش");

        Loc.ZoneLabel(winter, eastern, arabic: false).Should().Be("EST");
        Loc.ZoneLabel(summer, eastern, arabic: false).Should().Be("EDT");
        Loc.ZoneLabel(winter, eastern, arabic: true).Should().Be("غرينتش-٥");

        Loc.ZoneLabel(winter, customPlus3, arabic: false).Should().Be("GMT+3");
        Loc.ZoneLabel(winter, customPlus3, arabic: true).Should().Be("غرينتش+٣");
    }
}
