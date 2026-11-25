using System.Globalization;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.VisualImporterUno.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Xunit;
using LogLevel = AlUmmahNow.ToolsCore.Import.LogLevel;

namespace AlUmmahNow.VisualImporterUno.Tests;

/// <summary>The lines of a log entry and of a failure, each in its own direction, and with no mark Uno draws as a box.</summary>
[Collection("CultureSensitive")]
public sealed class ImportLinesTests
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

    private const string Share = @"\\nas\media\a.mp4";
    private const string System59 = "An unexpected network error occurred";

    private static IEnumerable<LogLine> Lines()
    {
        LogLine L(string code, params string[] args) => new(DateTimeOffset.UtcNow, LogLevel.Info, "x", code, args);
        yield return L("share_path", Share);
        yield return L("share_chosen", @"Y:\a.mp4");
        yield return L("share_tool", System59);
        yield return L("share_back", "123456");
        yield return L("hash_resumed", "123456");
        yield return L("sent_check", "123456");
        yield return L("video_unchecked");
        yield return L("cloud_download");
        yield return L("read_failed", "share_lost", "Windows", "59", System59, "123456", Share);
        yield return L("attempt_failed", "Hash", "1", "share_lost", "10", "x", "Windows", "59", System59, "123456");
        yield return L("failed", "Hash", "6", "share_damaged", "ERR-AAAA-BBBB", "x", "Windows", "59", System59, "123456");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ar")]
    public void No_line_holds_a_direction_mark_and_paths_and_system_words_stand_alone(string culture) => WithCulture(culture, () =>
    {
        foreach (var line in Lines())
        {
            var text = BulkImportViewModel.LogText(line);
            text.Should().NotMatchRegex("[\u200E\u200F\u202A-\u202E\u2066-\u2069]");
            var lines = text.Split('\n');
            if (line.Code is "share_path" or "read_failed") lines.Should().Contain(Share);
            if (line.Args!.Contains(System59)) lines.Should().Contain(System59);
        }
        BulkImportViewModel.ErrorText("share_lost", "x", new ReadError("Windows", 59, System59, 123_456)).Should().NotMatchRegex("[\u2066-\u2069]");
    });

    [Fact]
    public void An_arabic_reference_stands_on_its_own_line() => WithCulture("ar", () =>
        BulkImportViewModel.LogText(Lines().Single(l => l.Code == "failed")).Split('\n')[^1].Should().Be("ERR-AAAA-BBBB"));

    [Theory]
    [InlineData(@"\\nas\media\a.mp4", true)]
    [InlineData(@"Y:\a.mp4", true)]
    [InlineData("/Volumes/Media/a.mp4", true)]
    [InlineData(System59, false)]
    [InlineData("Windows error 59", false)]
    [InlineData("a: b", false)]
    [InlineData("a", false)]
    public void A_path_line_is_known_as_one(string text, bool path)
    {
        var line = new ShownLine(text);
        line.IsPath.Should().Be(path);
        line.IsText.Should().Be(!path);
        line.Path.Should().Be(path ? text : null);
    }

    [Fact]
    public void On_an_arabic_page_the_apps_words_run_right_to_left_and_the_systems_left_to_right()
    {
        WithCulture("ar", () =>
        {
            new ShownLine("توقفت القراءة عند البايت ٥").Direction.Should().Be(FlowDirection.RightToLeft);
            var system = new ShownLine(System59);
            system.Direction.Should().Be(FlowDirection.LeftToRight);
            system.Align.Should().Be(HorizontalAlignment.Left);
        });
        WithCulture("en-US", () => new ShownLine(System59).Align.Should().Be(HorizontalAlignment.Stretch));
        ShownLine.Of("").Should().BeEmpty();
        ShownLine.Of("a\nb").Select(l => l.Text).Should().Equal("a", "b");
    }

    [Fact]
    public void A_pasted_folder_loses_its_quotes() => BulkImportViewModel.CleanFolder(" \"\\\\nas\\media\\Lessons\" ").Should().Be(@"\\nas\media\Lessons");
}
