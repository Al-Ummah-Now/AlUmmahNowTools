using AlUmmahNow.ToolsCore.Updates;

namespace AlUmmahNow.VisualImporterUno;

/// <summary>A build that can't update itself (the WinAppSDK head is not released): notices only, with a download link.</summary>
public sealed class NoSelfUpdate : IUpdateInstaller
{
    public bool CanSelfUpdate => false;
    public Task StageAsync(string packagePath, CoreRelease release, CoreArtifact artifact, CancellationToken cancel) => Task.CompletedTask;
    public void ApplyOnExit() { }
    public void ApplyAndRestart() { }
}
