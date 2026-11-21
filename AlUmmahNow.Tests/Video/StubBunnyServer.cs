using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AlUmmahNow.Tests.Video
{
    /// <summary>
    /// A local stand-in for the Bunny Stream API: every request gets the same status and JSON body,
    /// so client tests never reach the real API (whose answers to a fake key can change).
    /// </summary>
    public sealed class StubBunnyServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        public string BaseUrl { get; }
        public string? LastAccessKey { get; private set; }
        public string? LastPath { get; private set; }

        public StubBunnyServer(HttpStatusCode status, string json)
        {
            var port = FreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch (Exception) { return; }
                    LastAccessKey = ctx.Request.Headers["AccessKey"];
                    LastPath = ctx.Request.Url?.AbsolutePath;
                    var bytes = Encoding.UTF8.GetBytes(json);
                    ctx.Response.StatusCode = (int)status;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            });
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
        }
    }
}
