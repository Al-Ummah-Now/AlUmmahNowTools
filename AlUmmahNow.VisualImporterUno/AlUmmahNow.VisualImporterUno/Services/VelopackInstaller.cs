#if !WINDOWS
using System.Security.Cryptography;
using AlUmmahNow.ToolsCore.Updates;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace AlUmmahNow.VisualImporterUno.Services;

/// <summary>
/// Installs an update that <see cref="UpdateService"/> has already downloaded and verified (wiki AUN-APP-UPDATES §5.2,
/// §5.3). Velopack never downloads anything itself here: its feed is built from the verified manifest and its
/// "download" copies the verified file, so Velopack's own hash check runs on the bytes we checked. The package folder is
/// cleared at start and before staging, and the staged file is checked again right before Velopack's updater gets it.
/// </summary>
public sealed class VelopackInstaller : IUpdateInstaller
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _staged;
    private StagedPackage? _package;

    public VelopackInstaller()
    {
        try
        {
            var manager = new UpdateManager(new VerifiedFileSource());
            // Only a build installed by Velopack's own installer can update itself; the MSI, the zip, the .deb and
            // the .tar.gz can't (they get the notice with a download link instead).
            _manager = manager.IsInstalled ? manager : null;
            // Nothing an earlier run left is ever installed
            if (_manager is not null && VelopackLocator.Current.PackagesDir is { } left) StagedPackage.Clear(left);
        }
        catch (Exception)
        {
            _manager = null;
        }
    }

    public bool CanSelfUpdate => _manager is not null;

    public async Task StageAsync(string packagePath, CoreRelease release, CoreArtifact artifact, CancellationToken cancel)
    {
        if (_manager is null) throw new InvalidOperationException("not a self-updating install");
        var folder = VelopackLocator.Current.PackagesDir ?? throw new IOException("no package folder");
        var package = StagedPackage.Prepare(folder, VerifiedFileSource.PackageFile(VelopackLocator.Current.AppId!, release.Version), artifact);
        VerifiedFileSource.Offer(packagePath, release.Version, artifact);
        var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false)
            ?? throw new UpdateVerificationException("bad_manifest");
        await _manager.DownloadUpdatesAsync(info, cancelToken: cancel).ConfigureAwait(false);
        package.Verify();
        _package = package;
        _staged = info;
    }

    public void VerifyStaged()
    {
        if (_package is null || _staged is null) throw new UpdateVerificationException("hash");
        _package.Verify();
    }

    public void Discard()
    {
        _package?.Discard();
        _package = null;
        _staged = null;
    }

    /// <summary>
    /// Called as the app exits, never earlier: Velopack's updater gives up 60 seconds after it starts waiting. If it
    /// can't run, the next start clears the package and the next check stages it again.
    /// </summary>
    public void ApplyOnExit()
    {
        if (_manager is null || _staged is null) return;
        _manager.WaitExitThenApplyUpdates(_staged.TargetFullRelease, silent: true, restart: false);
    }

    public void ApplyAndRestart()
    {
        if (_manager is null || _staged is null) return;
        _manager.ApplyUpdatesAndRestart(_staged.TargetFullRelease);
    }

    /// <summary>Velopack's update source: one release, the file <see cref="UpdateService"/> verified.</summary>
    private sealed class VerifiedFileSource : IUpdateSource
    {
        private static string? _path;
        private static string? _version;
        private static CoreArtifact? _artifact;

        public static void Offer(string path, string version, CoreArtifact artifact)
        {
            _path = path;
            _version = version;
            _artifact = artifact;
        }

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            if (_path is null || _version is null || _artifact is null || appId is null) return Task.FromResult(new VelopackAssetFeed { Assets = Array.Empty<VelopackAsset>() });
            using var file = File.OpenRead(_path);
            var sha1 = Convert.ToHexString(SHA1.HashData(file));
            var asset = new VelopackAsset
            {
                PackageId = appId,
                Version = Velopack.SemanticVersion.Parse(PackVersion(_version)),
                Type = VelopackAssetType.Full,
                FileName = PackageFile(appId, _version),
                SHA1 = sha1,
                // Velopack compares upper-case hex, letter by letter; the manifest's is lower case
                SHA256 = _artifact.Sha256.ToUpperInvariant(),
                Size = (long)_artifact.Size,
            };
            return Task.FromResult(new VelopackAssetFeed { Assets = new[] { asset } });
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
        {
            if (_path is null || _version is null || !string.Equals(PackageFile(releaseEntry.PackageId, _version), releaseEntry.FileName, StringComparison.Ordinal)) throw new InvalidOperationException("not the verified file");
            File.Copy(_path, localFile, overwrite: true);
            progress(100);
            return Task.CompletedTask;
        }

        /// <summary>The name Velopack itself gives a full package.</summary>
        internal static string PackageFile(string appId, string version) => $"{appId}-{PackVersion(version)}-full.nupkg";

        /// <summary>
        /// The Velopack package version for a Tools version (docs/RELEASE.md, the same rule as the MSI): YYYY.MM.DD[.N]
        /// becomes YYYY.M.(D×100+N), N = 1 when absent, so 2026.11.22 → 2026.11.2201 and 2026.11.22.2 → 2026.11.2202.
        /// </summary>
        public static string PackVersion(string version)
        {
            var p = version.Split('.');
            var n = p.Length > 3 ? int.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) : 1;
            return $"{int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture)}.{int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)}.{int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) * 100 + n}";
        }
    }
}
#endif
