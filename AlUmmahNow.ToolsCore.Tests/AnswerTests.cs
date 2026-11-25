using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Import.Sources;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>What was found out about a file is told on its row, without a reference to a report nobody gets.</summary>
public sealed class AnswerTests
{
    private sealed class Sink : IImportFailureSink
    {
        public readonly List<string> References = new();
        public Task ItemFailedAsync(QueueItem item, StepState step, Exception error, string reference, CancellationToken ct)
        {
            lock (References) References.Add(reference);
            return Task.CompletedTask;
        }
    }

    public static TheoryData<Exception, bool> Failures() => new()
    {
        { new StepFailedException("no_video", "The file has sound and no video", retryable: false), false },
        { new SourceReadException(ReadFailure.Of(new FileNotFoundException("gone"))), false },
        { new StepFailedException("not_hashed", "The file has not been checked yet", retryable: false), true },
        // The app's own work missing is the app's failure
        { new FileNotFoundException("a rendition"), true },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task The_log_and_the_sink_have_a_reference_only_when_there_is_a_report(Exception failure, bool reference)
    {
        var sink = new Sink();
        var hash = new ScriptedStep(ImportStepKind.Hash, (_, _, _) => throw failure);
        using var queue = new ImportQueue(Path.Combine(Temp.Folder(), "queue.json"), ImportQueueTests.All(hash), sink);
        queue.Settings.MaxAttempts = 1;
        var video = new ScannedVideo(Path.Combine(Path.GetTempPath(), "a.mp4"), 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = "a" }, Array.Empty<ScanNote>());
        var item = QueueItem.For(video, "b1", "ch1", DateTimeOffset.UtcNow);
        queue.Add(new[] { item });
        await Temp.WaitUntil(() =>
        {
            queue.Pump();
            lock (sink.References) return sink.References.Count == 1;
        });
        Assert.Equal(reference, sink.References[0].Length > 0);
        var line = queue.LogLines(item.Id).Single(l => l.Code == "failed");
        Assert.Equal(sink.References[0], line.Args![3]);
        Assert.Equal(reference, line.Text.Contains("(reference ", StringComparison.Ordinal));
    }
}
