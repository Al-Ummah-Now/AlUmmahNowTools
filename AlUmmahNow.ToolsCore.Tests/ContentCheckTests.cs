using System.Buffers.Binary;
using System.Text;
using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.ToolsCore.Import.Sources;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>What an MP4's own index says, from its boxes' headers.</summary>
public sealed class Mp4IndexTests
{
    internal static byte[] Box(string type, params byte[][] content)
    {
        var body = content.SelectMany(c => c).ToArray();
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    private static byte[] Wide(string type, byte[] body)
    {
        var box = new byte[16 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        body.CopyTo(box, 16);
        return box;
    }

    private static byte[] Track(string handler) => Box("trak", Box("tkhd", new byte[84]), Box("mdia", Box("mdhd", new byte[24]), Box("hdlr", new byte[8], Encoding.ASCII.GetBytes(handler), new byte[13])));

    internal static byte[] File(params string[] handlers) =>
        [.. Box("ftyp", "isom"u8.ToArray(), new byte[4]), .. Box("mdat", Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray()), .. Moov(handlers)];

    private static byte[] Moov(params string[] handlers) => Box("moov", [Box("mvhd", new byte[100]), .. handlers.Select(Track), Box("udta", Box("meta", new byte[30]))]);

    private static Task<VideoContent> Read(byte[] file) => Mp4Index.ReadAsync(new MemoryStream(file), file.Length, default);

    [Fact]
    public async Task A_video_track_is_found_before_or_after_the_sound_and_the_data()
    {
        Assert.Equal(VideoContent.Video, await Read(File("vide", "soun")));
        Assert.Equal(VideoContent.Video, await Read(File("soun", "vide")));
        Assert.Equal(VideoContent.Video, await Read([.. Box("ftyp", new byte[8]), .. Moov("vide"), .. Box("mdat", new byte[100])]));
        Assert.Equal(VideoContent.Video, await Read([.. Box("ftyp", new byte[8]), .. Wide("mdat", new byte[100]), .. Moov("vide")]));
        // A moov to the end of the file
        var open = File("vide");
        BinaryPrimitives.WriteUInt32BigEndian(open.AsSpan(open.Length - Moov("vide").Length), 0);
        Assert.Equal(VideoContent.Video, await Read(open));
    }

    [Fact]
    public async Task Sound_alone_is_no_video()
    {
        Assert.Equal(VideoContent.NoVideo, await Read(File("soun")));
        Assert.Equal(VideoContent.NoVideo, await Read(File("soun", "text")));
        Assert.Equal(VideoContent.Damaged, await Read(File()));
        Assert.Equal(VideoContent.Damaged, await Read(File("text")));
    }

    [Fact]
    public async Task A_file_cut_off_anywhere_is_damaged()
    {
        var whole = File("vide", "soun");
        for (var length = 9; length < whole.Length; length += 37) Assert.Equal(VideoContent.Damaged, await Read(whole[..length]));
    }

    [Fact]
    public async Task Reserved_and_never_filled_is_damaged()
    {
        Assert.Equal(VideoContent.Damaged, await Read(new byte[10_000]));
        Assert.Equal(VideoContent.Damaged, await Read(new byte[3]));
        // The start arrived, the rest is still empty
        var partly = File("vide");
        partly.AsSpan(1500).Clear();
        Assert.Equal(VideoContent.Damaged, await Read(partly));
    }

    [Fact]
    public async Task Other_kinds_of_files_are_not_judged()
    {
        Assert.Equal(VideoContent.Unknown, await Read([0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Equal(VideoContent.Unknown, await Read("RIFF....AVI LIST"u8.ToArray()));
        Assert.Equal(VideoContent.Unknown, await Read([1, 2, 3]));
    }

    [Fact]
    public async Task Sizes_that_make_no_sense_end_the_reading()
    {
        var bad = File("vide");
        foreach (var size in new uint[] { 1, 2, 7, 0xFFFFFFFF })
        {
            BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(16), size);
            Assert.Equal(VideoContent.Damaged, await Read(bad));
        }
        // A track larger than the index that holds it
        var track = File("vide");
        BinaryPrimitives.WriteUInt32BigEndian(track.AsSpan(track.Length - Moov("vide").Length + 8 + 108), 1_000_000);
        Assert.Equal(VideoContent.Damaged, await Read(track));
    }
}

/// <summary>Only a file with a video in it is hashed and sent, and only what was checked is sent whole.</summary>
public sealed class ContentCheckTests
{
    private static QueueItem Item(byte[] bytes, string name = "lesson.mp4")
    {
        var dir = Temp.Folder();
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
        var item = QueueItem.For(FolderScanner.Scan(dir, false, null).Videos[0], "b1", "ch1", DateTimeOffset.UtcNow);
        item.Sha256 = Temp.Sha(item.Video.Path);
        item.FingerprintHash = new string('c', 64);
        return item;
    }

    private static UploadStep Step(FakeServer server, IMediaTools? media = null) => new(
        new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), server),
        new TusClient(server),
        new Uri("https://videos.api.example"),
        _ => Task.FromResult<string?>("tok"),
        media: media);

    private static async Task<string> Refused(Func<Task> work, bool retryable = false)
    {
        var failure = await Assert.ThrowsAsync<StepFailedException>(work);
        Assert.Equal(retryable, failure.Retryable);
        return failure.Code;
    }

    /// <summary>The file's bytes change, its size and time do not.</summary>
    private static void Change(QueueItem item, int at)
    {
        var bytes = File.ReadAllBytes(item.Video.Path);
        bytes[at] ^= 1;
        var time = File.GetLastWriteTimeUtc(item.Video.Path);
        File.WriteAllBytes(item.Video.Path, bytes);
        File.SetLastWriteTimeUtc(item.Video.Path, time);
    }

    [Theory]
    [InlineData("soun", "no_video")]
    [InlineData(null, "not_video")]
    public async Task A_file_without_a_video_is_neither_hashed_nor_sent(string? track, string code)
    {
        var item = Item(track is null ? Mp4IndexTests.File("vide")[..2000] : Mp4IndexTests.File(track));
        var server = new FakeServer();
        item.Sha256 = null;
        Assert.Equal(code, await Refused(() => new HashStep().RunAsync(item, new RecordingContext(), default)));
        Assert.Null(item.Sha256);
        // Hashed by a version that did not check
        item.Sha256 = Temp.Sha(item.Video.Path);
        Assert.Equal(code, await Refused(() => Step(server).RunAsync(item, new RecordingContext(), default)));
        Assert.Empty(server.Calls);
    }

    [Theory]
    [InlineData("no_video", "no_video")]
    [InlineData("probe_failed", "not_video")]
    [InlineData("no_media", "not_video")]
    [InlineData("ffmpeg_missing", null)]
    [InlineData(null, null)]
    public async Task Other_kinds_are_asked_of_ffprobe_when_it_is_there(string? probe, string? code)
    {
        var item = Item([0x1A, 0x45, 0xDF, 0xA3, .. new byte[500]], "lesson.mkv");
        var media = new Probe(probe);
        var context = new RecordingContext();
        if (code is not null) Assert.Equal(code, await Refused(() => new HashStep(media: media).RunAsync(item, context, default)));
        else Assert.Equal(StepResult.Done, await new HashStep(media: media).RunAsync(item, context, default));
        Assert.Equal(probe == "ffmpeg_missing", context.Lines.Any(l => l.Code == "video_unchecked"));
        Assert.Equal(code is null, item.VideoChecked);
        Assert.Equal(1, media.Asked);
        // Once, not at every step
        if (code is null) await Step(new FakeServer(), media).RunAsync(item, context, default);
        Assert.Equal(1, media.Asked);
    }

    private sealed class Probe(string? code) : IMediaTools
    {
        public int Asked;
        public Task<SourceInfo> ProbeAsync(string path, CancellationToken ct)
        {
            Asked++;
            return code is null ? Task.FromResult(new SourceInfo(1280, 720, 0, 30, true, 12)) : throw new LocalEncodeException(code, code);
        }
        public Task<string?> PickEncoderAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task EncodeAsync(string source, SourceInfo info, IReadOnlyList<PlannedRung> rungs, IReadOnlyList<string> outputs, string encoder, EncodeLadderSettings settings, IProgress<double> progress, CancellationToken ct) => throw new NotSupportedException();
        public Task ExtractAudioAsync(string source, string wavPath, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task What_is_not_the_checked_file_is_never_sent_whole_and_the_servers_copy_goes()
    {
        var item = Item(Mp4IndexTests.File("vide"));
        var size = item.Video.Size;
        Change(item, 100);
        var server = new FakeServer();
        Assert.Equal("content_changed", await Refused(() => Step(server).RunAsync(item, new RecordingContext(), default)));
        Assert.Equal((size - 1) / 1000, server.Calls.Count(c => c.Method == "PATCH"));
        Assert.Equal(1, server.Calls.Count(c => c.Method == "DELETE"));
        Assert.Empty(server.Uploads);
        Assert.Empty(server.Uploaded);
        Assert.Null(item.UploadUrl);

        // Read right this time: from nothing
        Change(item, 100);
        Assert.Equal(StepResult.Done, await Step(server).RunAsync(item, new RecordingContext(), default));
        Assert.Equal(File.ReadAllBytes(item.Video.Path), Assert.Single(server.Uploads).Value.Data.ToArray());
        Assert.False(item.StartOver);
        Assert.Contains("item-1", server.Uploaded);
    }

    [Fact]
    public async Task A_copy_the_server_could_not_drop_is_dropped_before_the_next_try_also_after_a_restart()
    {
        var item = Item(Mp4IndexTests.File("vide"));
        Change(item, 100);
        var server = new FakeServer { DeleteFailures = 2 };
        Assert.Equal("content_changed", await Refused(() => Step(server).RunAsync(item, new RecordingContext(), default)));
        Assert.Single(server.Uploads);
        Change(item, 100);
        item = Restarted(item);
        Assert.True(item.StartOver);
        await Assert.ThrowsAsync<TusException>(() => Step(server).RunAsync(item, new RecordingContext(), default));
        Assert.Equal(StepResult.Done, await Step(server).RunAsync(item, new RecordingContext(), default));
        Assert.Equal(File.ReadAllBytes(item.Video.Path), Assert.Single(server.Uploads).Value.Data.ToArray());
    }

    private static QueueItem Restarted(QueueItem item) =>
        System.Text.Json.JsonSerializer.Deserialize<QueueItem>(System.Text.Json.JsonSerializer.Serialize(item))!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_lost_answer_goes_on_where_the_server_is(bool kept)
    {
        var item = Item(Mp4IndexTests.File("vide"));
        var server = new FakeServer { LoseAt = 2000, LoseKeeps = kept };
        await Assert.ThrowsAsync<TusException>(() => Step(server).RunAsync(item, new RecordingContext(), default));
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await Step(server).RunAsync(item, context, default));
        Assert.Equal(File.ReadAllBytes(item.Video.Path), Assert.Single(server.Uploads).Value.Data.ToArray());
        // Nothing is read a second time for it
        Assert.DoesNotContain(context.Lines, l => l.Code == "sent_check");
    }

    [Fact]
    public async Task A_part_sent_again_must_be_the_same()
    {
        var item = Item(Mp4IndexTests.File("vide"));
        var server = new FakeServer { LoseAt = 2000 };
        await Assert.ThrowsAsync<TusException>(() => Step(server).RunAsync(item, new RecordingContext(), default));
        Change(item, 2100);
        Assert.Equal("content_changed", await Refused(() => Step(server).RunAsync(item, new RecordingContext(), default)));
        Assert.Empty(server.Uploads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task After_a_restart_what_was_sent_is_read_again_and_must_be_the_same(bool changed)
    {
        var item = Item(Mp4IndexTests.File("vide"));
        var server = new FakeServer { LoseAt = 2000, LoseKeeps = true };
        await Assert.ThrowsAsync<TusException>(() => Step(server).RunAsync(item, new RecordingContext(), default));
        item = Restarted(item);
        Assert.Equal(2000, item.SentTo);
        if (changed) Change(item, 100);
        var context = new RecordingContext();
        if (changed) Assert.Equal("content_changed", await Refused(() => Step(server).RunAsync(item, context, default)));
        else Assert.Equal(StepResult.Done, await Step(server).RunAsync(item, context, default));
        Assert.Contains(context.Lines, l => l.Code == "sent_check");
        Assert.Equal(changed, server.Uploads.Count == 0);
        if (!changed) Assert.Equal(File.ReadAllBytes(item.Video.Path), Assert.Single(server.Uploads).Value.Data.ToArray());
    }

    [Fact]
    public async Task An_upload_begun_by_an_earlier_version_goes_on()
    {
        var item = Item(Mp4IndexTests.File("vide"));
        var server = new FakeServer { LoseAt = 2000, LoseKeeps = true };
        await Assert.ThrowsAsync<TusException>(() => Step(server).RunAsync(item, new RecordingContext(), default));
        item = Restarted(item);
        (item.SentTo, item.SentSha256) = (0, null);
        Assert.Equal(StepResult.Done, await Step(server).RunAsync(item, new RecordingContext(), default));
        Assert.Contains("item-1", server.Uploaded);
    }

    [Theory]
    [InlineData(ImportStepKind.Hash)]
    [InlineData(ImportStepKind.Upload)]
    public async Task Trying_again_is_offered_once_another_file_is_in_its_place(ImportStepKind at)
    {
        var dir = Temp.Folder();
        var path = Path.Combine(dir, "lesson.mp4");
        File.WriteAllBytes(path, Mp4IndexTests.File("soun"));
        var item = QueueItem.For(FolderScanner.Scan(dir, false, null).Videos[0], "b1", "ch1", DateTimeOffset.UtcNow);
        if (at == ImportStepKind.Upload)
        {
            (item.Sha256, item.FingerprintHash) = (Temp.Sha(path), new string('c', 64));
            foreach (var done in item.Steps.Where(s => s.Kind < at)) done.Status = StepStatus.Done;
        }
        var server = new FakeServer();
        var steps = Enum.GetValues<ImportStepKind>().Select(IImportStep (k) => k switch
        {
            ImportStepKind.Hash => new HashStep(),
            ImportStepKind.Upload => Step(server),
            _ => new ScriptedStep(k),
        }).ToArray();
        var look = new SemaphoreSlim(0);
        var queue = new ImportQueue(Path.Combine(dir, "queue.json"), steps)
        {
            LookAtFile = async (p, ct) =>
            {
                await look.WaitAsync(ct);
                return await SourceAccess.Default.LookAsync(p, ct);
            },
        };
        queue.Add([item]);
        queue.Start();
        await Temp.WaitUntil(() => item.Phase == ItemPhase.Failed, 20_000);
        Assert.Equal("no_video", item.CurrentStep!.LastErrorCode);
        Assert.True(item.NeedsOtherFile);
        Assert.False(item.CanRetry);
        // Asked all the same: nothing happens
        queue.Retry(item.Id);
        Assert.Equal(ItemPhase.Failed, item.Phase);

        File.WriteAllBytes(path, Mp4IndexTests.File("vide", "soun"));
        look.Release(10);
        await Temp.WaitUntil(() => item.CanRetry, 40_000);
        queue.Retry(item.Id);
        await Temp.WaitUntil(() => item.Phase == ItemPhase.Done, 20_000);
        Assert.Equal(Temp.Sha(path), item.Sha256);
        Assert.Equal(File.ReadAllBytes(path), Assert.Single(server.Uploads).Value.Data.ToArray());
        await queue.StopAsync();
    }

    [Fact]
    public async Task A_hash_of_less_than_the_whole_file_is_none()
    {
        using var state = new HashSoFar(1000, 0);
        Assert.Equal("file_writing", (await Assert.ThrowsAsync<StepFailedException>(() => FileHasher.HashAsync(new MemoryStream(new byte[900]), state, null, default))).Code);
    }
}
