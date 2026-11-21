using System.Net;
using System.Text;
using AlUmmahNow.Core;
using AlUmmahNow.ToolsCore;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>Plays the browser: follows the sign-in URL's redirect to the app's loopback listener.</summary>
internal sealed class FakeBrowser : IBrowserLauncher
{
    public Uri? Opened;
    /// <summary>What the "browser" does with the opened URL; null leaves it open (no redirect)</summary>
    public Func<Uri, Task>? Behaviour;

    public void Open(Uri uri)
    {
        Opened = uri;
        if (Behaviour != null) _ = Task.Run(() => Behaviour(uri));
    }

    /// <summary>Hits the redirect_uri the app put in the URL, with a code and state.</summary>
    public static Func<Uri, Task> Redirects(string query = "code=c1&state=s1") => async uri =>
    {
        var redirect = System.Web.HttpUtility.ParseQueryString(uri.Query)["redirect_uri"]!;
        using var http = new HttpClient();
        // A favicon first: the listener answers 404 and keeps waiting for the callback
        await http.GetAsync(new Uri(new Uri(redirect), "/favicon.ico"));
        await http.GetAsync(redirect + "?" + query);
    };
}

internal sealed class FakeAuth : IAuthBackend
{
    public ToolsAccount? Saved;
    public string? Token = "token-1";
    public Exception? TokenError;
    public Exception? CompleteError;
    public string? CompletedWith;
    public readonly List<string> SignedOut = new();
    public bool Disposed;

    public string BeginLogin(string redirectUri) =>
        "https://auth.example/authorize?client_id=x&redirect_uri=" + Uri.EscapeDataString(redirectUri) + "&code_challenge=abc";

    public Task<ToolsAccount> CompleteLoginAsync(string redirect)
    {
        CompletedWith = redirect;
        if (CompleteError != null) throw CompleteError;
        Saved = new ToolsAccount("usr_1", "staff@example.org", "Staff Member");
        return Task.FromResult(Saved);
    }

    public ToolsAccount? Active() => Saved;

    public Task<string?> AccessTokenAsync()
    {
        if (TokenError != null) throw TokenError;
        return Task.FromResult(Saved == null ? null : Token);
    }

    public Task SignOutAsync(string userId)
    {
        SignedOut.Add(userId);
        Saved = null;
        return Task.CompletedTask;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeHandler : HttpMessageHandler
{
    public readonly List<HttpRequestMessage> Requests = new();
    public Func<HttpRequestMessage, HttpResponseMessage> Answer = _ => Me("imports:manage");

    public static HttpResponseMessage Me(params string[] permissions) => Json(HttpStatusCode.OK,
        "{\"success\":true,\"result\":{\"userId\":\"usr_1\",\"email\":\"staff@example.org\",\"roles\":[\"admin\"],\"permissions\":[" +
        string.Join(",", permissions.Select(p => "\"" + p + "\"")) + "],\"contentPermissions\":[]}}");

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Answer(request));
    }
}
