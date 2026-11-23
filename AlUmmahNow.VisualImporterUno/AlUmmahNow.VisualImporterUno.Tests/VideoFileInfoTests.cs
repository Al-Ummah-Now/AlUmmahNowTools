using System.Globalization;
using AlUmmahNow.VisualImporterUno.Models;
using FluentAssertions;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

[Collection("CultureSensitive")]
public sealed class VideoFileInfoTests
{
    [Fact]
    public void FileNameAndChannelName_DeriveFromPathAndRespectOverride()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MyChannelFolder");
        var info = new VideoFileInfo
        {
            FileName = "lesson-01.mp4",
            Directory = dir,
            FileSize = 2_097_152,
        };

        info.FileNameWithoutExtension.Should().Be("lesson-01");
        info.ChannelName.Should().Be("MyChannelFolder");

        var changed = new List<string?>();
        info.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        info.ChannelNameOverride = "Custom Scholar";

        info.ChannelName.Should().Be("Custom Scholar");
        changed.Should().Contain(nameof(VideoFileInfo.ChannelNameOverride));
        changed.Should().Contain(nameof(VideoFileInfo.ChannelName));
    }

    [Theory]
    [InlineData(ImportFileStatus.NotImported, "Ready", "SystemControlForegroundBaseMediumBrush", "\uE73E")]
    [InlineData(ImportFileStatus.Pending, "Pending", "SystemFillColorAttentionBrush", "\uE916")]
    [InlineData(ImportFileStatus.Uploading, "Uploading", "AccentTextFillColorPrimaryBrush", "\uE898")]
    [InlineData(ImportFileStatus.Uploaded, "Uploaded", "SystemFillColorSuccessBrush", "\uE73E")]
    [InlineData(ImportFileStatus.Failed, "Failed", "SystemFillColorCriticalBrush", "\uE783")]
    [InlineData(ImportFileStatus.Skipped, "Skipped", "SystemControlForegroundBaseMediumBrush", "\uE738")]
    public void StatusProperties_ReflectImportStatus_AndRaiseDependentNotifications(
        ImportFileStatus status,
        string expectedText,
        string expectedColor,
        string expectedIcon)
    {
        var info = new VideoFileInfo();
        var changed = new List<string?>();
        info.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        info.ImportStatus = status;

        info.StatusText.Should().Be(expectedText);
        info.StatusColor.Should().Be(expectedColor);
        info.StatusIcon.Should().Be(expectedIcon);

        if (status != ImportFileStatus.NotImported)
        {
            changed.Should().Contain(new[]
            {
                nameof(VideoFileInfo.ImportStatus),
                nameof(VideoFileInfo.StatusText),
                nameof(VideoFileInfo.StatusColor),
                nameof(VideoFileInfo.StatusIcon),
            });
        }
    }

    [Fact]
    public void DuplicateAndExcludedStates_TakePrecedenceOverImportStatus()
    {
        var info = new VideoFileInfo { ImportStatus = ImportFileStatus.Uploading };

        info.IsSelected = false;
        info.StatusText.Should().Be("Excluded");
        info.StatusIcon.Should().Be("\uE738");

        info.IsDuplicate = true;
        info.StatusText.Should().Be("Duplicate");
        info.StatusColor.Should().Be("SystemFillColorCautionBrush");
        info.StatusIcon.Should().Be("\uE7C3");
    }

    [Fact]
    public void FileStatistics_FormatsSizesUsingCurrentUiCulture()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var stats = new FileStatistics
            {
                TotalSize = 1024 * 1024 * 5L,
                UniqueSize = 1024 * 1024 * 3L,
                DuplicateSize = 1024 * 1024 * 2L,
            };

            stats.TotalSizeFormatted.Should().Be("5 MB");
            stats.UniqueSizeFormatted.Should().Be("3 MB");
            stats.DuplicateSizeFormatted.Should().Be("2 MB");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
