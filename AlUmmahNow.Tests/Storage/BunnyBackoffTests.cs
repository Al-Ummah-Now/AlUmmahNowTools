using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Storage;
using BunnyCDN.Net.Storage;

namespace AlUmmahNow.Tests.Storage;

/// <summary>
/// TOOLS-10: Bunny throttling (429) and server errors used to surface from the SDK as one generic exception that
/// was never retried, Retry-After was lost, and every process retried other failures on the same 1/2/4 s schedule.
/// These run the real SDK over a scripted HTTP transport, with the waits recorded instead of slept.
/// </summary>
public class BunnyBackoffTests
{
    private const string Zone = "test-zone";
    private const string Path = "/test-zone/videos/a.mp4";

    /// <summary>Answers each request with the next scripted response and records what was sent.</summary>
    private sealed class ScriptedTransport : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Func<HttpResponseMessage>> _script;
        public List<byte[]> Bodies { get; } = new();

        public ScriptedTransport(params Func<HttpResponseMessage>[] script) => _script = new(script);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken));
            return _script.TryDequeue(out var next) ? next() : new HttpResponseMessage(HttpStatusCode.Created);
        }
    }

    private static Func<HttpResponseMessage> Status(HttpStatusCode code, Action<HttpResponseHeaders>? headers = null) => () =>
    {
        var r = new HttpResponseMessage(code);
        headers?.Invoke(r.Headers);
        return r;
    };

    private static (BunnyStorageClient<Video> Client, List<TimeSpan> Waits) Client(ScriptedTransport transport, int seed = 1)
    {
        var waits = new List<TimeSpan>();
        var client = new BunnyStorageClient<Video>(Zone, "key", "de", 10, null, transport,
            d => { waits.Add(d); return Task.CompletedTask; }, new Random(seed));
        return (client, waits);
    }

    private static MemoryStream Body() => new(System.Text.Encoding.UTF8.GetBytes("video bytes"));

    [Fact]
    public async Task A_429_is_retried_after_the_servers_Retry_After_and_the_whole_body_is_resent()
    {
        var transport = new ScriptedTransport(Status(HttpStatusCode.TooManyRequests, h => h.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7))));
        var (client, waits) = Client(transport);

        await client.UploadAsync(Body(), Path);

        var wait = Assert.Single(waits);
        Assert.InRange(wait, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7.5));
        Assert.Equal(2, transport.Bodies.Count);
        Assert.All(transport.Bodies, b => Assert.Equal("video bytes", System.Text.Encoding.UTF8.GetString(b)));
    }

    [Fact]
    public async Task Retry_After_as_a_date_is_honoured()
    {
        var transport = new ScriptedTransport(Status(HttpStatusCode.ServiceUnavailable,
            h => h.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(20))));
        var (client, waits) = Client(transport);

        await client.UploadAsync(Body(), Path);

        Assert.InRange(Assert.Single(waits), TimeSpan.FromSeconds(17), TimeSpan.FromSeconds(21));
    }

    [Fact]
    public async Task Server_errors_back_off_exponentially_with_jitter_then_succeed()
    {
        var transport = new ScriptedTransport(Status(HttpStatusCode.BadGateway), Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.InternalServerError));
        var (client, waits) = Client(transport);

        await client.UploadAsync(Body(), Path);

        Assert.Equal(4, transport.Bodies.Count);
        Assert.Equal(3, waits.Count);
        for (var i = 0; i < 3; i++)
        {
            var full = TimeSpan.FromSeconds(Math.Pow(2, i));
            Assert.InRange(waits[i], full / 2, full);
        }
    }

    [Fact]
    public async Task It_gives_up_after_three_retries_with_the_status_in_the_error()
    {
        var transport = new ScriptedTransport(Enumerable.Repeat(Status(HttpStatusCode.ServiceUnavailable), 10).ToArray());
        var (client, waits) = Client(transport);

        var error = await Assert.ThrowsAsync<BunnyTransientResponseException>(() => client.UploadAsync(Body(), Path));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(4, transport.Bodies.Count);
        Assert.Equal(3, waits.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Client_errors_are_not_retried(HttpStatusCode status)
    {
        var transport = new ScriptedTransport(Status(status));
        var (client, waits) = Client(transport);

        await Assert.ThrowsAnyAsync<BunnyCDNStorageException>(() => client.UploadAsync(Body(), Path));

        Assert.Single(transport.Bodies);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_stream_that_cannot_seek_is_not_resent_after_a_partial_send()
    {
        var transport = new ScriptedTransport(Status(HttpStatusCode.ServiceUnavailable));
        var (client, waits) = Client(transport);

        await Assert.ThrowsAsync<BunnyTransientResponseException>(() => client.UploadAsync(new NonSeekable(Body()), Path));

        Assert.Single(transport.Bodies);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task The_callers_stream_stays_open_after_the_upload()
    {
        var (client, _) = Client(new ScriptedTransport(Status(HttpStatusCode.ServiceUnavailable)));
        using var body = Body();

        await client.UploadAsync(body, Path);

        Assert.True(body.CanSeek);
        Assert.Equal(body.Length, body.Position);
    }

    [Fact]
    public void Clients_failing_together_do_not_retry_in_step()
    {
        // 100 processes hit the same outage at once; their first retries must spread out, not land together.
        var error = new HttpRequestException("connection reset");
        var firstRetries = Enumerable.Range(0, 100).Select(seed => BunnyBackoff.DelayFor(1, error, new Random(seed))).ToList();

        Assert.All(firstRetries, d => Assert.InRange(d, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1)));
        Assert.True(firstRetries.Select(d => Math.Round(d.TotalMilliseconds / 10)).Distinct().Count() >= 30);
        Assert.True(firstRetries.Max() - firstRetries.Min() > TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void Waits_are_capped()
    {
        var random = new Random(3);
        Assert.InRange(BunnyBackoff.DelayFor(20, new TimeoutException(), random), TimeSpan.FromSeconds(15), BunnyBackoff.MaxDelay);
        var hours = new BunnyTransientResponseException(HttpStatusCode.TooManyRequests, TimeSpan.FromHours(3));
        Assert.InRange(BunnyBackoff.DelayFor(1, hours, random), BunnyBackoff.MaxRetryAfter, BunnyBackoff.MaxRetryAfter + TimeSpan.FromSeconds(1));
    }

    private sealed class NonSeekable : Stream
    {
        private readonly Stream _inner;
        public NonSeekable(Stream inner) => _inner = inner;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
