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

    private static byte[] Track(string handler) => Track(handler, "avc1", 250);

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Track(string handler, string codec, uint samples) => Box("trak", Box("tkhd", new byte[84]), Box("mdia", Box("mdhd", new byte[24]),
        Box("hdlr", new byte[8], Encoding.ASCII.GetBytes(handler), new byte[13]),
        // QuickTime has a second handler here, which is not the track's
        Box("minf", Box("hdlr", new byte[8], "url "u8.ToArray(), new byte[13]),
            Box("stbl", Box("stsd", new byte[4], U32(1), U32(86), Encoding.ASCII.GetBytes(codec), new byte[70]), Box("stsz", new byte[4], U32(0), U32(samples))))));

    private static byte[] Data(int length) => Box("mdat", Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray());

    /// <summary>The index first, as for the web: the rest is cut off or never filled after it.</summary>
    private static byte[] IndexFirst(int data = 300_000) => [.. Box("ftyp", "isom"u8.ToArray(), new byte[4]), .. Moov("vide", "soun"), .. Data(data)];

    [Fact]
    public async Task With_the_index_first_a_file_cut_off_in_its_data_is_damaged()
    {
        var whole = IndexFirst();
        Assert.Equal(VideoContent.Video, await Read(whole));
        foreach (var length in new[] { whole.Length - 1, whole.Length / 2, whole.Length - 299_990 }) Assert.Equal(VideoContent.Damaged, await Read(whole[..length]));
        // Not one byte of its data
        Assert.Equal(VideoContent.Damaged, await Read(whole[..(whole.Length - Data(300_000).Length)]));
    }

    [Fact]
    public async Task With_the_index_first_a_file_that_was_never_filled_is_damaged()
    {
        var empty = IndexFirst();
        empty.AsSpan(empty.Length / 3).Clear();
        Assert.Equal(VideoContent.Damaged, await Read(empty));
        // A quiet stretch is not an empty file
        var quiet = IndexFirst();
        quiet.AsSpan(quiet.Length / 2, 60_000).Clear();
        Assert.Equal(VideoContent.Video, await Read(quiet));
        var tail = IndexFirst();
        tail.AsSpan(tail.Length - 40_000).Clear();
        Assert.Equal(VideoContent.Video, await Read(tail));
    }

    [Fact]
    public async Task Room_left_free_is_meant_to_be_empty_and_the_data_is_looked_at_wherever_it_is()
    {
        var free = Box("free", new byte[2_000_000]);
        // At the end, at the start, between the index and the data
        Assert.Equal(VideoContent.Video, await Read([.. IndexFirst(100_000), .. free]));
        Assert.Equal(VideoContent.Video, await Read([.. IndexFirst(3000), .. free]));
        Assert.Equal(VideoContent.Video, await Read([.. Box("ftyp", new byte[8]), .. free, .. Data(100_000), .. Moov("vide")]));
        Assert.Equal(VideoContent.Video, await Read([.. Box("ftyp", new byte[8]), .. Moov("vide"), .. free, .. Data(100_000)]));
        // The data in many pieces
        byte[] Pieces(byte[] piece) => [.. Box("ftyp", new byte[8]), .. Moov("vide"), .. Enumerable.Repeat((byte[])[.. Box("moof", new byte[16]), .. piece], 100).SelectMany(b => b), .. free];
        Assert.Equal(VideoContent.Video, await Read(Pieces(Data(1000))));
        Assert.Equal(VideoContent.Damaged, await Read(Pieces(Box("mdat", new byte[1000]))));
        Assert.Equal(VideoContent.Damaged, await Read([.. Box("ftyp", new byte[8]), .. Moov("vide"), .. Box("mdat", new byte[100_000]), .. free]));
    }

    [Fact]
    public async Task A_fragmented_file_is_whole_to_its_last_fragment()
    {
        byte[] Fragment() => [.. Box("moof", Box("mfhd", new byte[8])), .. Data(2000)];
        byte[] start = [.. Box("ftyp", "isom"u8.ToArray(), new byte[4]), .. Moov("vide", "soun")];
        byte[] whole = [.. start, .. Fragment(), .. Fragment(), .. Fragment()];
        Assert.Equal(VideoContent.Video, await Read(whole));
        Assert.Equal(VideoContent.Damaged, await Read(whole[..^500]));
        Assert.Equal(VideoContent.Damaged, await Read(start));
        Assert.Equal(VideoContent.Damaged, await Read([.. start, .. Fragment()[..10]]));
    }

    [Fact]
    public async Task What_follows_a_whole_file_is_not_looked_into()
    {
        // Padding, text, a box that says more than there is
        foreach (var tail in new byte[][] { new byte[5000], "some app's own words, after the end"u8.ToArray(), Box("free", new byte[100])[..50] })
        {
            Assert.Equal(VideoContent.Video, await Read([.. IndexFirst(3000), .. tail]));
            Assert.Equal(VideoContent.Video, await Read([.. File("vide"), .. tail]));
        }
    }

    [Fact]
    public async Task A_cover_is_not_a_video_and_one_frame_or_a_film_of_stills_is()
    {
        Task<VideoContent> With(params byte[][] tracks) => Read([.. Box("ftyp", new byte[8]), .. Data(3000), .. Box("moov", tracks)]);
        Assert.Equal(VideoContent.NoVideo, await With(Track("soun"), Track("vide", "jpeg", 1)));
        Assert.Equal(VideoContent.NoVideo, await With(Track("vide", "png ", 1), Track("soun")));
        Assert.Equal(VideoContent.Damaged, await With(Track("vide", "jpeg", 1)));
        Assert.Equal(VideoContent.Video, await With(Track("soun"), Track("vide", "jpeg", 400)));
        Assert.Equal(VideoContent.Video, await With(Track("soun"), Track("vide", "avc1", 1)));
        Assert.Equal(VideoContent.Video, await With(Track("vide", "jpeg", 1), Track("vide"), Track("soun")));
    }

    [Fact]
    public async Task A_packed_index_is_never_a_reason_to_refuse()
    {
        Assert.Equal(VideoContent.Undecided, await Read([.. Box("ftyp", new byte[8]), .. Data(3000), .. Box("moov", Box("cmov", Box("dcom", new byte[4]), Box("cmvd", new byte[200])))]));
    }

    /// <summary>Counts what is asked of the disk.</summary>
    private sealed class Counting(byte[] bytes) : MemoryStream(bytes)
    {
        public int Reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Reads++;
            return base.ReadAsync(buffer, ct);
        }
    }

    [Fact]
    public async Task A_file_of_countless_boxes_is_undecided_after_a_bounded_number_of_small_reads()
    {
        var crumb = Box("free");
        byte[] Crumbs(int count) => Enumerable.Repeat(crumb, count).SelectMany(b => b).ToArray();
        foreach (var bytes in new byte[][]
        {
            [.. Box("ftyp", new byte[8]), .. Crumbs(200_000), .. Data(100), .. Moov("vide")],
            [.. Box("ftyp", new byte[8]), .. Data(100), .. Box("moov", Crumbs(200_000), Track("vide"))],
            [.. Box("ftyp", new byte[8]), .. Data(100), .. Box("moov", Enumerable.Repeat(Box("trak", Box("mdia")), 100_000).ToArray())],
        })
        {
            var file = new Counting(bytes);
            Assert.Equal(VideoContent.Undecided, await Mp4Index.ReadAsync(file, bytes.Length, default));
            Assert.InRange(file.Reads, 1, 200);
        }
        // A long fragmented recording is well inside the bound
        byte[] fragments = Enumerable.Repeat((byte[])[.. Box("moof", Box("mfhd", new byte[8])), .. Data(300)], 5000).SelectMany(b => b).ToArray();
        Assert.Equal(VideoContent.Video, await Read([.. Box("ftyp", new byte[8]), .. Moov("vide", "soun"), .. fragments]));
    }

    internal static byte[] File(params string[] handlers) =>
        [.. Box("ftyp", "isom"u8.ToArray(), new byte[4]), .. Box("mdat", Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray()), .. Moov(handlers)];

    private static byte[] Moov(params string[] handlers) => Box("moov", [Box("mvhd", new byte[100]), .. handlers.Select(Track), Box("udta", Box("meta", new byte[30]))]);

    private static Task<VideoContent> Read(byte[] file) => Mp4Index.ReadAsync(new MemoryStream(file), file.Length, default);

    [Fact]
    public async Task A_video_track_is_found_before_or_after_the_sound_and_the_data()
    {
        Assert.Equal(VideoContent.Video, await Read(File("vide", "soun")));
        Assert.Equal(VideoContent.Video, await Read(File("soun", "vide")));
        Assert.Equal(VideoContent.Video, await Read([.. Box("ftyp", new byte[8]), .. Moov("vide"), .. Data(100)]));
        Assert.Equal(VideoContent.Video, await Read([.. Box("ftyp", new byte[8]), .. Wide("mdat", new byte[100]), .. Moov("vide")]));
        // A moov to the end of the file
        var open = File("vide");
        BinaryPrimitives.WriteUInt32BigEndian(open.AsSpan(open.Length - Moov("vide").Length), 0);
        Assert.Equal(VideoContent.Video, await Read(open));
    }

    private static byte[] Id(uint id, params byte[][] rest) => Box("tkhd", [new byte[12], U32(id), new byte[68], .. rest]);

    private static byte[] Numbered(uint id, string handler, string codec, uint samples, params uint[] chapters) => Box("trak", Id(id),
        chapters.Length == 0 ? [] : Box("tref", Box("chap", chapters.Select(U32).ToArray())),
        Box("mdia", Box("mdhd", new byte[24]), Box("hdlr", new byte[8], Encoding.ASCII.GetBytes(handler), new byte[13]),
            Box("minf", Box("stbl", Box("stsd", new byte[4], U32(1), U32(86), Encoding.ASCII.GetBytes(codec), new byte[70]), Box("stsz", new byte[4], U32(0), U32(samples))))));

    [Theory]
    [InlineData("jpeg", 12u)]
    [InlineData("avc1", 12u)]
    public async Task The_pictures_of_a_sounds_chapters_are_no_video(string codec, uint pictures)
    {
        byte[] Chapters(params byte[][] tracks) => [.. Box("ftyp", "M4A "u8.ToArray(), new byte[4]), .. Data(3000), .. Box("moov", [Box("mvhd", new byte[100]), .. tracks])];
        // Named by the sound, before it or after it
        Assert.Equal(VideoContent.NoVideo, await Read(Chapters(Numbered(1, "soun", "mp4a", 900, 2, 3), Numbered(2, "text", "text", 12), Numbered(3, "vide", codec, pictures))));
        Assert.Equal(VideoContent.NoVideo, await Read(Chapters(Numbered(3, "vide", codec, pictures), Numbered(1, "soun", "mp4a", 900, 3))));
        // A video with chapter pictures is a video
        Assert.Equal(VideoContent.Video, await Read(Chapters(Numbered(1, "vide", "avc1", 900, 3), Numbered(2, "soun", "mp4a", 900), Numbered(3, "vide", codec, pictures))));
        Assert.Equal(VideoContent.Video, await Read(Chapters(Numbered(1, "soun", "mp4a", 900), Numbered(3, "vide", codec, pictures))));
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
        // A track larger than the index that holds it (after the index's header and its mvhd)
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
    [InlineData("probe_failed", null)]
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
        Assert.Equal(probe == "probe_failed", context.Lines.Any(l => l.Code == "video_undecided"));
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
    public async Task A_video_by_its_index_that_ffprobe_finds_no_video_in_is_refused_before_anything_is_opened()
    {
        var item = Item(Mp4IndexTests.File("vide", "soun"));
        item.Sha256 = null;
        var media = new Probe("no_video");
        Assert.Equal("no_video", await Refused(() => new HashStep(media: media).RunAsync(item, new RecordingContext(), default)));
        // Checked by a version that did not ask ffprobe
        var (checkedBefore, server) = (Item(Mp4IndexTests.File("vide", "soun")), new FakeServer());
        checkedBefore.VideoChecked = true;
        Assert.Equal("no_video", await Refused(() => Step(server, media).RunAsync(checkedBefore, new RecordingContext(), default)));
        Assert.Empty(server.Calls);
    }

    [Theory]
    [InlineData("probe_failed")]
    [InlineData("no_media")]
    [InlineData("source_read")]
    [InlineData("ffmpeg_missing")]
    [InlineData(null)]
    public async Task Only_no_video_is_taken_from_ffprobe_over_the_index_and_it_is_asked_once(string? probe)
    {
        var item = Item(Mp4IndexTests.File("vide", "soun"));
        var media = new Probe(probe);
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await new HashStep(media: media).RunAsync(item, context, default));
        Assert.Equal(StepResult.Done, await Step(new FakeServer(), media).RunAsync(item, context, default));
        Assert.Equal(1, media.Asked);
        Assert.DoesNotContain(context.Lines, l => l.Code is "video_unchecked" or "video_undecided");
    }

    [Theory]
    [InlineData("no_video", "no_video")]
    [InlineData("no_media", "not_video")]
    public async Task The_encodes_own_no_video_is_final_the_server_is_not_asked_to_encode_it(string probe, string code)
    {
        var item = Item(Mp4IndexTests.File("vide", "soun"));
        item.VideoChecked = item.VideoProbed = true;
        var server = new RenditionServer();
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), server);
        var reported = new List<Exception>();
        var media = new FakeMedia { Fail = new LocalEncodeException(probe, "The file has sound and no video track") };
        var step = new EncodeStep(api, new Uri("https://videos.api.example"), media, new FakePackager(), Temp.Folder(), "Tools test", (ex, _) => reported.Add(ex), _ => long.MaxValue);
        Assert.Equal(code, await Refused(() => step.RunAsync(item, new RecordingContext(), default)));
        Assert.Empty(reported);
        Assert.Equal(0, media.Encodes);
    }

    [Fact]
    public async Task A_video_whose_size_could_not_be_told_is_left_to_the_server()
    {
        var probed = FfmpegMediaTools.ParseProbe("""{"streams":[{"codec_type":"video","codec_name":"prores"},{"codec_type":"audio"}],"format":{"duration":"12.0"}}""");
        Assert.Equal(0, probed.Width);
        var item = Item(Mp4IndexTests.File("vide", "soun"));
        // The check passes it, with the same probe
        var media = new FakeMedia { Info = probed };
        Assert.Equal(StepResult.Done, await new HashStep(media: media).RunAsync(item, new RecordingContext(), default));
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), new RenditionServer());
        var reported = new List<Exception>();
        var context = new RecordingContext();
        var step = new EncodeStep(api, new Uri("https://videos.api.example"), media, new FakePackager(), Temp.Folder(), "Tools test", (ex, _) => reported.Add(ex), _ => long.MaxValue);
        Assert.Equal(StepResult.Skipped, await step.RunAsync(item, context, default));
        Assert.Contains(context.Lines, l => l.Code == "encode_failed");
        Assert.Equal("no_dimensions", Assert.IsType<LocalEncodeException>(Assert.Single(reported)).Code);
    }

    [Fact]
    public async Task A_file_refused_after_its_video_was_opened_marks_it_failed_on_the_server()
    {
        var server = new FakeServer();
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("t"), server);
        using var reporter = new ErrorReporter(api.ReportErrorsAsync, "1", () => "en", TimeSpan.FromHours(1));
        var item = Item(Mp4IndexTests.File("vide", "soun"));
        item.ServerItemId = "item-9";
        var refused = new StepFailedException("no_video", "The file has sound and no video", retryable: false);
        await new ServerFailureSink(api, reporter).ItemFailedAsync(item, item.Steps.Single(s => s.Kind == ImportStepKind.Encode), refused, "ERR-AAAA-BBBB", default);
        Assert.Contains("sound and no video", server.Calls.Single(c => c.Path.EndsWith("/items/item-9/failed")).Body);
    }

    [Fact]
    public async Task A_file_whose_index_takes_too_long_goes_up_with_a_line_in_its_log()
    {
        var item = Item(Mp4IndexTests.File("soun"));
        var context = new RecordingContext();
        // ffprobe answers for it; without one it goes up
        Assert.Equal("no_video", await Refused(() => VideoCheck.RunAsync(Item(Mp4IndexTests.File("soun")), SourceAccess.Default, new Probe("no_video"), context, default, TimeSpan.Zero)));
        await VideoCheck.RunAsync(item, SourceAccess.Default, new Probe("ffmpeg_missing"), context, default, TimeSpan.Zero);
        Assert.Contains(context.Lines, l => l.Code == "video_undecided");
        // Stopped, it is no answer
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VideoCheck.RunAsync(Item(Mp4IndexTests.File("soun")), SourceAccess.Default, null, context, stopped.Token));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(400)]
    public async Task An_upload_the_server_will_not_give_up_is_left_and_another_begun(int status)
    {
        var item = Item(Mp4IndexTests.File("vide"));
        Change(item, 100);
        var server = new FakeServer { DeleteStatus = status };
        var context = new RecordingContext();
        Assert.Equal("content_changed", await Refused(() => Step(server).RunAsync(item, context, default)));
        Assert.Contains(context.Lines, l => l.Code == "upload_kept");
        var kept = Assert.Single(server.Uploads).Key;
        Assert.Null(item.UploadUrl);
        Change(item, 100);
        item = Restarted(item);
        // The server offers the kept one to go on with: it is not taken
        Assert.Equal(StepResult.Done, await Step(server).RunAsync(item, new RecordingContext(), default));
        Assert.Equal(2, server.Uploads.Count);
        Assert.Equal(File.ReadAllBytes(item.Video.Path), server.Uploads.Single(u => u.Key != kept).Value.Data.ToArray());
    }

    [Fact]
    public async Task A_lost_answer_that_the_server_remembers_costs_no_second_reading()
    {
        var item = Item(Mp4IndexTests.File("vide"));
        // The part is kept and its answer is a conflict, as when it was sent twice
        var server = new FakeServer { LoseAt = 2000, LoseKeeps = true, LoseStatus = 409 };
        var context = new RecordingContext();
        Assert.Equal(StepResult.Done, await Step(server).RunAsync(item, context, default));
        Assert.DoesNotContain(context.Lines, l => l.Code == "sent_check");
        Assert.Equal(item.Video.Size, item.SentTo);
    }

    [Fact]
    public async Task A_file_hashed_by_a_version_that_did_not_check_opens_nothing_on_the_server()
    {
        var item = Item(Mp4IndexTests.File("soun"));
        var server = new RenditionServer();
        var api = new ImportApiClient(new Uri("https://admin.api.example"), _ => Task.FromResult<string?>("tok"), server);
        var work = Temp.Folder();
        // Left by the file that was in its place
        var left = Path.Combine(work, item.Id, "360p");
        Directory.CreateDirectory(left);
        var step = new EncodeStep(api, new Uri("https://videos.api.example"), new FakeMedia(), new FakePackager(), work, "Tools test", (_, _) => { }, _ => long.MaxValue);
        Assert.Equal("no_video", await Refused(() => step.RunAsync(item, new RecordingContext(), default)));
        Assert.Equal(0, server.Inner.OpenCount);
        Assert.False(Directory.Exists(left));
    }

    [Fact]
    public async Task A_hash_of_less_than_the_whole_file_is_none()
    {
        using var state = new HashSoFar(1000, 0);
        Assert.Equal("file_writing", (await Assert.ThrowsAsync<StepFailedException>(() => FileHasher.HashAsync(new MemoryStream(new byte[900]), state, null, default))).Code);
    }
}
