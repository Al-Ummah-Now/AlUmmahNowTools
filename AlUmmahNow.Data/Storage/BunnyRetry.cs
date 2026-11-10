using System.Net;

namespace AlUmmahNow.Data.Storage
{
    /// <summary>
    /// Bunny answered with a status worth retrying (429, 408 or a 5xx). The SDK maps every failed status to one
    /// generic exception and drops the response, so <see cref="BunnyTransientResponseHandler"/> raises this first,
    /// keeping the status and the server's <c>Retry-After</c>.
    /// </summary>
    public sealed class BunnyTransientResponseException : HttpRequestException
    {
        public BunnyTransientResponseException(HttpStatusCode statusCode, TimeSpan? retryAfter)
            : base($"Bunny storage returned {(int)statusCode} {statusCode}", null, statusCode)
        {
            RetryAfter = retryAfter;
        }

        /// <summary>How long the server asked us to wait, if it said.</summary>
        public TimeSpan? RetryAfter { get; }
    }

    /// <summary>Sits under the Bunny SDK's HttpClient and turns retryable responses into <see cref="BunnyTransientResponseException"/>.</summary>
    internal sealed class BunnyTransientResponseHandler : DelegatingHandler
    {
        public BunnyTransientResponseHandler(HttpMessageHandler inner) : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!BunnyBackoff.IsRetryableStatus(response.StatusCode))
            {
                return response;
            }

            var retryAfter = response.Headers.RetryAfter switch
            {
                { Delta: { } delta } => delta,
                { Date: { } date } => date - DateTimeOffset.UtcNow,
                _ => (TimeSpan?)null,
            };
            var status = response.StatusCode;
            response.Dispose();
            throw new BunnyTransientResponseException(status, retryAfter);
        }
    }

    /// <summary>
    /// Retry timing for Bunny uploads. Without jitter every process (VisualImporter, BunnyBulkUploader, the
    /// FileScanService) retried on the same 1/2/4 s schedule and re-sent whole videos in step; each wait is now
    /// half fixed, half random, and a server <c>Retry-After</c> always wins.
    /// </summary>
    public static class BunnyBackoff
    {
        public static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
        /// <summary>Upper bound on a server-requested wait, so a bad header can't park an upload for hours.</summary>
        public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RetryAfterJitter = TimeSpan.FromMilliseconds(500);

        public static bool IsRetryableStatus(HttpStatusCode status) =>
            status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)status >= 500;

        /// <summary>Network failures, timeouts and retryable responses.</summary>
        public static bool IsTransient(Exception error) =>
            error is HttpRequestException or WebException or TimeoutException or TaskCanceledException;

        /// <summary>The wait before retry number <paramref name="attempt"/> (1 = first retry).</summary>
        public static TimeSpan DelayFor(int attempt, Exception error, Random random)
        {
            if (error is BunnyTransientResponseException { RetryAfter: { } retryAfter })
            {
                // Never earlier than asked; a little spread so clients told the same instant don't return together.
                var asked = retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter > MaxRetryAfter ? MaxRetryAfter : retryAfter;
                return asked + RetryAfterJitter * random.NextDouble();
            }

            var exponential = BaseDelay * Math.Pow(2, Math.Max(0, attempt - 1));
            if (exponential > MaxDelay)
            {
                exponential = MaxDelay;
            }
            return exponential / 2 + exponential / 2 * random.NextDouble();
        }
    }
}

namespace AlUmmahNow.Data.Storage
{
    /// <summary>
    /// Passes reads and seeks through to the caller's stream but ignores Dispose. HttpClient disposes a request's
    /// content, and with it the stream, after every attempt; without this the first failed attempt closed the
    /// caller's stream and no retry could rewind it.
    /// </summary>
    internal sealed class LeaveOpenStream : Stream
    {
        private readonly Stream _inner;

        public LeaveOpenStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
