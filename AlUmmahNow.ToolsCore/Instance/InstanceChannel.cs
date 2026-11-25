using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace AlUmmahNow.ToolsCore.Instance;

/// <summary>What the running copy answers a second one that asks it to come forward.</summary>
public enum FrontAnswer
{
    /// <summary>Its window is in front</summary>
    Shown,
    /// <summary>It is closing: the second one may start once it has gone</summary>
    Quitting,
}

/// <summary>
/// How a second copy asks the running one to come forward: a pipe named after the queue folder, for this user only. The
/// running copy says its process id, hears "front", and answers "shown" or "quitting". Nothing else can be asked of it.
/// </summary>
public static class InstanceChannel
{
    private static readonly TimeSpan ClientLimit = TimeSpan.FromSeconds(5);

    /// <summary>Short: on macOS and Linux it becomes a socket's path, which may have about 100 characters.</summary>
    public static string NameFor(string folder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) full = full.ToUpperInvariant();
        return "aun-tools-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12];
    }

    /// <summary>Answers second copies until <paramref name="ct"/>. Only the copy that holds the folder's lock listens.</summary>
    public static async Task ListenAsync(string folder, Func<Task<FrontAnswer>> bringForward, Action<Exception>? failed = null, CancellationToken ct = default)
    {
        NamedPipeServerStream Create() => new(NameFor(folder), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        NamedPipeServerStream? waiting = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    waiting ??= Create();
                    await waiting.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    // The next one waits before this one is answered and closed: on macOS and Linux the last one to close
                    // takes the socket away, with whoever is just connecting
                    var connected = waiting;
                    waiting = null;
                    try
                    {
                        waiting = Create();
                    }
                    finally
                    {
                        _ = AnswerAsync(connected, bringForward, failed, ct);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
                {
                    failed?.Invoke(ex);
                    waiting?.Dispose();
                    waiting = null;
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is ending
        }
        finally
        {
            waiting?.Dispose();
        }
    }

    private static async Task AnswerAsync(NamedPipeServerStream pipe, Func<Task<FrontAnswer>> bringForward, Action<Exception>? failed, CancellationToken ct)
    {
        try
        {
            await using (pipe.ConfigureAwait(false))
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(ClientLimit);
                await WriteAsync(pipe, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), limit.Token).ConfigureAwait(false);
                if (await ReadAsync(pipe, limit.Token).ConfigureAwait(false) != "front") return;
                var answer = await bringForward().WaitAsync(limit.Token).ConfigureAwait(false);
                await WriteAsync(pipe, answer == FrontAnswer.Shown ? "shown" : "quitting", limit.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // A second copy that went away, or a window that did not answer: it makes do without an answer
            failed?.Invoke(ex);
        }
    }

    /// <summary>
    /// Asks the running copy to come forward; null when it does not answer. <paramref name="allow"/> gets its process id
    /// first: on Windows only the copy the person just started may hand over the foreground.
    /// </summary>
    public static async Task<FrontAnswer?> AskAsync(string folder, Action<int>? allow = null, TimeSpan? limit = null)
    {
        using var ct = new CancellationTokenSource(limit ?? TimeSpan.FromSeconds(4));
        try
        {
            var pipe = new NamedPipeClientStream(".", NameFor(folder), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await using (pipe.ConfigureAwait(false))
            {
                await pipe.ConnectAsync(ct.Token).ConfigureAwait(false);
                if (!int.TryParse(await ReadAsync(pipe, ct.Token).ConfigureAwait(false), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) return null;
                allow?.Invoke(pid);
                await WriteAsync(pipe, "front", ct.Token).ConfigureAwait(false);
                return await ReadAsync(pipe, ct.Token).ConfigureAwait(false) switch { "shown" => FrontAnswer.Shown, "quitting" => FrontAnswer.Quitting, _ => null };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or TimeoutException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task WriteAsync(Stream stream, string line, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\n"), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>A short line, or null when the other side ended or sent something else.</summary>
    private static async Task<string?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var line = new StringBuilder();
        var one = new byte[1];
        while (line.Length <= 32)
        {
            if (await stream.ReadAsync(one, ct).ConfigureAwait(false) == 0) return null;
            if (one[0] == '\n') return line.ToString();
            line.Append((char)one[0]);
        }
        return null;
    }
}
