#if UPDATE_TEST
using System.Diagnostics;
using AlUmmahNow.ToolsCore.Import;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The update test's work (<c>TOOLS_UPDATE_TEST_BUSY=1</c>): a step that runs a child process and takes four seconds to stop, and
/// the test's own scanner (<c>TOOLS_UPDATE_TEST_SCANNER</c>), which ends only when it is killed. Compiled only with -p:UpdateTest=true.
/// </summary>
internal sealed class UpdateTestStep : IImportStep
{
    public static bool On => Environment.GetEnvironmentVariable("TOOLS_UPDATE_TEST_BUSY") == "1";

    public static volatile bool Running;

    public ImportStepKind Kind => ImportStepKind.Hash;

    public static QueueItem Item() =>
        QueueItem.For(new ScannedVideo("/update-test/a.mp4", 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = "a" }, Array.Empty<ScanNote>()), "b", "c", DateTimeOffset.UtcNow);

    /// <summary>The step runs, and the scanner answers.</summary>
    public static async Task WaitAsync()
    {
        _ = ImportWorkspace.Current;
        var scanner = App.GetService<Services.FileScanServiceManager>();
        for (var i = 0; i < 600 && !(Running && scanner.IsServiceManaged); i++) await Task.Delay(100);
        UpdateTestHandler.Log($"busy step={Running} scanner={scanner.IsServiceManaged}");
    }

    public async Task<StepResult> RunAsync(QueueItem item, IStepContext context, CancellationToken ct)
    {
        var command = Environment.GetEnvironmentVariable("TOOLS_UPDATE_TEST_STEP")!.Split('|');
        var start = new ProcessStartInfo(command[0]) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in command.Skip(1)) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        Running = true;
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
        }
        await Task.Delay(4000, CancellationToken.None);
        child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync(CancellationToken.None);
        UpdateTestHandler.Log("step stopped");
        throw new OperationCanceledException(ct);
    }
}
#endif
