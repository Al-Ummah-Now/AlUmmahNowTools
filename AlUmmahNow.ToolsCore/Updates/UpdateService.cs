using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AlUmmahNow.ToolsCore.Updates;

/// <summary>What the bar, the forced screen and Settings → Updates show (wiki AUN-APP-UPDATES §5.3, §12).</summary>
public enum UpdateStage
{
    /// <summary>Nothing to show</summary>
    Hidden,
    /// <summary>A new version can be downloaded: "Version … is available." + What's new + Update</summary>
    Available,
    /// <summary>Downloading and checking it: progress in the bar</summary>
    Downloading,
    /// <summary>Downloaded and verified: "Update ready. It installs when you close the app." + Restart now</summary>
    Ready,
    /// <summary>Below the minimum but nothing to update to yet: a bar that can't be closed</summary>
    RequiredSoon,
    /// <summary>Below the minimum and an update is there: the full-window screen</summary>
    Forced,
}

/// <summary>Everything the UI needs; immutable, replaced on each change.</summary>
public sealed record UpdateView(
    UpdateStage Stage,
    string? Version = null,
    string? NotesEn = null,
    string? NotesAr = null,
    string? MessageEn = null,
    string? MessageAr = null,
    double? Progress = null,
    /// <summary>The button opens <see cref="UpdateService.DownloadPage"/> in the browser; nothing is downloaded or installed</summary>
    bool DownloadOnly = false,
    /// <summary>Below the minimum: the forced screen stays up while its update downloads and waits for a restart</summary>
    bool Required = false)
{
    public static readonly UpdateView None = new(UpdateStage.Hidden);
    public bool CanClose => !Required && (Stage is UpdateStage.Available or UpdateStage.Ready);
}

/// <summary>The decision the core makes for one answer (aun-core <c>UpdatePrompt</c>).</summary>
public sealed record CorePrompt(string Kind, string? Version, string? NotesEn, string? NotesAr, string? MessageEn, string? MessageAr, string Action, string? Url, string? ManifestUrl, string? Tag, uint CheckHours);

public sealed record CoreArtifact(string App, string Platform, string? Arch, string Kind, string Url, string Sha256, ulong Size);

public sealed record CoreRelease(string Version, string Tag, string NotesEn, string NotesAr, IReadOnlyList<CoreArtifact> Artifacts);

/// <summary>A refused manifest: <see cref="Reason"/> is the code reported to the server.</summary>
public sealed class UpdateVerificationException(string reason) : Exception($"update refused: {reason}")
{
    public string Reason { get; } = reason;
}

/// <summary>aun-core's <c>UpdateChecker</c> as Tools uses it; faked in tests (CoreUpdateChecker adapts the real one).</summary>
public interface IUpdateCore
{
    CorePrompt ApplyAnswer(string updateJson, bool? deviceHasUpdate);
    /// <exception cref="UpdateVerificationException">the manifest was refused</exception>
    CoreRelease VerifyManifest(byte[] manifest, string signature, string tag);
    bool AllowedDownloadUrl(string url);
    void Dismiss(string version);
    bool? AutoInstall { get; set; }
    void MarkChecked(long atMs);
    long? LastCheckMs { get; }
    string OfflinePromptKind();
    string StateJson();
}

/// <summary>Velopack (or nothing, in builds that can't update themselves).</summary>
public interface IUpdateInstaller
{
    /// <summary>A build installed by our own installer (Velopack): it can update itself</summary>
    bool CanSelfUpdate { get; }
    /// <summary>Hands Velopack the downloaded, verified package; <see cref="ApplyOnExit"/> or <see cref="ApplyAndRestart"/> installs it</summary>
    Task StageAsync(string packagePath, CoreRelease release, CoreArtifact artifact, CancellationToken cancel);
    /// <summary>The staged file is still the one the signed manifest lists</summary>
    /// <exception cref="UpdateVerificationException">it isn't</exception>
    void VerifyStaged();
    /// <summary>Drops the staged update: it is not installed, now or later</summary>
    void Discard();
    /// <summary>The app is exiting: installs the staged update once it has. Only right after <see cref="VerifyStaged"/></summary>
    void ApplyOnExit();
    /// <summary>The same, and the app is started again after it. Only right after <see cref="VerifyStaged"/></summary>
    void ApplyAndRestart();
}

