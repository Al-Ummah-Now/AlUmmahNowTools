#if UPDATE_TEST
using AlUmmahNow.ToolsCore.Updates;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// CI's update test (scripts/update-test.mjs): every request goes to the local test server with its real host in the
/// path, so the host rules run on the real URLs and nothing leaves the machine. Writes each state to a log, and exits
/// the app once an update is staged. Compiled only with -p:UpdateTest=true.
/// </summary>
internal sealed class UpdateTestHandler : HttpMessageHandler
{
    private static readonly Uri Server = new(Environment.GetEnvironmentVariable("TOOLS_UPDATE_TEST_SERVER") ?? throw new InvalidOperationException("TOOLS_UPDATE_TEST_SERVER"));
    private static readonly string LogPath = Environment.GetEnvironmentVariable("TOOLS_UPDATE_TEST_LOG") ?? throw new InvalidOperationException("TOOLS_UPDATE_TEST_LOG");
    private readonly HttpMessageInvoker _inner = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var real = request.RequestUri!;
        request.RequestUri = new Uri(Server, $"/{real.Host}{real.PathAndQuery}");
        return _inner.SendAsync(request, cancellationToken);
    }

    public static void Watch(UpdateService service)
    {
        Log($"started {service.InstalledVersion} self={(service.CanSelfUpdate ? "true" : "false")}");
        var last = UpdateStage.Hidden;
        service.Changed += v =>
        {
            if (v.Stage != last) Log($"{v.Stage} {v.Version}");
            last = v.Stage;
            // The app closing is what installs a staged update; ProcessExit runs AppUpdates' own exit step
            if (v.Stage == UpdateStage.Ready) _ = Task.Run(async () =>
            {
                await Task.Delay(int.TryParse(Environment.GetEnvironmentVariable("TOOLS_UPDATE_TEST_EXIT_MS"), out var ms) ? ms : 1000);
                if (Environment.GetEnvironmentVariable("TOOLS_UPDATE_TEST_RECHECK") == "1")
                {
                    await service.CheckAsync(userAsked: true);
                    Log($"rechecked {service.View.Stage}");
                }
                Environment.Exit(0);
            });
        };
    }

    private static void Log(string line) => File.AppendAllText(LogPath, line + "\n");
}
#endif
