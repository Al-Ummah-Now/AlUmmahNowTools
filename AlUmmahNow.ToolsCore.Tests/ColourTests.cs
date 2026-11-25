using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlUmmahNow.ToolsCore.Import;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

public class ColourTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    /// <summary>colour-vectors.json is written by the media container's own colour.mjs and prebuilt.mjs (AlUmmahNow apps/media/container): what it decides for these streams.</summary>
    [Fact]
    public void Decides_exactly_what_the_server_does()
    {
        var vectors = JsonNode.Parse(File.ReadAllText(Fixture("colour-vectors.json")))!.AsArray();
        Assert.True(vectors.Count > 400);
        var end = SdrPlan.End;
        Assert.Equal(File.ReadAllText(Fixture("colour-rung-end.txt")).Trim(), end);
        foreach (var vector in vectors)
        {
            var stream = vector!["stream"]!;
            var colour = SourceColour.Of(stream);
            var kind = vector["plan"]!.GetValue<string>();
            if (kind == "unsupported")
            {
                Assert.Equal("colour_unsupported", Assert.Throws<LocalEncodeException>(() => SdrPlan.For(colour)).Code);
            }
            else
            {
                var plan = SdrPlan.For(colour);
                Assert.True(kind == plan.Kind, $"{stream}: {plan.Kind}, the server {kind}");
                Assert.Equal(vector["filters"]!.GetValue<string>(), plan.Filters(1280, 720, HdrCubes.FileName));
            }
            var asRung = vector["asRung"]!.AsArray();
            foreach (var (source, i) in new[] { SdrPlan.AsIs, SdrPlan.Hlg }.Select((k, i) => (k, i)))
            {
                Assert.True(asRung[i] is null == (new SdrPlan(source).Wrong(colour) is null), $"as a rung of {source}: {stream}");
            }
        }
    }

    [Theory]
    [InlineData("hlg")]
    [InlineData("pq")]
    public void The_tables_are_the_servers_byte_for_byte(string kind)
    {
        var cubes = Ladder.Settings.Ladder.Colour!.Cubes!;
        Assert.Equal(kind == "hlg" ? cubes.Hlg : cubes.Pq, HdrCubes.Sha256(HdrCubes.Read(kind)));
        Assert.Equal(HdrCubes.Read(kind), HdrCubes.For(new SdrPlan(kind), Ladder.Settings.Ladder));
    }

    [Fact]
    public void Leaves_HDR_to_a_server_with_other_tables_or_that_does_not_say()
    {
        var ladder = Ladder.Settings.Ladder;
        var hlg = new SdrPlan(SdrPlan.Hlg);
        Assert.Equal("hdr_settings", Assert.Throws<LocalEncodeException>(() => HdrCubes.For(hlg, ladder with { Colour = null })).Code);
        Assert.Equal("hdr_settings", Assert.Throws<LocalEncodeException>(() => HdrCubes.For(hlg, ladder with { Colour = new(new EncodeCubes("00", "00")) })).Code);
    }

    private static readonly bool HasFfmpeg = OnPath("ffmpeg") && OnPath("ffprobe");

    private static bool OnPath(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Any(p => p.Length > 0 && (File.Exists(Path.Combine(p, name)) || File.Exists(Path.Combine(p, name + ".exe"))));

    private static string Run(string exe, params string[] args)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{exe} {string.Join(' ', args)}\n{error.Result}");
        return output;
    }

    /// <summary>Real encodes through the app's own ffmpeg arguments, measured: a grey and a skin patch of known HDR light.</summary>
    [Theory]
    [InlineData("libx264", "arib-std-b67", 0.75, 175)]
    [InlineData("libx264", "arib-std-b67", 0.5, 108)]
    [InlineData("libx264", "smpte2084", 0.5807, 232)]
    [InlineData("h264_videotoolbox", "arib-std-b67", 0.75, 175)]
    [InlineData("libx264", "bt709", 0.5, 128)]
    [InlineData("libx264", "arib-std-b67", 0.75, 175, "321x241")]
    [InlineData("libx264", "smpte170m", 0.5, 128, "321x241")]
    public async Task An_encoded_rung_is_SDR_in_the_right_levels_and_says_so(string encoder, string transfer, double signal, int expected, string size = "640x360")
    {
        if (!HasFfmpeg) return;
        if (!Run("ffmpeg", "-hide_banner", "-encoders").Contains(encoder, StringComparison.Ordinal)) return;
        if (encoder == "h264_videotoolbox" && !OperatingSystem.IsMacOS()) return;
        // A name and a folder that would break a filter that held them
        var directory = Directory.CreateTempSubdirectory("aun colour 'x' ").FullName;
        try
        {
            var (space, primaries) = transfer switch { "bt709" => ("bt709", "bt709"), "smpte170m" => ("smpte170m", "smpte170m"), _ => ("bt2020nc", "bt2020") };
            var y = (int)Math.Round(64 + (876 * signal));
            var source = Path.Combine(directory, "in, [1].mkv");
            Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=black:size=" + size + ":rate=25:duration=2,format=yuv444p10le,geq=lum=" + y.ToString(CultureInfo.InvariantCulture) + ":cb=512:cr=512,setparams=color_primaries=" + primaries + ":color_trc=" + transfer + ":colorspace=" + space + ":range=tv", "-c:v", "ffv1", source);
            var tools = new FfmpegMediaTools(_ => Task.FromResult<(string, string)?>(("ffmpeg", "ffprobe")));
            var info = await tools.ProbeAsync(source, default);
            var ladder = Ladder.Settings.Ladder;
            var rungs = EncodeLadder.For(info, ladder);
            var outputs = rungs.Select(r => Path.Combine(directory, r.Name + ".mp4")).ToList();
            await tools.EncodeAsync(source, info, rungs, outputs, encoder, ladder, new Progress<double>(), default);
            var made = (await tools.ProbeAsync(outputs[0], default)).Colour!;
            Assert.Equal(("bt709", "bt709", "bt709", false, false), (made.Matrix, made.Primaries, made.Transfer, made.Full, made.IsHdr));
            Assert.Null(SdrPlan.For(info.Colour).Wrong(made));
            var raw = Path.Combine(directory, "out.rgb");
            Run("ffmpeg", "-v", "error", "-y", "-i", outputs[0], "-frames:v", "1", "-vf", "crop=8:8,scale=in_color_matrix=bt709:in_range=tv:out_range=pc:flags=accurate_rnd+full_chroma_int,format=rgb24", "-f", "rawvideo", raw);
            var rgb = File.ReadAllBytes(raw)[..3];
            Assert.True(rgb.All(v => Math.Abs(v - expected) <= 2), $"{string.Join(",", rgb)}, expected {expected}");
            Assert.True(rgb.Max() - rgb.Min() <= 1, $"a grey came out {string.Join(",", rgb)}");
            Assert.False(File.Exists(Path.Combine(directory, HdrCubes.FileName)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