/// <summary>
/// Update checks for Tools (wiki AUN-APP-UPDATES §5): on launch (after 30 s), every <c>checkHours</c> and on Check now,
/// ask <c>GET /api/v1/app-config</c>; then download, verify (ed25519 manifest, every redirect hop, SHA-256) and stage the
/// update. Anything that fails verification installs nothing, offers no link, and is reported to the server.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string Repo = "AlUmmahNowTools";
    /// <summary>The one page Download opens (wiki §4.1.1): fixed here, never taken from an answer, a manifest or the network.</summary>
    public const string DownloadPage = "https://github.com/Al-Ummah-Now/AlUmmahNowTools/releases";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private const int MaxRedirects = 5;

    private readonly HttpMessageHandler _handler;
    private readonly Func<Uri> _videosApi;
    private readonly IUpdateCore _core;
    private readonly IUpdateInstaller _installer;
    private readonly string _statePath;
    private readonly string _downloadDir;
    private readonly string _installedVersion;
    private readonly string _platform;
    private readonly string _arch;
    private readonly Func<bool> _busy;
    private readonly bool _hasRootKey;
    private readonly Func<bool> _otherCopy;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITimer? _timer;
    private UpdateView _view = UpdateView.None;
    private CorePrompt? _lastPrompt;
    private Staged? _staged;
    // One at a time checks the staged update and hands it over
    private readonly SemaphoreSlim _applying = new(1, 1);
    private volatile bool _handedOver;
    private int _restarting;
    private readonly Func<Task<bool>>? _quit;
    private bool _autoInstallDefault = true;
    /// <summary>The release the last answer offers to install, whether or not its notice was closed</summary>
    private string? _offeredTag;

    /// <summary>A staged update and what proves it: checked again before it is installed</summary>
    private sealed record Staged(string Version, string Tag, byte[] Manifest, string Signature);

    /// <param name="hasRootKey">this build carries a root key: without one it can verify nothing, so it only shows the notice with its fixed page (§4.1.1)</param>
    /// <param name="busy">true while an import, upload or encode runs: never restart then</param>
    /// <param name="otherCopy">true while another copy runs from the same install: the updater would end it, so nothing is installed then</param>
    public UpdateService(HttpMessageHandler handler, Func<Uri> videosApi, IUpdateCore core, IUpdateInstaller installer, string statePath, string downloadDir,
        string installedVersion, string platform, string arch, Func<bool> busy, bool hasRootKey, TimeProvider? clock = null, Func<bool>? otherCopy = null, Func<Task<bool>>? quit = null)
    {
        // Every redirect hop is checked here, so the handler must never follow one by itself
        if (handler is SocketsHttpHandler { AllowAutoRedirect: true } or HttpClientHandler { AllowAutoRedirect: true })
            throw new ArgumentException("the update handler must not follow redirects (AllowAutoRedirect = false)", nameof(handler));
        _handler = handler;
        _videosApi = videosApi;
        _core = core;
        _installer = installer;
        _statePath = statePath;
        _downloadDir = downloadDir;
        _installedVersion = installedVersion;
        _platform = platform;
        _arch = arch;
        _busy = busy;
        _hasRootKey = hasRootKey;
        _otherCopy = otherCopy ?? (() => false);
        _quit = quit;
        _clock = clock ?? TimeProvider.System;
    }

    public UpdateView View => _view;
    public event Action<UpdateView>? Changed;

    /// <summary>
    /// Settings → Updates: "Install updates automatically": the user's own choice, else the admin's default
    /// (`updates.autoInstallDefault`), else on (APPUPD-D5)
    /// </summary>
    public bool AutoInstall
    {
        get => _core.AutoInstall ?? _autoInstallDefault;
        set
        {
            _core.AutoInstall = value;
            SaveState();
        }
    }

    public bool CanSelfUpdate => _hasRootKey && _installer.CanSelfUpdate;

    /// <summary>The server's appUpdates switch at the last check; null before the first one. Settings → Updates shows only while it is on.</summary>
    public bool? Enabled { get; private set; }
    public DateTimeOffset? LastChecked => _core.LastCheckMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null;
    public string InstalledVersion => _installedVersion;

    /// <summary>Shows a fixed state without checking anything: demo runs and screenshots only (TOOLS_DEMO_UPDATE).</summary>
    public void Preview(UpdateView view)
    {
        Enabled = true;
        Publish(view);
    }

    /// <summary>Starts the schedule: the first check 30 s after launch.</summary>
    public void Start()
    {
        if (_core.OfflinePromptKind() == "RequiredBar") Publish(new UpdateView(UpdateStage.RequiredSoon));
        _timer = _clock.CreateTimer(_ => _ = CheckAsync(userAsked: false), null, FirstCheckDelay, Timeout.InfiniteTimeSpan);
    }

    private string Source => CanSelfUpdate ? "direct" : "managed";

    /// <summary>One check. Returns false when the server couldn't be reached (Check now shows "Couldn't check…").</summary>
    public async Task<bool> CheckAsync(bool userAsked, CancellationToken cancel = default)
    {
        if (!await _gate.WaitAsync(0, cancel).ConfigureAwait(false)) return true;
        try
        {
            CorePrompt prompt;
            try
            {
                using var http = new HttpClient(_handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(20) };
                var url = new Uri(_videosApi(), $"/api/v1/app-config?app=tools&platform={_platform}&version={Uri.EscapeDataString(_installedVersion)}&source={Source}&arch={_arch}&channel=stable");
                var json = await http.GetStringAsync(url, cancel).ConfigureAwait(false);
                using (var answer = JsonDocument.Parse(json))
                {
                    Enabled = answer.RootElement.TryGetProperty("update", out var update) && update.TryGetProperty("enabled", out var on) && on.ValueKind == JsonValueKind.True;
                    _offeredTag = Enabled == true && update.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String && action.GetString() == "install"
                        && update.TryGetProperty("tag", out var tag) && tag.ValueKind == JsonValueKind.String ? tag.GetString() : null;
                    _autoInstallDefault = !(answer.RootElement.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object
                        && config.TryGetProperty("updates.autoInstallDefault", out var auto) && auto.ValueKind == JsonValueKind.False);
                }
                prompt = Gate(_core.ApplyAnswer(json, null));
                _core.MarkChecked(_clock.GetUtcNow().ToUnixTimeMilliseconds());
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                Reschedule(TimeSpan.FromHours(1));
                return false;
            }
            _lastPrompt = prompt;
            Reschedule(TimeSpan.FromHours(Math.Clamp(prompt.CheckHours, 1u, 48u)));
            SaveState();
            await ActOnAsync(prompt, cancel).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The root-key rules (wiki §4.1.1). No URL of an answer is ever opened: Download opens <see cref="DownloadPage"/>.
    /// Without a root key everything is the notice; with one, a notice answer is nothing.
    /// </summary>
    private CorePrompt Gate(CorePrompt p)
    {
        if (!_hasRootKey)
            return p.Action is "install" or "download" or "notice" ? p with { Action = "notice", Url = null, ManifestUrl = null, Tag = null } : Nothing(p);
        return p.Action is "install" or "download" ? p with { Url = null } : Nothing(p);
    }

    /// <summary>Nothing this build may act on: no notice; below the minimum, the bar that can't be closed.</summary>
    private static CorePrompt Nothing(CorePrompt p) =>
        p with { Kind = p.Kind is "Forced" or "RequiredBar" ? "RequiredBar" : "None", Action = "none", Url = null, ManifestUrl = null, Tag = null };

    private async Task ActOnAsync(CorePrompt p, CancellationToken cancel)
    {
        if (_staged is { } staged)
        {
            // Still offered, and still trusted after whatever key list this answer brought (a revoked key, a yanked version)
            if (_offeredTag != staged.Tag || !CanSelfUpdate) Unstage();
            else if (await StillGoodAsync(cancel).ConfigureAwait(false))
            {
                // Closed for this version: it still installs as the app closes, without the bar
                if (p.Kind == "None") Publish(UpdateView.None);
                else Publish(new UpdateView(UpdateStage.Ready, staged.Version, p.NotesEn, p.NotesAr, p.MessageEn, p.MessageAr, Required: p.Kind == "Forced"));
                return;
            }
            else
            {
                Publish(p.Kind == "Forced" ? ViewFor(UpdateStage.RequiredSoon, p) : UpdateView.None);
                return;
            }
        }
        switch (p.Kind)
        {
            case "Forced":
                // The whole-window screen needs an update this build can really get (§3.7): verify its manifest first
                if (Installs(p) && await VerifiedAsync(p, cancel).ConfigureAwait(false) is null)
                {
                    Publish(ViewFor(UpdateStage.RequiredSoon, p));
                    return;
                }
                Publish(ViewFor(UpdateStage.Forced, p));
                return;
            case "RequiredBar":
                Publish(ViewFor(UpdateStage.RequiredSoon, p));
                return;
            case "Notice":
                if (Installs(p) && AutoInstall)
                {
                    await DownloadAndStageAsync(p, cancel).ConfigureAwait(false);
                    return;
                }
                Publish(ViewFor(UpdateStage.Available, p));
                return;
            default:
                Publish(UpdateView.None);
                return;
        }
    }

    private bool Installs(CorePrompt p) => p.Action == "install" && CanSelfUpdate;

    /// <summary>
    /// The bar that can't be closed names no version, notes or message: there is nothing to update to, or nothing this
    /// build may be told of (an unsigned release, a refused manifest). Core fills them in all the same.
    /// </summary>
    private UpdateView ViewFor(UpdateStage stage, CorePrompt p) =>
        stage == UpdateStage.RequiredSoon ? new(stage) :
        new(stage, p.Version, p.NotesEn, p.NotesAr, p.MessageEn, p.MessageAr, DownloadOnly: p.Action != "none" && !Installs(p), Required: stage == UpdateStage.Forced);

    /// <summary>The bar's Update button (or the forced screen's): install, or open the download page.</summary>
    public async Task<string?> UpdateNowAsync(CancellationToken cancel = default)
    {
        var p = _lastPrompt;
        if (p is null) return null;
        if (p.Action == "none") return null;
        if (!Installs(p)) return DownloadPage;
        await DownloadAndStageAsync(p, cancel).ConfigureAwait(false);
        return null;
    }

    /// <summary>Another copy of the app runs from the same install: the staged update waits until it has closed.</summary>
    public bool OtherCopyRunning => _otherCopy();

    /// <summary>
    /// Restart now: refused while work is running (the bar says "Finish or pause your uploads first.") or another copy is open.
    /// Then the app quits: the work stops, and the end (<see cref="OnExit"/>) starts the updater, which starts the app again.
    /// </summary>
    public async Task<bool> RestartNowAsync(CancellationToken cancel = default)
    {
        if (_view.Stage != UpdateStage.Ready || _busy() || _otherCopy()) return false;
        if (Interlocked.CompareExchange(ref _restarting, 1, 0) != 0) return false;
        if (!TryBeginApply())
        {
            NotRestarting();
            return false;
        }
        var goesOn = false;
        try
        {
            if (!await StillGoodAsync(cancel).ConfigureAwait(false))
            {
                Publish(_view.Required ? new UpdateView(UpdateStage.RequiredSoon, MessageEn: _view.MessageEn, MessageAr: _view.MessageAr) : UpdateView.None);
                return false;
            }
            // Once more, after the check of the file: work or a copy may have started meanwhile
            if (_busy() || _otherCopy()) return false;
            goesOn = true;
        }
        finally
        {
            // Before an end that waits for this check goes on: it is not to start the app again for a Restart now that was refused
            if (!goesOn) NotRestarting();
            EndApply();
        }
        var quitting = false;
        try
        {
            // A quit that came meanwhile may have handed it over already
            quitting = _quit is null ? OnExit() || _handedOver : await _quit().ConfigureAwait(false);
            return quitting;
        }
        finally
        {
            if (!quitting) NotRestarting();
        }
    }

    /// <summary>The app goes on, and can try again.</summary>
    private void NotRestarting() => Interlocked.Exchange(ref _restarting, 0);

    /// <summary>Only one hand-over to the updater at a time: a second click, or the exit during the first, finds it taken.</summary>
    private bool TryBeginApply(TimeSpan wait = default)
    {
        if (!_applying.Wait(wait)) return false;
        if (!_handedOver) return true;
        _applying.Release();
        return false;
    }

    /// <summary>The hand-over did not happen: it may be tried again.</summary>
    private void EndApply() => _applying.Release();

    /// <summary>The × on the bar: hidden for this version.</summary>
    public void Dismiss()
    {
        if (!_view.CanClose || _view.Version is null) return;
        _core.Dismiss(_view.Version);
        SaveState();
        Publish(UpdateView.None);
    }

    /// <summary>Restart now was pressed, and the app is to be open again when all is done.</summary>
    public bool RestartAsked => _restarting == 1;

    /// <summary>How long the end waits for a check of the staged file that is under way; well inside what it has for the hand-over</summary>
    private static readonly TimeSpan CheckWait = TimeSpan.FromSeconds(6);

    /// <summary>The work has ended: the staged update is handed to the updater, which installs it as the app goes. False: nothing was.</summary>
    public bool OnExit()
    {
        // Restart now may be checking the file this moment: it is waited for
        if (_staged is null || _otherCopy() || !TryBeginApply(CheckWait)) return false;
        try
        {
            // A copy that started during the check counts: the update is staged again at the next start
            if (!StillGoodAsync(CancellationToken.None, report: false).GetAwaiter().GetResult() || _otherCopy()) return false;
            if (_restarting == 1) _installer.ApplyAndRestart();
            else _installer.ApplyOnExit();
            // Not when that threw: the updater did not start
            _handedOver = true;
            return true;
        }
        finally
        {
            EndApply();
        }
    }

    /// <summary>
    /// The staged update, checked again from the start: its manifest's signature under the keys trusted now, and the
    /// file on disk. If either fails it is dropped and (unless the app is going) reported.
    /// </summary>
    private async Task<bool> StillGoodAsync(CancellationToken cancel, bool report = true)
    {
        if (_staged is not { } staged) return false;
        try
        {
            Verify(staged.Manifest, staged.Signature, staged.Tag);
            _installer.VerifyStaged();
            return true;
        }
        catch (UpdateVerificationException ex)
        {
            Unstage();
            if (report) await ReportFailureAsync(staged.Tag, ex.Reason, cancel).ConfigureAwait(false);
            return false;
        }
    }

    private void Unstage()
    {
        _staged = null;
        _installer.Discard();
    }

    /// <summary>The release the answer names, its signature checked; null when refused (and reported) or unreachable.</summary>
    private async Task<CoreRelease?> VerifiedAsync(CorePrompt p, CancellationToken cancel)
    {
        if (p.ManifestUrl is null || p.Tag is null) return null;
        try
        {
            return (await FetchReleaseAsync(p.ManifestUrl, p.Tag, cancel).ConfigureAwait(false)).Release;
        }
        catch (UpdateVerificationException ex)
        {
            await ReportFailureAsync(p.Tag, ex.Reason, cancel).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>The update state is saved after every check of a manifest, before anything is done with what it says.</summary>
    private CoreRelease Verify(byte[] manifest, string signature, string tag)
    {
        var release = _core.VerifyManifest(manifest, signature, tag);
        SaveState();
        return release;
    }

    private async Task<(CoreRelease Release, Staged Proof)> FetchReleaseAsync(string manifestUrl, string tag, CancellationToken cancel)
    {
        var manifest = await FetchAsync(manifestUrl, 256 * 1024, null, cancel).ConfigureAwait(false);
        var signature = Encoding.UTF8.GetString(await FetchAsync(manifestUrl + ".sig", 4096, null, cancel).ConfigureAwait(false));
        var release = Verify(manifest, signature, tag);
        return (release, new Staged(release.Version, tag, manifest, signature));
    }

    private async Task DownloadAndStageAsync(CorePrompt p, CancellationToken cancel)
    {
        if (p.ManifestUrl is null || p.Tag is null || p.Version is null) return;
        var required = p.Kind == "Forced";
        Publish(new UpdateView(UpdateStage.Downloading, p.Version, p.NotesEn, p.NotesAr, p.MessageEn, p.MessageAr, Progress: 0, Required: required));
        string reason;
        try
        {
            var (release, proof) = await FetchReleaseAsync(p.ManifestUrl, p.Tag, cancel).ConfigureAwait(false);
            // The signed manifest names the version, not the answer
            if (release.Version != p.Version) throw new UpdateVerificationException("wrong_tag");
            var artifact = release.Artifacts.FirstOrDefault(a => a.App == "tools" && a.Platform == _platform && a.Kind == "velopack-full" && (a.Arch is null || a.Arch == _arch || a.Arch == "universal"))
                ?? throw new UpdateVerificationException("bad_manifest");
            Directory.CreateDirectory(_downloadDir);
            var path = Path.Combine(_downloadDir, $"tools-{release.Version}-{_arch}.nupkg");
            await DownloadVerifiedAsync(artifact, path, cancel).ConfigureAwait(false);
            try
            {
                await _installer.StageAsync(path, release, artifact, cancel).ConfigureAwait(false);
                _installer.VerifyStaged();
            }
            catch (Exception ex) when (ex is not (UpdateVerificationException or OperationCanceledException))
            {
                // The installer couldn't take a verified package (disk, permissions): not a refusal, and never a stuck bar
                throw new IOException("the installer could not take the update", ex);
            }
            _staged = proof;
            Publish(new UpdateView(UpdateStage.Ready, release.Version, release.NotesEn, release.NotesAr, p.MessageEn, p.MessageAr, Required: required));
            return;
        }
        catch (UpdateVerificationException ex)
        {
            reason = ex.Reason;
            Unstage();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Network trouble is not a verification failure: try again at the next check (the forced screen stays, to retry)
            Publish(required ? ViewFor(UpdateStage.Forced, p) : UpdateView.None);
            return;
        }
        // Fail closed (wiki §5.3 step 6): nothing installed, no link offered, the server is told. Below the minimum the
        // app stays usable behind the bar that can't be closed.
        Publish(required ? ViewFor(UpdateStage.RequiredSoon, p) : UpdateView.None);
        await ReportFailureAsync(p.Tag, reason, cancel).ConfigureAwait(false);
    }

    /// <summary>GET with redirects followed by hand: every hop must be an allowed host, over HTTPS.</summary>
    private async Task<byte[]> FetchAsync(string url, long max, Action<double>? progress, CancellationToken cancel)
    {
        using var stream = new MemoryStream();
        await StreamAsync(url, stream, max, progress, cancel).ConfigureAwait(false);
        return stream.ToArray();
    }

    private async Task StreamAsync(string url, Stream into, long max, Action<double>? progress, CancellationToken cancel)
    {
        using var http = new HttpClient(_handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(30) };
        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!_core.AllowedDownloadUrl(current)) throw new UpdateVerificationException("host");
            using var response = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next)
            {
                current = (next.IsAbsoluteUri ? next : new Uri(new Uri(current), next)).ToString();
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
            var total = response.Content.Headers.ContentLength;
            if (total > max) throw new UpdateVerificationException("hash");
            await using var body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                read += n;
                if (read > max) throw new UpdateVerificationException("hash");
                await into.WriteAsync(buffer.AsMemory(0, n), cancel).ConfigureAwait(false);
                if (total is > 0) progress?.Invoke((double)read / total.Value);
            }
            return;
        }
        throw new UpdateVerificationException("host");
    }

    private async Task DownloadVerifiedAsync(CoreArtifact artifact, string path, CancellationToken cancel)
    {
        var temp = path + ".part";
        try
        {
            await using (var file = File.Create(temp))
            {
                await StreamAsync(artifact.Url, file, (long)artifact.Size, f => Publish(_view with { Progress = f }), cancel).ConfigureAwait(false);
            }
            var info = new FileInfo(temp);
            byte[] hash;
            await using (var check = File.OpenRead(temp))
            {
                hash = await SHA256.HashDataAsync(check, cancel).ConfigureAwait(false);
            }
            if (info.Length != (long)artifact.Size || !CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(artifact.Sha256)))
                throw new UpdateVerificationException("hash");
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private async Task ReportFailureAsync(string tag, string reason, CancellationToken cancel)
    {
        try
        {
            using var http = new HttpClient(_handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) };
            var body = JsonSerializer.Serialize(new { app = "tools", platform = _platform, version = _installedVersion, repo = Repo, tag, reason });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            await http.PostAsync(new Uri(_videosApi(), "/api/v1/app-config/update-failure"), content, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The report is best effort; the next check tries the update again and reports again
        }
    }

    private void Reschedule(TimeSpan after) => _timer?.Change(after, Timeout.InfiniteTimeSpan);

    private void SaveState()
    {
        try
        {
            AtomicFile.Write(_statePath, _core.StateJson());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved: the next run starts from the key list the build carries, which is still safe
        }
    }

    private void Publish(UpdateView view)
    {
        _view = view;
        Changed?.Invoke(view);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _gate.Dispose();
    }

    public static string DefaultStatePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNow", "Tools", "updates.json");

    public static string DefaultDownloadDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AlUmmahNow", "Tools", "updates");

    /// <summary>The handler the app uses: HTTPS through the OS stack, redirects left to <see cref="UpdateService"/>.</summary>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
}
