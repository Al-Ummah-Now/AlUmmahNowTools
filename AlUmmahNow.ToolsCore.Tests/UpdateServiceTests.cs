using System.Net;
using System.Security.Cryptography;
using System.Text;
using AlUmmahNow.ToolsCore.Updates;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>
/// Tools update checks (wiki AUN-APP-UPDATES §5.3): download, every redirect hop checked, SHA-256 checked, fail closed
/// with a report, never a restart while work runs. The signature rules themselves are aun-core's and tested there.
/// </summary>
public sealed class UpdateServiceTests : IDisposable
{
    private const string Tag = "v2026.11.23";
    private const string ManifestUrl = "https://github.com/Al-Ummah-Now/AlUmmahNowTools/releases/download/v2026.11.23/app-release.json";
    private const string PackageUrl = "https://github.com/Al-Ummah-Now/AlUmmahNowTools/releases/download/v2026.11.23/full.nupkg";
    private const string AssetUrl = "https://release-assets.githubusercontent.com/asset/1";
    private static readonly byte[] Package = Encoding.UTF8.GetBytes("velopack package bytes");

    private readonly string _dir = Directory.CreateTempSubdirectory("tools-updates-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeCore : IUpdateCore
    {
        public CorePrompt Prompt = new("Notice", "2026.11.23", "Faster.", "أسرع.", null, null, "install", "https://github.com/Al-Ummah-Now/AlUmmahNowTools/releases/tag/v2026.11.23", ManifestUrl, Tag, 6);
        public string Sha = Convert.ToHexStringLower(SHA256.HashData(Package));
        public ulong Size = (ulong)Package.Length;
        public string? Refuse;
        public readonly List<string> Dismissed = new();
        public CorePrompt ApplyAnswer(string updateJson, bool? deviceHasUpdate) => Prompt;
        public CoreRelease VerifyManifest(byte[] manifest, string signature, string tag)
        {
            if (Refuse is not null) throw new UpdateVerificationException(Refuse);
            Assert.Equal(Tag, tag);
            return new CoreRelease("2026.11.23", Tag, "Faster.", "أسرع.", new[] { new CoreArtifact("tools", "windows", "x64", "velopack-full", PackageUrl, Sha, Size) });
        }
        public bool AllowedDownloadUrl(string url) => url.StartsWith("https://github.com/Al-Ummah-Now/AlUmmahNowTools/releases/download/", StringComparison.Ordinal) || url.StartsWith("https://release-assets.githubusercontent.com/", StringComparison.Ordinal);
        public void Dismiss(string version) => Dismissed.Add(version);
        public bool? AutoInstall { get; set; }
        public void MarkChecked(long atMs) => LastCheckMs = atMs;
        public long? LastCheckMs { get; private set; }
        public string OfflinePromptKind() => "None";
        public string StateJson() => "{}";
    }

    private sealed class FakeInstaller : IUpdateInstaller
    {
        public bool CanSelfUpdate { get; set; } = true;
        public string? Staged;
        public int Restarts;
        public Task StageAsync(string packagePath, CoreRelease release, CoreArtifact artifact, CancellationToken cancel)
        {
            Staged = packagePath;
            return Task.CompletedTask;
        }
        public void ApplyAndRestart() => Restarts++;
    }

    private sealed class FakeHttp : HttpMessageHandler
    {
        public readonly Dictionary<string, Func<HttpResponseMessage>> Routes = new();
        public readonly List<string> Calls = new();
        public string? Reported;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Calls.Add(url);
            if (url.EndsWith("/api/v1/app-config/update-failure", StringComparison.Ordinal))
            {
                Reported = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
            if (url.Contains("/api/v1/app-config?", StringComparison.Ordinal)) return Routes.TryGetValue("config", out var c) ? c() : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"update\":{}}") };
            return Routes.TryGetValue(url, out var r) ? r() : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    private static HttpResponseMessage Redirect(string to)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(to);
        return r;
    }

    private (UpdateService, FakeCore, FakeInstaller, FakeHttp) Make(bool busy = false)
    {
        var core = new FakeCore();
        var installer = new FakeInstaller();
        var http = new FakeHttp();
        http.Routes[ManifestUrl] = () => Ok(Encoding.UTF8.GetBytes("{}"));
        http.Routes[ManifestUrl + ".sig"] = () => Ok(Encoding.UTF8.GetBytes("{\"signatures\":[]}"));
        http.Routes[PackageUrl] = () => Redirect(AssetUrl);
        http.Routes[AssetUrl] = () => Ok(Package);
        var service = new UpdateService(http, () => new Uri("https://videos.api.alummahnow.net"), core, installer, Path.Combine(_dir, "updates.json"), Path.Combine(_dir, "dl"), "2026.11.22", "windows", "x64", () => busy);
        return (service, core, installer, http);
    }

    [Fact]
    public async Task Installs_automatically_after_verifying_and_follows_the_asset_redirect()
    {
        var (service, _, installer, http) = Make();
        Assert.True(await service.CheckAsync(userAsked: true));
        Assert.Equal(UpdateStage.Ready, service.View.Stage);
        Assert.Equal("2026.11.23", service.View.Version);
        Assert.NotNull(installer.Staged);
        Assert.Equal(Package, await File.ReadAllBytesAsync(installer.Staged!));
        Assert.Contains(AssetUrl, http.Calls);
        Assert.Contains("source=direct", http.Calls[0]);
        Assert.Null(http.Reported);
    }

