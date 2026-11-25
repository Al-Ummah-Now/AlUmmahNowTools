using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>"Encode on this computer" turned off and on, over the files already in the queue (owner, 2026-11-25).</summary>
public sealed class EncodeChoiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Temp.Folder(), "queue.json");
    private readonly ScriptedStep _encode;
    private readonly ScriptedStep _upload;
    private readonly ImportQueue _queue;
    private Func<CancellationToken, Task<StepResult>> _encodes = async ct => { await Task.Delay(Timeout.Infinite, ct); return StepResult.Done; };
    private Func<CancellationToken, Task<StepResult>> _uploads = _ => Task.FromResult(StepResult.Done);

    public EncodeChoiceTests()
    {
        // As the real step: a file left to the server is nothing to do
        _encode = new ScriptedStep(ImportStepKind.Encode, (item, _, ct) => item.ServerEncodes ? Task.FromResult(StepResult.Skipped) : _encodes(ct));
        _upload = new ScriptedStep(ImportStepKind.Upload, (_, _, ct) => _uploads(ct));
        _queue = Open();
        _queue.Settings.MaxAttempts = 2;
    }

    private ImportQueue Open() => new(_path, ImportQueueTests.All(_encode, _upload), backoff: _ => TimeSpan.FromHours(1));

    public void Dispose() => _queue.Dispose();

    private QueueItem Add(string name = "a.mp4", bool serverEncodes = false)
    {
        var video = new ScannedVideo(Path.Combine(Path.GetTempPath(), name), 10, 0, "video/mp4", Array.Empty<CaptionFile>(), null, null, null, new ImportMetadata { FallbackTitle = name }, Array.Empty<ScanNote>());
        var item = QueueItem.For(video, "b1", "ch1", DateTimeOffset.UtcNow, serverEncodes);
        _queue.Add(new[] { item });
        return item;
    }

    private static StepState Step(QueueItem item, ImportStepKind kind) => item.Steps.Single(s => s.Kind == kind);

    private Task Until(Func<bool> done) => Temp.WaitUntil(() =>
    {
        _queue.Pump();
        return done();
    });

    [Fact]
    public async Task Off_every_file_that_is_not_uploaded_skips_the_encode()
    {
        var items = new[] { Add("a.mp4"), Add("b.mp4"), Add("c.mp4", serverEncodes: true) };
        Assert.Equal(2, _queue.SetServerEncodes(true));
        await Until(() => items.All(i => i.Phase == ItemPhase.Done));
        Assert.All(items, i => Assert.Equal(StepStatus.Skipped, Step(i, ImportStepKind.Encode).Status));
        Assert.Equal(3, _upload.Runs);
    }

    [Fact]
    public async Task Off_a_running_encode_stops_and_the_file_goes_on_with_its_upload()
    {
        var item = Add();
        await Until(() => _encode.Running == 1);
        Assert.Equal(1, _queue.SetServerEncodes(true));
        await Until(() => item.Phase == ItemPhase.Done);
        Assert.Equal(StepStatus.Skipped, Step(item, ImportStepKind.Encode).Status);
        Assert.Equal(1, Step(item, ImportStepKind.Encode).Attempts);
        Assert.Equal(1, _upload.Runs);
        Assert.Contains(_queue.LogLines(item.Id), l => l.Code == "stopped");
    }

    [Fact]
    public async Task Off_an_encode_waiting_to_try_again_or_given_up_goes_on_at_once()
    {
        _encodes = _ => throw new ImportApiException(503, "internal_error", "down");
        var waiting = Add();
        await Until(() => waiting.Phase == ItemPhase.RetryWaiting);
        _queue.SetServerEncodes(true);
        await Until(() => waiting.Phase == ItemPhase.Done);

        var failed = Add("b.mp4");
        _queue.Settings.MaxAttempts = 1;
        await Until(() => failed.Phase == ItemPhase.Failed);
        Assert.Equal(1, _queue.SetServerEncodes(true));
        await Until(() => failed.Phase == ItemPhase.Done);
    }

    [Fact]
    public async Task A_failure_of_the_files_own_stays()
    {
        _encodes = _ => throw new StepFailedException("no_video", "The file has sound and no video", retryable: false);
        var item = Add();
        await Until(() => item.Phase == ItemPhase.Failed);
        Assert.Equal(0, _queue.SetServerEncodes(true));
        Assert.Equal(ItemPhase.Failed, item.Phase);
    }

    [Fact]
    public async Task What_is_encoded_or_uploading_or_done_is_not_changed()
    {
        _encodes = _ => Task.FromResult(StepResult.Done);
        _uploads = async ct => { await Task.Delay(Timeout.Infinite, ct); return StepResult.Done; };
        var uploading = Add();
        await Until(() => _upload.Running == 1);
        Assert.Equal(0, _queue.SetServerEncodes(true));
        Assert.Equal(1, _upload.Running);
        Assert.False(uploading.ServerEncodes);
        Assert.False(uploading.CanLeaveToServer);
    }

    [Fact]
    public async Task On_again_the_files_not_uploaded_are_encoded_here()
    {
        _encodes = _ => Task.FromResult(StepResult.Done);
        var item = Add(serverEncodes: true);
        _uploads = async ct => { await Task.Delay(Timeout.Infinite, ct); return StepResult.Done; };
        await Until(() => _upload.Running == 1);
        // Its upload runs: as it is
        Assert.Equal(0, _queue.SetServerEncodes(false));
        _queue.Pause(item.Id);
        await Until(() => _upload.Running == 0 && Step(item, ImportStepKind.Upload).Status == StepStatus.Pending);
        Assert.Equal(1, _queue.SetServerEncodes(false));
        Assert.Equal(ItemPhase.Paused, item.Phase);
        _uploads = _ => Task.FromResult(StepResult.Done);
        _queue.Resume(item.Id);
        await Until(() => item.Phase == ItemPhase.Done);
        Assert.Equal(StepStatus.Done, Step(item, ImportStepKind.Encode).Status);
        Assert.Contains(_queue.LogLines(item.Id), l => l.Code == "encode_here_chosen");
    }

    [Fact]
    public async Task One_file_alone_is_left_to_the_server()
    {
        var (first, second) = (Add("a.mp4"), Add("b.mp4"));
        _queue.Settings.Parallel[ImportStepKind.Encode] = 2;
        await Until(() => _encode.Running == 2);
        Assert.True(first.CanLeaveToServer);
        Assert.Equal(1, _queue.SetServerEncodes(true, first.Id));
        await Until(() => first.Phase == ItemPhase.Done);
        Assert.Equal(1, _encode.Running);
        Assert.False(second.ServerEncodes);
        Assert.Equal(0, _queue.SetServerEncodes(true, first.Id));
    }

    [Fact]
    public async Task A_paused_file_stays_paused_and_the_choice_is_kept_over_a_restart()
    {
        var item = Add();
        _queue.Pause(item.Id);
        Assert.Equal(1, _queue.SetServerEncodes(true));
        for (var i = 0; i < 5; i++)
        {
            _queue.Pump();
            await Task.Delay(10);
        }
        Assert.Equal(ItemPhase.Paused, item.Phase);
        using var again = Open();
        Assert.True(Assert.Single(again.Items).ServerEncodes);
    }

    [Fact]
    public void The_switch_is_remembered_and_on_until_it_is_turned_off()
    {
        var path = Path.Combine(Temp.Folder(), "preferences.json");
        Assert.True(ToolsPreferences.Load(path).EncodeHere);
        File.WriteAllText(path, "{\"Language\":\"ar\"}");
        Assert.True(ToolsPreferences.Load(path).EncodeHere);
        new ToolsPreferences { EncodeHere = false }.Save(path);
        Assert.False(ToolsPreferences.Load(path).EncodeHere);
    }
}
