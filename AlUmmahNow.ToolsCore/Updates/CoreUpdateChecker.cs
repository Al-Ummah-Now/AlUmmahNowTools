using AlUmmahNow.Core;

namespace AlUmmahNow.ToolsCore.Updates;

/// <summary>
/// The trust anchors this build carries (wiki AUN-APP-UPDATES §3.5): the repo's offline root public keys and the
/// root-signed key list current at build time. Empty until the owner provides the Tools root key (K-R); with no root
/// key the app trusts no release and shows no update notice (fail closed, §5.5).
/// </summary>
public static class ToolsUpdateKeys
{
    /// <summary>Base64 raw ed25519 root public keys (not secret); the release workflow never changes these.</summary>
    public static readonly string[] RootKeys = Array.Empty<string>();

    /// <summary>The key list and its root signature as of this build, or null (written by the release workflow).</summary>
    public static string? BundledKeyList => Read("update-keys.json");
    public static string? BundledKeyListSig => Read("update-keys.json.sig");

    private static string? Read(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}

/// <summary><see cref="IUpdateCore"/> over aun-core's <c>UpdateChecker</c> (the rules live in aun-core's aun-updates).</summary>
public sealed class CoreUpdateChecker : IUpdateCore, IDisposable
{
    private readonly UpdateChecker _checker;

    public CoreUpdateChecker(string installedVersion, string? stateJson)
    {
        string? list = ToolsUpdateKeys.BundledKeyList, sig = ToolsUpdateKeys.BundledKeyListSig;
        try
        {
            _checker = new UpdateChecker(UpdateService.Repo, ToolsUpdateKeys.RootKeys, installedVersion, false, stateJson, list, sig);
        }
        catch (AunException)
        {
            // A bundled list that doesn't verify is ignored rather than stopping the app: nothing is trusted then
            _checker = new UpdateChecker(UpdateService.Repo, ToolsUpdateKeys.RootKeys, installedVersion, false, stateJson, null, null);
        }
    }

    public static CoreUpdateChecker Load(string installedVersion, string statePath) =>
        new(installedVersion, File.Exists(statePath) ? SafeRead(statePath) : null);

    private static string? SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public CorePrompt ApplyAnswer(string updateJson, bool? deviceHasUpdate)
    {
        try
        {
            var p = _checker.ApplyAnswer(updateJson, deviceHasUpdate);
            return new CorePrompt(p.Kind.ToString(), p.Version, p.NotesEn, p.NotesAr, p.MessageEn, p.MessageAr, p.Action, p.Url, p.ManifestUrl, p.Tag, p.CheckHours);
        }
        catch (AunException ex)
        {
            throw new InvalidOperationException("unreadable app-config answer", ex);
        }
    }

    public CoreRelease VerifyManifest(byte[] manifest, string signature, string tag)
    {
        var check = _checker.VerifyManifest(manifest, signature, tag);
        if (check.Refused is not null || check.Release is null) throw new UpdateVerificationException(check.Refused ?? "bad_manifest");
        var r = check.Release;
        return new CoreRelease(r.Version, r.Tag, r.NotesEn, r.NotesAr,
            r.Artifacts.Select(a => new CoreArtifact(a.App, a.Platform, a.Arch, a.Kind, a.Url, a.Sha256, a.Size)).ToList());
    }

    public bool AllowedDownloadUrl(string url) => _checker.AllowedDownloadUrl(url);
    public void Dismiss(string version) => _checker.Dismiss(version);

    public bool? AutoInstall
    {
        get => _checker.AutoInstall();
        set => _checker.SetAutoInstall(value);
    }

    public void MarkChecked(long atMs) => _checker.MarkChecked(atMs);
    public long? LastCheckMs => _checker.LastCheckMs();
    public string OfflinePromptKind() => _checker.OfflinePromptKind().ToString();
    public string StateJson() => _checker.StateJson();
    public void Dispose() => _checker.Dispose();
}