    [Fact]
    public async Task With_automatic_install_off_it_only_offers_the_update()
    {
        var (service, core, installer, _) = Make();
        core.AutoInstall = false;
        await service.CheckAsync(userAsked: false);
        Assert.Equal(UpdateStage.Available, service.View.Stage);
        Assert.Null(installer.Staged);
        await service.UpdateNowAsync();
        Assert.Equal(UpdateStage.Ready, service.View.Stage);
    }

    [Fact]
    public async Task A_changed_package_installs_nothing_offers_no_link_and_is_reported()
    {
        var (service, core, installer, http) = Make();
        core.Sha = new string('0', 64);
        await service.CheckAsync(userAsked: false);
        Assert.Equal(UpdateStage.Hidden, service.View.Stage);
        Assert.Null(service.View.Url);
        Assert.Null(installer.Staged);
        Assert.Contains("\"reason\":\"hash\"", http.Reported);
        Assert.Empty(Directory.Exists(Path.Combine(_dir, "dl")) ? Directory.GetFiles(Path.Combine(_dir, "dl")) : Array.Empty<string>());
    }

    [Fact]
    public async Task A_refused_manifest_is_reported_with_its_reason()
    {
        var (service, core, installer, http) = Make();
        core.Refuse = "bad_signature";
        await service.CheckAsync(userAsked: false);
        Assert.Equal(UpdateStage.Hidden, service.View.Stage);
        Assert.Null(installer.Staged);
        Assert.Contains("\"reason\":\"bad_signature\"", http.Reported);
        Assert.Contains("\"tag\":\"v2026.11.23\"", http.Reported);
    }

    [Fact]
    public async Task A_redirect_to_another_host_is_refused()
    {
        var (service, _, installer, http) = Make();
        http.Routes[PackageUrl] = () => Redirect("https://evil.example/package");
        await service.CheckAsync(userAsked: false);
        Assert.Null(installer.Staged);
        Assert.DoesNotContain("https://evil.example/package", http.Calls);
        Assert.Contains("\"reason\":\"host\"", http.Reported);
    }

    [Fact]
    public async Task A_bigger_file_than_the_manifest_says_is_refused()
    {
        var (service, core, installer, http) = Make();
        core.Size = 3;
        await service.CheckAsync(userAsked: false);
        Assert.Null(installer.Staged);
        Assert.Contains("\"reason\":\"hash\"", http.Reported);
    }

    [Fact]
    public async Task Offline_check_reports_false_and_shows_nothing()
    {
        var (service, _, _, http) = Make();
        http.Routes["config"] = () => throw new HttpRequestException("offline");
        Assert.False(await service.CheckAsync(userAsked: true));
        Assert.Equal(UpdateStage.Hidden, service.View.Stage);
    }

    [Fact]
    public async Task Download_only_builds_open_the_page_and_the_bar_closes_per_version()
    {
        var (service, core, installer, _) = Make();
        installer.CanSelfUpdate = false;
        core.Prompt = core.Prompt with { Action = "download", ManifestUrl = null };
        await service.CheckAsync(userAsked: false);
        Assert.Equal(UpdateStage.Available, service.View.Stage);
        Assert.True(service.View.DownloadOnly);
        Assert.Equal(core.Prompt.Url, await service.UpdateNowAsync());
        service.Dismiss();
        Assert.Equal(new[] { "2026.11.23" }, core.Dismissed);
        Assert.Equal(UpdateStage.Hidden, service.View.Stage);
    }

    [Fact]
    public async Task Forced_and_required_soon_cannot_be_closed()
    {
        var (service, core, _, _) = Make();
        core.Prompt = core.Prompt with { Kind = "RequiredBar", Action = "none" };
        await service.CheckAsync(userAsked: false);
        Assert.Equal(UpdateStage.RequiredSoon, service.View.Stage);
        Assert.False(service.View.CanClose);
        service.Dismiss();
        Assert.Equal(UpdateStage.RequiredSoon, service.View.Stage);
        core.Prompt = core.Prompt with { Kind = "Forced", Action = "install", MessageEn = "Sign-in changed.", MessageAr = "تغيّر تسجيل الدخول." };
        await service.CheckAsync(userAsked: false);
        Assert.Equal(UpdateStage.Forced, service.View.Stage);
        Assert.Equal("تغيّر تسجيل الدخول.", service.View.MessageAr);
    }

    [Fact]
    public async Task Never_restarts_while_work_is_running()
    {
        var (busyService, _, busyInstaller, _) = Make(busy: true);
        await busyService.CheckAsync(userAsked: false);
        Assert.False(busyService.RestartNow());
        Assert.Equal(0, busyInstaller.Restarts);
        var (idle, _, idleInstaller, _) = Make();
        await idle.CheckAsync(userAsked: false);
        Assert.True(idle.RestartNow());
        Assert.Equal(1, idleInstaller.Restarts);
    }

    [Fact]
    public void A_handler_that_follows_redirects_by_itself_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new UpdateService(new SocketsHttpHandler(), () => new Uri("https://x"), new FakeCore(), new FakeInstaller(), Path.Combine(_dir, "s.json"), _dir, "1.0.0", "windows", "x64", () => false));
    }
}
