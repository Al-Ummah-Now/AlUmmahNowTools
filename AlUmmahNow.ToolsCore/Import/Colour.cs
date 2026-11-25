using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>A video stream's colours as ffprobe reads them; null where the file does not say.</summary>
public sealed record SourceColour(
    string? Matrix,
    string? Primaries,
    string? Transfer,
    bool Full = false,
    bool Rgb = false,
    string Codec = "",
    int Width = 0,
    int Height = 0,
    int? DolbyVisionProfile = null,
    int DolbyVisionCompatible = 0,
    bool HdrData = false)
{
    private static readonly string[] RgbFormats = { "rgb", "bgr", "gbr", "argb", "abgr", "pal8", "0rgb", "0bgr", "xv30", "x2rgb", "x2bgr" };

    public static SourceColour Of(JsonNode stream)
    {
        var format = Text(stream["pix_fmt"]) ?? "";
        int? profile = null;
        var (compatible, hdr) = (0, false);
        foreach (var side in stream["side_data_list"]?.AsArray() ?? new JsonArray())
        {
            var type = Text(side?["side_data_type"]) ?? "";
            hdr |= new[] { "mastering display", "content light", "hdr", "dovi", "dolby" }.Any(t => type.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (side?["dv_profile"] is not JsonNode dv) continue;
            profile = Number(dv);
            compatible = Number(side["dv_bl_signal_compatibility_id"]);
        }
        return new SourceColour(
            Known(stream["color_space"]),
            Known(stream["color_primaries"]),
            Known(stream["color_transfer"]),
            Text(stream["color_range"]) == "pc" || format.StartsWith("yuvj", StringComparison.Ordinal),
            RgbFormats.Any(f => format.StartsWith(f, StringComparison.Ordinal)),
            Text(stream["codec_name"]) ?? "",
            Number(stream["width"]),
            Number(stream["height"]),
            profile,
            compatible,
            hdr);
    }

    /// <summary>Tagged as HDR, or in an HDR container's colours: never sent.</summary>
    public bool IsHdr => Transfer is "arib-std-b67" or "smpte2084" || (Matrix?.StartsWith("bt2020", StringComparison.Ordinal) ?? false) || Primaries == "bt2020" || DolbyVisionProfile is not null || HdrData;

    private static string? Known(JsonNode? node) => Text(node) is { } text && text is not ("unknown" or "unspecified" or "reserved") ? text : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var n) ? n : 0;
}

/// <summary>
/// How a source becomes SDR BT.709, the only thing ever published: the media container's rules
/// (apps/media/container/colour.mjs), line for line.
/// </summary>
public sealed record SdrPlan(string Kind, string Matrix = "bt709", string Primaries = "bt709", bool Full = false, bool Rgb = false)
{
    /// <summary>The pixels are SDR BT.709 already: not touched, only tagged.</summary>
    public const string AsIs = "as_is";
    /// <summary>SDR in another matrix, range or gamut.</summary>
    public const string Convert = "convert";
    public const string Hlg = "hlg";
    public const string Pq = "pq";

    private static readonly string[] Matrices = { "bt709", "fcc", "bt470bg", "smpte170m", "smpte240m", "bt2020nc", "ycgco" };
    private static readonly string[] KnownPrimaries = { "bt709", "bt470m", "bt470bg", "smpte170m", "smpte240m", "film", "bt2020", "smpte428", "smpte431", "smpte432", "ebu3213" };
    /// <summary>Wider than BT.709: their colours move when the primaries are ignored.</summary>
    private static readonly string[] Wide = { "bt2020", "smpte431", "smpte432", "smpte428", "film" };
    /// <summary>An untagged standard-definition file of these is BT.601; every other untagged file is BT.709, as browsers show it.</summary>
    private static readonly string[] Bt601Codecs = { "mpeg1video", "mpeg2video", "mpeg4", "msmpeg4v1", "msmpeg4v2", "msmpeg4v3", "h263", "h263p", "flv1", "wmv1", "wmv2", "wmv3", "vc1", "dvvideo", "mjpeg", "vp6", "vp6f", "rv30", "rv40", "svq1", "svq3", "cinepak", "indeo5", "theora" };

    /// <summary>4:2:0 and the colorspace filter need even sizes.</summary>
    private const string Even = "w=trunc(iw/2)*2:h=trunc(ih/2)*2";
    private const string Fine = "flags=bicubic+accurate_rnd+full_chroma_int";
    /// <summary>Every frame says what it is; the encoder and the muxer write it.</summary>
    public const string Tags = "setparams=range=tv:color_primaries=bt709:color_trc=bt709:colorspace=bt709";

    /// <summary>The last filters of every rung: no HDR metadata, also of an SDR source that carries it (closed captions stay), and the tags.</summary>
    public const string End = "sidedata=mode=delete:type=MASTERING_DISPLAY_METADATA,sidedata=mode=delete:type=CONTENT_LIGHT_LEVEL,sidedata=mode=delete:type=DYNAMIC_HDR_PLUS," + Tags;

    public bool IsHdr => Kind is Hlg or Pq;

