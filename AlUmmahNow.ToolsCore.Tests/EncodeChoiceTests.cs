using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>"Encode on this computer" turned off and on, over the files already in the queue (owner, 2026-11-25).</summary>
public sealed class EncodeChoiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Temp.Folder(), "queue.json");
    private readonly ScriptedStep _encode;
    private readonly ScriptedStep _upload;
    private readonly ScriptedStep _hash;
    private readonly ImportQueue _queue;
    private Func<CancellationToken, Task<StepResult>> _encodes = async ct => { await Task.Delay(Timeout.Infinite, ct); return StepResult.Done; };
    private Func<CancellationToken, Task<StepResult>> _uploads = _ => Task.FromResult(StepResult.Done);
    private Func<Task<StepResult>> _hashes = () => Task.FromResult(StepResult.Done);
    /// <summary>Just before a run that leaves the file to the server ends</summary>
    private Action? _leaving;
    /// <summary>What each upload began with: the choice, and whether it has renditions to send</summary>
    private readonly List<(bool Server, bool Renditions)> _began = new();

    public EncodeChoiceTests()
    {
        // As the real step: a file left to the server is nothing to do
        _encode = new ScriptedStep(ImportStepKind.Encode, async (item, _, ct) =>
        {
            if (item.EncodeRun!.Value)
            {
                item.Renditions = null;
                var leaving = _leaving;
                _leaving = null;
                leaving?.Invoke();
                return StepResult.Skipped;
            }
            if (item.Renditions is not null) return StepResult.Done;
            var result = await _encodes(ct);
            if (result == StepResult.Done) item.Renditions = new RenditionSet { Directory = Path.Combine(Path.GetTempPath(), item.Id) };
            return result;
        });
        _upload = new ScriptedStep(ImportStepKind.Upload, (item, _, ct) =>
        {
            lock (_began) _began.Add((item.ServerEncodes, item.Renditions is not null));
            return _uploads(ct);
        });
        _hash = new ScriptedStep(ImportStepKind.Hash, (_, _, _) => _hashes());
        _queue = Open();
        _queue.Settings.MaxAttempts = 2;
    }

    private ImportQueue Open() => new(_path, ImportQueueTests.All(_hash, _encode, _upload), backoff: _ => TimeSpan.FromHours(1));

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
        _encodes = _ => Task.FromResult(StepResult.NoVideo);
        var item = Add();
        await Until(() => item.Phase == ItemPhase.NoVideo);
        Assert.Equal(0, _queue.SetServerEncodes(true));
        Assert.Equal(ItemPhase.NoVideo, item.Phase);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_upload_that_was_opened_never_goes_back_to_the_encode(bool paused)
    {
        var item = Add(serverEncodes: true);
        _uploads = async ct =>
        {
            item.UploadUrl = "https://videos.api.example/api/videos/tus/1";
            if (!paused) throw new ImportApiException(503, "internal_error", "down");
            await Task.Delay(Timeout.Infinite, ct);
            return StepResult.Done;
        };
        if (paused)
        {
            await Until(() => _upload.Running == 1);
            _queue.Pause(item.Id);
            await Until(() => _upload.Running == 0 && Step(item, ImportStepKind.Upload).Status == StepStatus.Pending);
        }
        else
        {
            await Until(() => item.Phase == ItemPhase.RetryWaiting);
        }
        Assert.Equal(0, _queue.SetServerEncodes(false));
        Assert.True(item.ServerEncodes);
        Assert.Equal(ImportStepKind.Upload, item.CurrentStep!.Kind);
        // Also after a restart, and in a queue saved by a version that did not note it
        using (var again = Open())
        {
            Assert.Equal(0, again.SetServerEncodes(false));
        }
        var saved = File.ReadAllText(_path);
        Assert.Contains("\"uploadOpened\":true", saved);
        saved = saved.Replace("\"uploadOpened\":true", "\"uploadOpened\":false");
        File.WriteAllText(_path, saved);
        using var older = Open();
        Assert.Equal(0, older.SetServerEncodes(false));
    }

    [Fact]
    public async Task Off_as_the_encode_finishes_nothing_made_here_is_sent()
    {
        var item = Add();
        var changed = -1;
        // Finished, and not yet noted as finished
        _encodes = _ =>
        {
            changed = _queue.SetServerEncodes(true);
            return Task.FromResult(StepResult.Done);
        };
        await Until(() => item.Phase == ItemPhase.Done);
        Assert.Equal(1, changed);
        Assert.True(item.ServerEncodes);
        Assert.Null(item.Renditions);
        Assert.Equal(StepStatus.Skipped, Step(item, ImportStepKind.Encode).Status);
        Assert.Equal((true, false), Assert.Single(_began));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Off_then_on_while_the_encode_is_stopping_it_is_encoded_here(bool finishesAllTheSame)
    {
        var item = Add();
        var stopping = new TaskCompletionSource();
        var runs = 0;
        _encodes = async ct =>
        {
            if (++runs > 1) return StepResult.Done;
            // As an ffmpeg that takes its time to end
            await stopping.Task;
            if (!finishesAllTheSame) ct.ThrowIfCancellationRequested();
            return StepResult.Done;
        };
        await Until(() => _encode.Running == 1);
        Assert.Equal(1, _queue.SetServerEncodes(true));
        Assert.Equal(1, _queue.SetServerEncodes(false));
        Assert.Equal(StepStatus.Running, Step(item, ImportStepKind.Encode).Status);
        stopping.SetResult();
        await Until(() => item.Phase == ItemPhase.Done);
        Assert.False(item.ServerEncodes);
        Assert.Equal(StepStatus.Done, Step(item, ImportStepKind.Encode).Status);
        Assert.Equal(finishesAllTheSame ? 1 : 2, runs);
        Assert.Equal((false, true), Assert.Single(_began));
    }

    [Fact]
    public async Task On_as_the_file_is_being_left_to_the_server_it_is_encoded_here()
    {
        var item = Add(serverEncodes: true);
        _encodes = _ => Task.FromResult(StepResult.Done);
        var changed = -1;
        _leaving = () => changed = _queue.SetServerEncodes(false);
        await Until(() => item.Phase == ItemPhase.Done);
        Assert.Equal(1, changed);
        Assert.Equal((false, true), Assert.Single(_began));
        Assert.Equal(StepStatus.Done, Step(item, ImportStepKind.Encode).Status);
    }

    [Fact]
    public async Task Off_as_the_encode_fails_the_file_goes_on()
    {
        var item = Add();
        _encodes = _ =>
        {
            _queue.SetServerEncodes(true);
            throw new ImportApiException(503, "internal_error", "down");
        };
        await Until(() => item.Phase == ItemPhase.Done);
        Assert.Equal((true, false), Assert.Single(_began));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Off_as_the_encode_finds_no_video_it_settles_locally_all_the_same(bool noVideo)
    {
        var item = Add();
        _encodes = _ =>
        {
            _queue.SetServerEncodes(true);
            if (noVideo) return Task.FromResult(StepResult.NoVideo);
            throw new StepFailedException("not_video", "Damaged video", retryable: false);
        };
        await Until(() => item.Phase == (noVideo ? ItemPhase.NoVideo : ItemPhase.Failed));
        Assert.Equal(noVideo ? StepStatus.Skipped : StepStatus.Failed, Step(item, ImportStepKind.Encode).Status);
        Assert.Equal(noVideo ? null : "not_video", Step(item, ImportStepKind.Encode).LastErrorCode);
        Assert.Empty(_began);
    }

    [Theory]
    [InlineData("no_video")]
    [InlineData("not_video")]
    [InlineData("file_missing")]
    [InlineData("file_changed")]
    public async Task A_file_refused_before_its_encode_is_neither_changed_nor_counted(string code)
    {
        _hashes = () => throw new StepFailedException(code, "Refused", retryable: false);
        var (item, server) = (Add(), Add("b.mp4", serverEncodes: true));
        var phase = code == "no_video" ? ItemPhase.NoVideo : ItemPhase.Failed;
        await Until(() => item.Phase == phase && server.Phase == phase);
        var log = _queue.LogLines(server.Id).Count;
        Assert.Equal(0, _queue.SetServerEncodes(true));
        Assert.Equal(0, _queue.SetServerEncodes(false));
        Assert.False(item.ServerEncodes);
        Assert.True(server.ServerEncodes);
        Assert.Equal(log, _queue.LogLines(server.Id).Count);
        Assert.False(item.CanLeaveToServer);
    }

    [Fact]
    public async Task Off_and_on_at_random_the_status_the_choice_and_what_is_sent_agree()
    {
        _queue.Settings.Parallel[ImportStepKind.Encode] = 4;
        _encodes = async ct =>
        {
            await Task.Delay(Random.Shared.Next(6), ct);
            return StepResult.Done;
        };
        var items = Enumerable.Range(0, 40).Select(i => Add($"{i}.mp4")).ToList();
        await Until(() =>
        {
            _queue.SetServerEncodes(Random.Shared.Next(2) == 0);
            return items.All(i => i.Phase == ItemPhase.Done);
        });
        Assert.All(_began, b => Assert.NotEqual(b.Server, b.Renditions));
        Assert.All(items, i => Assert.Equal(i.ServerEncodes ? StepStatus.Skipped : StepStatus.Done, Step(i, ImportStepKind.Encode).Status));
        Assert.Equal(items.Count, _began.Count);
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
