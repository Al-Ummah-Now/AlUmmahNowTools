using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AlUmmahNow.ToolsCore;

/// <summary>
/// Receives the OAuth redirect on 127.0.0.1 with a port the OS picks (RFC 8252 §7.3), answers the
/// browser with a short bilingual page and returns the full redirect URL. As Desktop Studio's.
/// </summary>
public sealed class LoopbackRedirect : IDisposable
{
    public const string CallbackPath = "/auth/callback";

    private readonly TcpListener _listener;
    private readonly string _successHtml;

    public LoopbackRedirect(string successMessageEn, string successMessageAr)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _successHtml = "<!doctype html><meta charset=utf-8><title>Al Ummah Now Tools</title>"
            + "<body style=\"font-family:system-ui,sans-serif;text-align:center;margin-top:20vh;background:#111;color:#eee\">"
            + "<p lang=en dir=ltr>" + WebUtility.HtmlEncode(successMessageEn) + "</p>"
            + "<p lang=ar dir=rtl>" + WebUtility.HtmlEncode(successMessageAr) + "</p>";
    }

    public int Port { get; }

    public string RedirectUri => "http://127.0.0.1:" + Port + CallbackPath;

    /// <summary>Waits for the browser to hit the callback; other paths (favicon) get 404.</summary>
    public async Task<string> WaitAsync(CancellationToken cancellationToken)
    {
        using (cancellationToken.Register(() => _listener.Stop()))
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ObjectDisposedException or SocketException or InvalidOperationException or OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new SignInFailedException(SignInFailure.Failed, "The local callback listener stopped", ex);
                }

                using (client)
                using (var stream = client.GetStream())
                {
                    var line = await ReadRequestLineAsync(stream, cancellationToken).ConfigureAwait(false);
                    var parts = line?.Split(' ');
                    var method = parts is { Length: >= 3 } ? parts[0] : "";
                    var path = parts is { Length: >= 3 } ? parts[1] : "";
                    if (!string.Equals(method, "GET", StringComparison.Ordinal)
                        || (!path.StartsWith(CallbackPath + "?", StringComparison.Ordinal) && path != CallbackPath)
                        || path.Any(c => c < ' ' || c == '\u007F'))
                    {
                        await WriteAsync(stream, "404 Not Found", "", cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await WriteAsync(stream, "200 OK", _successHtml, cancellationToken).ConfigureAwait(false);
                    return "http://127.0.0.1:" + Port + path;
                }
            }
        }
    }

    public void Dispose() => _listener.Stop();

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal)) return text[..text.IndexOf("\r\n", StringComparison.Ordinal)];
        }

        return null;
    }

    private static async Task WriteAsync(NetworkStream stream, string status, string body, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
