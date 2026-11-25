using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>A file's SHA-256 as far as it has been read, so a failed attempt or a pause goes on from there.</summary>
internal sealed class HashSoFar : IDisposable
{
    public HashSoFar(long size, long modifiedMs)
    {
        Size = size;
        ModifiedMs = modifiedMs;
    }

    public long Size { get; }
    public long ModifiedMs { get; }
    public long Offset { get; private set; }
    public IncrementalHash Hash { get; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public void Append(byte[] buffer, int count)
    {
        Hash.AppendData(buffer, 0, count);
        Offset += count;
    }

    public void Dispose() => Hash.Dispose();
}

/// <summary>The file's SHA-256 (admin-api dedupes a channel's files on it) and its upload fingerprint.</summary>
public static class FileHasher
{
    /// <summary>The first and last MiB make the fingerprint hash (web lib/upload/fingerprint.ts)</summary>
    public const int FingerprintEdgeBytes = 1024 * 1024;

    public static async Task<(string Sha256, string FingerprintHash)> HashAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var state = new HashSoFar(stream.Length, 0);
        return await HashAsync(stream, state, progress, ct).ConfigureAwait(false);
    }

    /// <summary>Goes on from <paramref name="state"/>, which keeps what was read when this throws.</summary>
    internal static async Task<(string Sha256, string FingerprintHash)> HashAsync(Stream stream, HashSoFar state, IProgress<double>? progress, CancellationToken ct)
    {
        var length = state.Size;
        stream.Position = state.Offset;
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            int n;
            while ((n = await stream.ReadAsync(buffer.AsMemory(0, 1 << 20), ct).ConfigureAwait(false)) > 0)
            {
                state.Append(buffer, n);
                progress?.Report(length == 0 ? 1 : (double)state.Offset / length);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        // The whole file when it is at most 2 MiB, else the first MiB then the last
        using var edges = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (length <= 2L * FingerprintEdgeBytes)
        {
            stream.Position = 0;
            var all = new byte[length];
            await stream.ReadExactlyAsync(all, ct).ConfigureAwait(false);
            edges.AppendData(all);
        }
        else
        {
            var edge = new byte[FingerprintEdgeBytes];
            stream.Position = 0;
            await stream.ReadExactlyAsync(edge, ct).ConfigureAwait(false);
            edges.AppendData(edge);
            stream.Position = length - FingerprintEdgeBytes;
            await stream.ReadExactlyAsync(edge, ct).ConfigureAwait(false);
            edges.AppendData(edge);
        }
        return (Convert.ToHexString(state.Hash.GetHashAndReset()).ToLowerInvariant(), Convert.ToHexString(edges.GetHashAndReset()).ToLowerInvariant());
    }
}

/// <summary>The TUS upload is gone (404 or 410): its session must be opened again.</summary>
public sealed class TusGoneException : Exception
{
    public TusGoneException(string message) : base(message) { }
}

/// <summary>A TUS request failed. <see cref="Status"/> is 0 when it was never answered.</summary>
public sealed class TusException : Exception
{
    public TusException(int status, string message, TimeSpan? retryAfter = null, Exception? inner = null) : base(message, inner)
    {
        Status = status;
        RetryAfter = retryAfter;
    }

    public int Status { get; }
    public TimeSpan? RetryAfter { get; }
    public bool IsRetryable => Status is 0 or 401 or 408 or 409 or 423 or 429 or >= 500;
}

/// <summary>
/// TUS 1.0 against api-videos' proxy (<c>/api/videos/tus</c>): create with the session admin-api opened, then
/// HEAD and PATCH with the admin's token. aun-core's UploadEngine opens its own uploads through the user's initiate
/// route (tier quotas and limits), so the importer speaks TUS itself on admin-api's session.
/// </summary>
public class TusClient
{
    private const string Version = "1.0.0";
    private readonly HttpClient _http;

    public TusClient(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: true) { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AlUmmahNowTools/1.0");
    }

    /// <summary>Creates the upload; answers its URL (on the same host as <paramref name="endpoint"/>).</summary>
    public virtual async Task<Uri> CreateAsync(Uri endpoint, string token, long length, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(Array.Empty<byte>()) };
        Headers(request, token);
        request.Headers.Add("Upload-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("Upload-Metadata", EncodeMetadata(metadata));
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Created) throw Failure(response, "create");
        var location = response.Headers.Location ?? throw new TusException((int)response.StatusCode, "The upload was created without a Location");
        var url = location.IsAbsoluteUri ? location : new Uri(endpoint, location);
        if (url.Scheme != Uri.UriSchemeHttps && !url.IsLoopback || url.Host != endpoint.Host) throw new TusException(502, "The upload URL is not on the videos API");
        return url;
    }

    public virtual async Task<long> OffsetAsync(Uri upload, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, upload);
        Headers(request, token);
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw Failure(response, "HEAD");
        return Offset(response) ?? throw new TusException((int)response.StatusCode, "HEAD answered without Upload-Offset");
    }

    /// <summary>Sends <paramref name="count"/> bytes from <paramref name="file"/> at <paramref name="offset"/>; answers the new offset.</summary>
    public virtual async Task<long> PatchAsync(Uri upload, string token, Stream file, long offset, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        file.Position = offset;
        await file.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Patch, upload) { Content = new ByteArrayContent(buffer) };
        Headers(request, token);
        request.Headers.Add("Upload-Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NoContent && !response.IsSuccessStatusCode) throw Failure(response, "PATCH");
        return Offset(response) ?? offset + count;
    }

    public static string EncodeMetadata(IReadOnlyDictionary<string, string> metadata) =>
        string.Join(",", metadata.Select(kv => $"{kv.Key} {Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value))}"));

    private static void Headers(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Tus-Resumable", Version);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new TusException(0, "The videos API could not be reached", inner: ex);
        }
    }

    private static long? Offset(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Upload-Offset", out var v) && long.TryParse(v.FirstOrDefault(), out var o) ? o : null;

    private static Exception Failure(HttpResponseMessage response, string what)
    {
        var status = (int)response.StatusCode;
        if (status is 404 or 410) return new TusGoneException($"The upload is gone ({what} {status})");
        return new TusException(status, $"{what} failed with {status}", response.Headers.RetryAfter?.Delta);
    }
}
