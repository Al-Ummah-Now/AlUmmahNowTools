using System.Net;
using AlUmmahNow.Core;
using AlUmmahNow.ToolsCore;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

public class EnvironmentTests
{
    [Fact]
    public void Each_environment_has_its_issuer_admin_api_client_and_own_token_store()
    {
        Assert.Equal("https://auth.alummahnow.net/", ToolsEnvironment.Production.Issuer.AbsoluteUri);
        Assert.Equal("https://admin.api.stage.alummahnow.net/", ToolsEnvironment.Stage.AdminApi.AbsoluteUri);
        Assert.Equal("75f36653-4dfd-44af-8f67-588a5b7e9613", ToolsEnvironment.Qa.ClientId);
        Assert.Equal(ToolsEnvironment.Production.ClientId, ToolsEnvironment.Stage.ClientId);
        Assert.Equal(3, ToolsEnvironment.All.Select(e => e.StoreName).Distinct().Count());
        Assert.Same(ToolsEnvironment.Qa, ToolsEnvironment.For(ToolsEnvironmentKind.Qa));
    }
}

public class PreferencesTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "aun-tools-" + Guid.NewGuid().ToString("N"), "preferences.json");

    [Fact]
    public void Defaults_are_english_the_device_theme_production_and_survive_a_round_trip()
    {
        var path = TempFile();
        var p = ToolsPreferences.Load(path);
        Assert.Equal(("en", (ToolsTheme?)null, ToolsEnvironmentKind.Production), (p.Language, p.Theme, p.Environment));
        Assert.Equal(ToolsTheme.Light, p.EffectiveTheme(deviceIsDark: false));
        Assert.Equal(ToolsTheme.Dark, p.EffectiveTheme(deviceIsDark: true));
        p.Language = "ar";
        p.Theme = ToolsTheme.Light;
        p.Environment = ToolsEnvironmentKind.Qa;
        p.Save(path);
        var again = ToolsPreferences.Load(path);
        Assert.Equal(("ar", (ToolsTheme?)ToolsTheme.Light, ToolsEnvironmentKind.Qa), (again.Language, again.Theme, again.Environment));
        Assert.Equal(ToolsTheme.Light, again.EffectiveTheme(deviceIsDark: true));
        Assert.True(again.IsRightToLeft);
    }

    [Fact]
    public void A_broken_or_odd_file_falls_back_to_defaults()
    {
        var path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json");
        Assert.Equal("en", ToolsPreferences.Load(path).Language);
        File.WriteAllText(path, "{\"Language\":\"fr\",\"Theme\":\"Light\"}");
        var p = ToolsPreferences.Load(path);
        Assert.Equal("en", p.Language);
        Assert.Equal(ToolsTheme.Light, p.Theme);
    }
}

public class SecureStoreTests
{
    private sealed class StringItemStore : SingleItemSecureStore
    {
        public string? Item;
        protected override string? ReadItem() => Item;
        protected override void WriteItem(string value) => Item = value;
    }

    [Fact]
    public void One_item_holds_every_key_and_lists_by_prefix()
    {
        var store = new StringItemStore();
        store.Set("aun.account.usr_1", new byte[] { 1, 2, 3 });
        store.Set("aun.active", new byte[] { 9 });
        Assert.Equal(new byte[] { 1, 2, 3 }, store.Get("aun.account.usr_1"));
        Assert.Equal(new[] { "aun.account.usr_1" }, store.List("aun.account."));
        store.Delete("aun.account.usr_1");
        Assert.Null(store.Get("aun.account.usr_1"));
        Assert.DoesNotContain("AQID", store.Item);
    }

    [Fact]
    public void An_unreadable_item_is_a_storage_error_not_an_empty_store()
    {
        var store = new StringItemStore { Item = "garbage" };
        Assert.Throws<IOException>(() => store.Get("k"));
    }

