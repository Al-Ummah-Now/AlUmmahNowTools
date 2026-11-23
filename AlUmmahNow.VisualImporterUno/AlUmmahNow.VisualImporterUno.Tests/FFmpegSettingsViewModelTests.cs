using AlUmmahNow.Data.Services;
using AlUmmahNow.VisualImporterUno.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

public sealed class FFmpegSettingsViewModelTests
{
    private static async Task WaitUntilIdleAsync(FFmpegSettingsViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while ((vm.IsVerifying || vm.IsAutoDetecting) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task VerifyInstallationAsync_WhenFullyInstalled_SetsPathsVersionAndReadyState()
    {
        var manager = new Mock<IFFmpegManagerService>();
        manager.Setup(m => m.VerifyFFmpegInstallationAsync()).ReturnsAsync(new FFmpegStatus
        {
            IsFullyInstalled = true,
            FFmpegExists = true,
            FFmpegWorks = true,
            FFprobeExists = true,
            FFprobeWorks = true,
            AutoDetected = true,
            FFmpegVersion = "7.1.1",
            FFmpegPath = "/usr/bin/ffmpeg",
            FFprobePath = "/usr/bin/ffprobe",
        });

        var vm = new FFmpegSettingsViewModel(NullLogger<FFmpegSettingsViewModel>.Instance, manager.Object);
        await WaitUntilIdleAsync(vm);
        await vm.VerifyInstallationAsync();

        vm.IsInstalled.Should().BeTrue();
        vm.AutoDetected.Should().BeTrue();
        vm.FFmpegVersion.Should().Be("7.1.1");
        vm.FFmpegPath.Should().Be("/usr/bin/ffmpeg");
        vm.FFprobePath.Should().Be("/usr/bin/ffprobe");
        vm.StatusTitle.Should().NotBeNullOrWhiteSpace();
        vm.StatusMessage.Should().NotBeNullOrWhiteSpace();

        // Changing a path clears the prior verification message
        vm.FFmpegPath = "/opt/bin/ffmpeg";
        vm.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyInstallationAsync_WhenMissingBinaries_MarksNotInstalledWithDetail()
    {
        var manager = new Mock<IFFmpegManagerService>();
        manager.Setup(m => m.VerifyFFmpegInstallationAsync()).ReturnsAsync(new FFmpegStatus
        {
            IsFullyInstalled = false,
            FFmpegExists = false,
            FFmpegWorks = false,
            FFprobeExists = true,
            FFprobeWorks = false,
            ErrorMessage = "ffprobe exited with code 127",
        });

        var vm = new FFmpegSettingsViewModel(NullLogger<FFmpegSettingsViewModel>.Instance, manager.Object);
        await WaitUntilIdleAsync(vm);
        await vm.VerifyInstallationAsync();

        vm.IsInstalled.Should().BeFalse();
        vm.StatusMessage.Should().Contain("ffprobe exited with code 127");
    }

    [Fact]
    public async Task AutoDetectAsync_UpdatesPathsAndVerifiesWhenFound_AndReportsMissingWhenNotFound()
    {
        var manager = new Mock<IFFmpegManagerService>();
        manager.Setup(m => m.VerifyFFmpegInstallationAsync()).ReturnsAsync(new FFmpegStatus());
        manager.Setup(m => m.AutoDetectFFmpegAsync()).ReturnsAsync(false);

        var vm = new FFmpegSettingsViewModel(NullLogger<FFmpegSettingsViewModel>.Instance, manager.Object);
        await WaitUntilIdleAsync(vm);

        await vm.AutoDetectAsync();
        vm.IsInstalled.Should().BeFalse();
        vm.IsAutoDetecting.Should().BeFalse();

        manager.Setup(m => m.AutoDetectFFmpegAsync()).ReturnsAsync(true);
        manager.SetupGet(m => m.FFmpegPath).Returns("/opt/homebrew/bin/ffmpeg");
        manager.SetupGet(m => m.FFprobePath).Returns("/opt/homebrew/bin/ffprobe");
        manager.Setup(m => m.VerifyFFmpegInstallationAsync()).ReturnsAsync(new FFmpegStatus
        {
            IsFullyInstalled = true,
            FFmpegExists = true,
            FFmpegWorks = true,
            FFprobeExists = true,
            FFprobeWorks = true,
            AutoDetected = true,
            FFmpegPath = "/opt/homebrew/bin/ffmpeg",
            FFprobePath = "/opt/homebrew/bin/ffprobe",
        });

        await vm.AutoDetectAsync();
        vm.IsInstalled.Should().BeTrue();
        vm.AutoDetected.Should().BeTrue();
        vm.FFmpegPath.Should().Be("/opt/homebrew/bin/ffmpeg");
        vm.FFprobePath.Should().Be("/opt/homebrew/bin/ffprobe");
    }

    [Fact]
    public async Task SavePathsAsync_RejectsBlankOrUnverifiedPaths()
    {
        var manager = new Mock<IFFmpegManagerService>();
        manager.Setup(m => m.VerifyFFmpegInstallationAsync()).ReturnsAsync(new FFmpegStatus());

        var vm = new FFmpegSettingsViewModel(NullLogger<FFmpegSettingsViewModel>.Instance, manager.Object);
        await WaitUntilIdleAsync(vm);

        vm.FFmpegPath = "";
        vm.FFprobePath = "";
        (await vm.SavePathsAsync()).Should().BeFalse();
        manager.Verify(m => m.SetFFmpegPaths(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        vm.FFmpegPath = "/bad/ffmpeg";
        vm.FFprobePath = "/bad/ffprobe";
        (await vm.SavePathsAsync()).Should().BeFalse();
        manager.Verify(m => m.SetFFmpegPaths("/bad/ffmpeg", "/bad/ffprobe"), Times.Once);
    }

    [Fact]
    public void InstallCommandForThisComputer_IsPopulatedForCurrentOs()
    {
        var cmd = FFmpegSettingsViewModel.InstallCommandForThisComputer;
        cmd.Should().NotBeNullOrWhiteSpace();
        if (OperatingSystem.IsMacOS()) cmd.Should().Contain("brew");
        else if (OperatingSystem.IsWindows()) cmd.Should().Contain("winget");
        else cmd.Should().Contain("apt");
    }
}
