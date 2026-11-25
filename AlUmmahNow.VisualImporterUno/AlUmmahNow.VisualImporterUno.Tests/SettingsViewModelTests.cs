using AlUmmahNow.ToolsCore;
using AlUmmahNow.VisualImporterUno.Converters;
using AlUmmahNow.VisualImporterUno.Services;
using AlUmmahNow.VisualImporterUno.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly List<FileScanServiceManager> _managers = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("settings-vm-").FullName;

    private SettingsViewModel CreateViewModel(
        Dictionary<string, string?>? configValues = null,
        Mock<INotificationService>? notifications = null,
        UserSettingsFile? file = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();
        var serviceManager = new FileScanServiceManager(NullLogger<FileScanServiceManager>.Instance, "http://127.0.0.1:59999");
        _managers.Add(serviceManager);
        var notif = notifications ?? new Mock<INotificationService>();
        return new SettingsViewModel(config, NullLogger<SettingsViewModel>.Instance, serviceManager, notif.Object, file ?? new UserSettingsFile(_dir));
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        foreach (var manager in _managers)
        {
            manager.Dispose();
        }
    }

    [Fact]
    public async Task SavedSettings_AreReadBackAndAreTheOnesTheAppsStartReads()
    {
        var vm = CreateViewModel();
        vm.FileScanServiceUrl = "http://10.0.0.42:5000";
        vm.AutoStartService = false;
        (await vm.SaveSettingsAsync()).Should().BeTrue();

        var next = CreateViewModel();
        next.FileScanServiceUrl.Should().Be("http://10.0.0.42:5000");
        next.AutoStartService.Should().BeFalse();
        new UserSettingsFile(_dir).Read().Should().Contain("10.0.0.42");
    }

    [Fact]
    public void ScannerLocationText_DistinguishesLoopbackFromRemoteHost_AndNotifiesOnUrlChange()
    {
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.FileScanServiceUrl = "http://localhost:5000";
        var localLabel = vm.ScannerLocationText;

        vm.FileScanServiceUrl = "http://10.0.0.42:5000";
        var remoteLabel = vm.ScannerLocationText;

        remoteLabel.Should().NotBe(localLabel);
        changed.Should().Contain(nameof(SettingsViewModel.ScannerLocationText));
    }

    [Fact]
    public void NotificationTogglesAndDetectedPath_UpdateBackingServicesAndProperties()
    {
        var notifications = new Mock<INotificationService>();
        notifications.SetupProperty(n => n.IsEnabled, true);
        notifications.SetupProperty(n => n.IsSoundEnabled, true);

        var vm = CreateViewModel(notifications: notifications);

        vm.EnableNotifications = false;
        vm.EnableNotificationSound = false;

        notifications.Object.IsEnabled.Should().BeFalse();
        notifications.Object.IsSoundEnabled.Should().BeFalse();

        vm.SelectedDetectedPath = "/opt/tools/AlUmmahNow.FileScanService";
        vm.ServiceExecutablePath.Should().Be("/opt/tools/AlUmmahNow.FileScanService");
    }

    [Fact]
    public async Task TestConnectionAsync_WithInvalidUrl_ReportsBadAddressWithoutThrowing()
    {
        var vm = CreateViewModel();
        vm.FileScanServiceUrl = "not a valid url";

        await vm.TestConnectionAsync();

        vm.IsTesting.Should().BeFalse();
        vm.TestResults.Should().NotBeNullOrWhiteSpace();

        // Changing the URL clears the stale test result
        vm.FileScanServiceUrl = "http://localhost:5000";
        vm.TestResults.Should().BeEmpty();
    }

    [Fact]
    public void ResetToDefaults_RestoresExpectedDefaultValues()
    {
        var vm = CreateViewModel();
        vm.FileScanServiceUrl = "http://192.168.1.5:7000";
        vm.ServiceExecutablePath = "/tmp/custom";
        vm.AutoStartService = false;
        vm.EnableNotifications = false;
        vm.EnableNotificationSound = false;

        vm.ResetToDefaults();

        vm.FileScanServiceUrl.Should().Be("http://localhost:5000");
        vm.ServiceExecutablePath.Should().BeEmpty();
        vm.AutoStartService.Should().BeTrue();
        vm.EnableNotifications.Should().BeTrue();
        vm.EnableNotificationSound.Should().BeTrue();
        vm.TestResults.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData("not-a-bool", false)]
    public void BoolNegationConverter_NegatesBooleansAndDefaultsNonBooleansToFalse(object input, bool expected)
    {
        var converter = new BoolNegationConverter();
        converter.Convert(input, typeof(bool), string.Empty, "en-US").Should().Be(expected);
        converter.ConvertBack(input, typeof(bool), string.Empty, "en-US").Should().Be(expected);
    }
}