    [Fact]
    public void The_macOS_keychain_store_round_trips()
    {
        // Writes to the login keychain: only when asked (TOOLS_KEYCHAIN_TEST=1 on a Mac)
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("TOOLS_KEYCHAIN_TEST") != "1") return;
        var account = "test-" + Guid.NewGuid().ToString("N");
        var store = new KeychainSecureStore("net.alummahnow.tools.test", account);
        store.Set("a", new byte[] { 7, 8 });
        Assert.Equal(new byte[] { 7, 8 }, store.Get("a"));
        store.Set("a", new byte[] { 9 });
        Assert.Equal(new byte[] { 9 }, store.Get("a"));
        store.Delete("a");
        Assert.Null(store.Get("a"));
        System.Diagnostics.Process.Start("/usr/bin/security", new[] { "delete-generic-password", "-s", "net.alummahnow.tools.test", "-a", account })!.WaitForExit();
    }
}

public class LoopbackTests
{
    [Fact]
    public async Task Returns_the_callback_url_and_ignores_other_paths()
    {
        using var loopback = new LoopbackRedirect("Signed in", "تم تسجيل الدخول");
        Assert.StartsWith("http://127.0.0.1:", loopback.RedirectUri);
        Assert.EndsWith("/auth/callback", loopback.RedirectUri);
        var wait = loopback.WaitAsync(CancellationToken.None);
        using var http = new HttpClient();
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(new Uri(loopback.RedirectUri), "/favicon.ico"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(loopback.RedirectUri + "extra?code=x")).StatusCode);
        var page = await http.GetStringAsync(loopback.RedirectUri + "?code=c1&state=s1");
        Assert.Contains("تم تسجيل الدخول", page);
        Assert.Equal(loopback.RedirectUri + "?code=c1&state=s1", await wait);
    }

    [Fact]
    public async Task Cancelling_stops_the_wait()
    {
        using var loopback = new LoopbackRedirect("ok", "ok");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loopback.WaitAsync(cts.Token));
    }
}

public class AdminApiTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, StaffCheckOutcome.NotStaff)]
    [InlineData(HttpStatusCode.Unauthorized, StaffCheckOutcome.SignedOut)]
    [InlineData(HttpStatusCode.InternalServerError, StaffCheckOutcome.Unreachable)]
    public async Task Status_codes_map_to_outcomes(HttpStatusCode status, StaffCheckOutcome expected)
    {
        var handler = new FakeHandler { Answer = _ => FakeHandler.Json(status, "{}") };
        var check = await new AdminApiClient(new Uri("https://admin.example"), handler).CheckStaffAsync("t");
        Assert.Equal(expected, check.Outcome);
    }

    [Fact]
    public async Task Staff_needs_imports_manage_and_the_token_is_sent()
    {
        var handler = new FakeHandler();
        var api = new AdminApiClient(new Uri("https://admin.example"), handler);
        var check = await api.CheckStaffAsync("tok");
        Assert.Equal(StaffCheckOutcome.Staff, check.Outcome);
        Assert.Equal("usr_1", check.Profile!.UserId);
        Assert.Equal("/api/admin/import/me", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("Bearer tok", handler.Requests[0].Headers.Authorization!.ToString());
        handler.Answer = _ => FakeHandler.Me("videos:manage");
        Assert.Equal(StaffCheckOutcome.NotStaff, (await api.CheckStaffAsync("tok")).Outcome);
        handler.Answer = _ => throw new HttpRequestException("offline");
        Assert.Equal(StaffCheckOutcome.Unreachable, (await api.CheckStaffAsync("tok")).Outcome);
    }
}

public class SessionTests
{
    private static (ToolsSession Session, FakeAuth Auth, FakeBrowser Browser, FakeHandler Api) Make(TimeSpan? timeout = null)
    {
        var auth = new FakeAuth();
        var browser = new FakeBrowser { Behaviour = FakeBrowser.Redirects() };
        var api = new FakeHandler();
        var session = new ToolsSession(ToolsEnvironment.Qa, auth, new AdminApiClient(ToolsEnvironment.Qa.AdminApi, api), browser, ("Signed in", "تم"), timeout);
        return (session, auth, browser, api);
    }