    /// <param name="c">Null: not known, taken as it is</param>
    public static SdrPlan For(SourceColour? c)
    {
        if (c is null) return new SdrPlan(AsIs);
        if (c.DolbyVisionProfile is not null && c.DolbyVisionCompatible == 0) throw new LocalEncodeException("colour_unsupported", $"Dolby Vision profile {c.DolbyVisionProfile} has no picture an ordinary player can show");
        var hdr = c.Transfer switch
        {
            "arib-std-b67" => Hlg,
            "smpte2084" => Pq,
            null when c.DolbyVisionProfile is not null => c.DolbyVisionCompatible switch { 4 => Hlg, 1 or 6 => Pq, _ => null },
            _ => null,
        };
        var matrix = c.Matrix == "bt470m" ? "fcc" : c.Matrix;
        if (!c.Rgb && matrix is not (null or "gbr" or "bt2020c") && !Matrices.Contains(matrix)) throw new LocalEncodeException("colour_unsupported", $"The colours of this file ({matrix}) cannot be converted");
        if (hdr is not null) return new SdrPlan(hdr, matrix == "bt709" ? "bt709" : "bt2020", "bt2020", c.Full, c.Rgb);
        var sd = Math.Max(c.Width, c.Height) < 1280 && Math.Min(c.Width, c.Height) <= 576;
        var m = matrix switch
        {
            "bt2020c" => "bt2020nc",
            null or "gbr" => sd && Bt601Codecs.Contains(c.Codec) ? "smpte170m" : "bt709",
            _ => matrix,
        };
        var p = c.Primaries is { } primaries && KnownPrimaries.Contains(primaries) ? primaries : m == "bt2020nc" ? "bt2020" : "bt709";
        return new SdrPlan(c.Rgb || c.Full || m != "bt709" || Wide.Contains(p) ? Convert : AsIs, m, p, c.Full, c.Rgb);
    }

    /// <summary>Filters that make the decoded source SDR BT.709 limited 4:2:0; empty when it is.</summary>
    /// <param name="cube">The plan's .cube file, by a name that needs no escaping</param>
    public string Filters(int width, int height, string cube)
    {
        if (IsHdr)
        {
            var from = Rgb ? "" : $":in_color_matrix={Matrix}:in_range={(Full ? "pc" : "tv")}";
            return $"scale=w={width}:h={height}:{Fine}{from},format=gbrp16le,lut3d=file={cube}:interp=tetrahedral,scale={Fine}:out_color_matrix=bt709:out_range=tv,format=yuv420p,{Tags}";
        }
        if (Kind != Convert) return "";
        string ColorSpace(string from, bool full) => $"colorspace=ispace={from}:iprimaries={Primaries}:itrc=bt709:irange={(full ? "pc" : "tv")}:space=bt709:primaries=bt709:trc=bt709:range=tv:format=yuv420p";
        // A crop, not a scale: swscale reads no YCgCo (a no-op at even sizes)
        if (!Rgb) return $"crop=trunc(iw/2)*2:trunc(ih/2)*2:0:0,{ColorSpace(Matrix, Full)},{Tags}";
        var rgb = $"scale={Even}:{Fine}:out_color_matrix=bt709:out_range=tv";
        return Wide.Contains(Primaries) ? $"{rgb},format=yuv444p,{ColorSpace("bt709", false)},{Tags}" : $"{rgb},format=yuv420p,{Tags}";
    }

    /// <summary>Why an encoded rung may not be sent, or null: the server's own check (prebuilt.mjs renditionColour).</summary>
    public string? Wrong(SourceColour made)
    {
        if (made.IsHdr) return "The encoded video still says it is HDR";
        if (made.Full || (made.Primaries is { } p && Wide.Contains(p)) || (made.Matrix is null ? Kind != AsIs : made.Matrix != "bt709")) return $"The encoded video's colours are {made.Matrix ?? "untagged"}{(made.Full ? ", full range" : "")}, not BT.709";
        return null;
    }
}

/// <summary>
/// The tables HDR is tone mapped through, the server's bytes (apps/media/container/colour.mjs <c>cubeText</c>).
/// The server says which it uses (<c>ENCODE_LADDER.colour.cubes</c>): an app with others leaves HDR files to it.
/// </summary>
public static class HdrCubes
{
    /// <summary>In the folder ffmpeg runs in, so no path has to be escaped inside a filter.</summary>
    public const string FileName = "colour.cube";

    public static byte[] Read(string kind)
    {
        using var packed = typeof(HdrCubes).Assembly.GetManifestResourceStream($"{kind}.cube.gz") ?? throw new InvalidOperationException($"No {kind} table in the app");
        using var gzip = new GZipStream(packed, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        return bytes.ToArray();
    }

    public static string Sha256(byte[] bytes) => System.Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>The plan's table, checked against the server's.</summary>
    public static byte[] For(SdrPlan plan, EncodeLadderSettings settings)
    {
        var bytes = Read(plan.Kind);
        var server = plan.Kind == SdrPlan.Hlg ? settings.Colour?.Cubes?.Hlg : settings.Colour?.Cubes?.Pq;
        if (server is null || !string.Equals(server, Sha256(bytes), StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalEncodeException("hdr_settings", "This app's HDR conversion is not the server's");
        }
        return bytes;
    }
}
