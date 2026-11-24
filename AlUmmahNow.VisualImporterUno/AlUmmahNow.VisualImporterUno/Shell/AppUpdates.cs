using System.Reflection;
using AlUmmahNow.ToolsCore;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Updates;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The app's one update service (wiki AUN-APP-UPDATES §5): created at launch, read by every shell the window builds
/// (the shell is rebuilt on a language change). Demo runs (TOOLS_DEMO_UPDATE) show a fixed state for screenshots and
/// never call the network.
/// </summary>
public static class AppUpdates
{
    public static UpdateService? Service { get; private set; }

    /// <summary>An import step is running: never restart for an update then.</summary>
    public static bool IsBusy() =>
        ImportWorkspace.IfStarted is { } workspace && workspace.Queue.Items.Any(i => i.CurrentStep?.Status == StepStatus.Running);

    /// <summary>This build's version (2026.11.22[.N]); "0.0.0" for a development build without one.</summary>
    public static string InstalledVersion =>
        typeof(AppUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] is { Length: > 0 } v && AlUmmahNow.Core.AunCoreMethods.CompareAppVersions(v, "0.0.0") is not null
            ? v
            : "0.0.0";

    public static string Platform => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

    public static string Arch => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

    public static void Start(ShellState state, IUpdateInstaller installer)
    {
        var demo = Environment.GetEnvironmentVariable("TOOLS_DEMO_UPDATE");
        var core = demo is { Length: > 0 } ? (IUpdateCore)new DemoUpdateCore() : CoreUpdateChecker.Load(InstalledVersion, UpdateService.DefaultStatePath);
        Service = new UpdateService(
            UpdateService.CreateHandler(),
            () => ToolsEnvironment.For(state.Preferences.Environment).VideosApi,
            core,
            demo is { Length: > 0 } ? new DemoInstaller() : installer,
            UpdateService.DefaultStatePath,
            UpdateService.DefaultDownloadDir,
            demo is { Length: > 0 } ? "2026.11.22" : InstalledVersion,
            Platform,
            Arch,
            IsBusy);
        if (demo is { Length: > 0 }) Service.Preview(DemoView(demo));
        else Service.Start();
    }

    private static UpdateView DemoView(string state)
    {
        const string notesEn = "Imports resume faster after a restart.\nThe folder list shows each file's size.";
        const string notesAr = "تُستأنف عمليات الاستيراد أسرع بعد إعادة التشغيل.\nتعرض قائمة المجلدات حجم كل ملف.";
        return state switch
        {
            "available" => new UpdateView(UpdateStage.Available, "2026.11.23", notesEn, notesAr),
            "download" => new UpdateView(UpdateStage.Available, "2026.11.23", notesEn, notesAr, DownloadOnly: true, Url: "https://github.com/Al-Ummah-Now/AlUmmahNowTools/releases/tag/v2026.11.23"),
            "downloading" => new UpdateView(UpdateStage.Downloading, "2026.11.23", notesEn, notesAr, Progress: 0.42),
            "ready" => new UpdateView(UpdateStage.Ready, "2026.11.23", notesEn, notesAr),
            "required-soon" => new UpdateView(UpdateStage.RequiredSoon),
            "forced" => new UpdateView(UpdateStage.Forced, "2026.11.23", notesEn, notesAr, "Sign-in changed. This version can no longer reach the server.", "تغيّر تسجيل الدخول. لم يعد هذا الإصدار يصل إلى الخادم."),
            _ => UpdateView.None,
        };
    }

    private sealed class DemoUpdateCore : IUpdateCore
    {
        public CorePrompt ApplyAnswer(string updateJson, bool? deviceHasUpdate) => new("None", null, null, null, null, null, "none", null, null, null, 6);
        public CoreRelease VerifyManifest(byte[] manifest, string signature, string tag) => throw new UpdateVerificationException("demo");
        public bool AllowedDownloadUrl(string url) => false;
        public void Dismiss(string version) { }
        public bool? AutoInstall { get; set; } = true;
        public void MarkChecked(long atMs) { }
        public long? LastCheckMs => DateTimeOffset.Parse("2026-11-23T15:04:00Z").ToUnixTimeMilliseconds();
        public string OfflinePromptKind() => "None";
        public string StateJson() => "{}";
    }

    private sealed class DemoInstaller : IUpdateInstaller
    {
        public bool CanSelfUpdate => true;
        public Task StageAsync(string packagePath, CoreRelease release, CoreArtifact artifact, CancellationToken cancel) => Task.CompletedTask;
        public void ApplyAndRestart() { }
    }
}