    [Fact]
    public async Task Sign_in_through_the_browser_then_the_staff_check_opens_the_app()
    {
        var (session, auth, browser, _) = Make();
        var states = new List<SessionState>();
        session.Changed += (_, _) => states.Add(session.State);
        await session.SignInAsync();
        Assert.Equal(SessionState.Staff, session.State);
        Assert.Equal("usr_1", session.Profile!.UserId);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A", browser.Opened!.Query);
        Assert.Matches(@"^http://127\.0\.0\.1:\d+/auth/callback\?code=c1&state=s1$", auth.CompletedWith!);
        Assert.Equal(new[] { SessionState.SigningIn, SessionState.Checking, SessionState.Staff }, states);
    }

    [Fact]
    public async Task A_signed_in_account_without_imports_manage_is_told_the_app_is_for_staff()
    {
        var (session, _, _, api) = Make();
        api.Answer = _ => FakeHandler.Json(HttpStatusCode.Forbidden, "{\"success\":false}");
        await session.SignInAsync();
        Assert.Equal(SessionState.NotStaff, session.State);
        Assert.NotNull(session.Account);
    }

    [Fact]
    public async Task A_saved_sign_in_is_restored_and_rechecked()
    {
        var (session, auth, browser, _) = Make();
        auth.Saved = new ToolsAccount("usr_1", null, null);
        await session.RestoreAsync();
        Assert.Equal(SessionState.Staff, session.State);
        Assert.Null(browser.Opened);
        var (fresh, _, _, _) = Make();
        await fresh.RestoreAsync();
        Assert.Equal(SessionState.SignedOut, fresh.State);
    }

    [Fact]
    public async Task A_refused_token_signs_out_and_revokes()
    {
        var (session, auth, _, api) = Make();
        auth.Saved = new ToolsAccount("usr_1", null, null);
        api.Answer = _ => FakeHandler.Json(HttpStatusCode.Unauthorized, "{}");
        await session.RestoreAsync();
        Assert.Equal(SessionState.SignedOut, session.State);
        Assert.Equal(new[] { "usr_1" }, auth.SignedOut);
        // An expired refresh (no token at all) is signed out too
        var (other, otherAuth, _, _) = Make();
        otherAuth.Saved = new ToolsAccount("usr_2", null, null);
        otherAuth.Token = null;
        await other.RestoreAsync();
        Assert.Equal(SessionState.SignedOut, other.State);
    }

    [Fact]
    public async Task Admin_api_down_keeps_the_account_with_try_again()
    {
        var (session, auth, _, api) = Make();
        auth.Saved = new ToolsAccount("usr_1", null, null);
        api.Answer = _ => throw new HttpRequestException("down");
        await session.RestoreAsync();
        Assert.Equal(SessionState.Unreachable, session.State);
        api.Answer = _ => FakeHandler.Me("imports:manage");
        await session.CheckAsync();
        Assert.Equal(SessionState.Staff, session.State);
        Assert.Empty(auth.SignedOut);
    }

    [Fact]
    public async Task Cancel_timeout_and_a_refused_code_say_why()
    {
        var (session, _, browser, _) = Make();
        browser.Behaviour = null;
        var waiting = session.SignInAsync();
        await Task.Delay(100);
        session.CancelSignIn();
        await waiting;
        Assert.Equal((SessionState.SignedOut, SignInFailure.Cancelled), (session.State, session.LastFailure));

        var (slow, _, slowBrowser, _) = Make(TimeSpan.FromMilliseconds(150));
        slowBrowser.Behaviour = null;
        await slow.SignInAsync();
        Assert.Equal(SignInFailure.TimedOut, slow.LastFailure);

        var (refused, refusedAuth, _, _) = Make();
        refusedAuth.CompleteError = new AunException.Network("dns");
        await refused.SignInAsync();
        Assert.Equal(SignInFailure.Offline, refused.LastFailure);
    }

    [Fact]
    public async Task Sign_out_revokes_and_clears()
    {
        var (session, auth, _, _) = Make();
        await session.SignInAsync();
        await session.SignOutAsync();
        Assert.Equal(SessionState.SignedOut, session.State);
        Assert.Null(session.Account);
        Assert.Equal(new[] { "usr_1" }, auth.SignedOut);
    }
}
