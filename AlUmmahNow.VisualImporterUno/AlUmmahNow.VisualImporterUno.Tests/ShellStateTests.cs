using AlUmmahNow.ToolsCore;
using AlUmmahNow.VisualImporterUno.Shell;
using FluentAssertions;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

public sealed class ShellStateTests
{
    private static string TempPreferencesPath() =>
        Path.Combine(Path.GetTempPath(), $"aun-tools-prefs-{Guid.NewGuid():N}", "preferences.json");

    [Theory]
    [InlineData("signed-out", SessionState.SignedOut)]
    [InlineData("not-staff", SessionState.NotStaff)]
    [InlineData("unreachable", SessionState.Unreachable)]
    [InlineData("staff", SessionState.Staff)]
    public async Task ShellDemo_DrivesSessionToExpectedState(string demoState, SessionState expected)
    {
        var prefs = new ToolsPreferences { Language = "en", Theme = ToolsTheme.Dark, Environment = ToolsEnvironmentKind.Production };
        var demo = new ShellDemo(prefs, demoState);
        var state = new ShellState(TempPreferencesPath(), demo);

        try
        {
            await demo.DriveAsync(state.Session);
            state.Session.State.Should().Be(expected);
        }
        finally
        {
            state.Session.Dispose();
        }
    }

    [Fact]
    public async Task ShellState_UpdatesThemeSpeechModelAndEnvironment_AndRaisesEvents()
    {
        var prefs = new ToolsPreferences { Language = "en", Theme = ToolsTheme.Dark, Environment = ToolsEnvironmentKind.Production };
        var demo = new ShellDemo(prefs, "staff");
        var state = new ShellState(TempPreferencesPath(), demo);

        try
        {
            var themeChanged = 0;
            var sessionChanged = 0;
            state.ThemeChanged += (_, _) => themeChanged++;
            state.Changed += (_, _) => sessionChanged++;

            state.SetTheme(ToolsTheme.Light);
            state.Preferences.Theme.Should().Be(ToolsTheme.Light);
            themeChanged.Should().Be(1);

            state.SetSpeechModel("large-v3-turbo");
            state.Preferences.SpeechModel.Should().Be("large-v3-turbo");

            await state.SwitchEnvironmentAsync(ToolsEnvironmentKind.Qa);
            state.Preferences.Environment.Should().Be(ToolsEnvironmentKind.Qa);
            state.Session.Environment.Kind.Should().Be(ToolsEnvironmentKind.Qa);
            state.Session.State.Should().Be(SessionState.Staff);
            sessionChanged.Should().BeGreaterThan(0);
        }
        finally
        {
            state.Session.Dispose();
        }
    }
}
