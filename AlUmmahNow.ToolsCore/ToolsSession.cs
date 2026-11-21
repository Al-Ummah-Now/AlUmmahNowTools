using AlUmmahNow.Core;

namespace AlUmmahNow.ToolsCore;

/// <summary>The signed-in account as the shell shows it.</summary>
public sealed record ToolsAccount(string UserId, string? Email, string? DisplayName);

/// <summary>The part of aun-core's AuthManager the app uses; faked in tests.</summary>
public interface IAuthBackend : IDisposable
{
    /// <summary>Starts a PKCE login for this loopback redirect and returns the URL to open</summary>
    string BeginLogin(string redirectUri);
    Task<ToolsAccount> CompleteLoginAsync(string redirect);
    /// <summary>The saved account, unless there is none or it needs a fresh sign-in</summary>
    ToolsAccount? Active();
    /// <summary>A fresh access token (refreshed by the core), or null when signed out</summary>
    Task<string?> AccessTokenAsync();
    Task SignOutAsync(string userId);
}

public enum SessionState
{
    /// <summary>Starting up: reading the saved sign-in</summary>
    Starting,
    SignedOut,
    /// <summary>The browser is open, waiting for the redirect</summary>
    SigningIn,
    /// <summary>Asking admin-api whether the account is staff</summary>
    Checking,
    Staff,
    NotStaff,
    /// <summary>Signed in, but admin-api could not be reached: Try again</summary>
    Unreachable,
}

/// <summary>
/// Sign-in for one environment (wiki TOOLS-ADMIN TA-10): the system browser with PKCE and a
/// loopback redirect (aun-core), tokens in the OS store, then the staff check against admin-api.
/// Only an account with <c>imports:manage</c> gets past the sign-in screen.
/// </summary>
public sealed class ToolsSession : IDisposable
{
    private readonly IAuthBackend _auth;
    private readonly AdminApiClient _api;
    private readonly IBrowserLauncher _browser;
    private readonly TimeSpan _timeout;
    private readonly (string En, string Ar) _successPage;
    private CancellationTokenSource? _signIn;
    private Uri? _pendingSignIn;

    public ToolsSession(ToolsEnvironment environment, IAuthBackend auth, AdminApiClient api, IBrowserLauncher browser, (string En, string Ar) successPage, TimeSpan? timeout = null)
    {
        Environment = environment;
        _auth = auth;
        _api = api;
        _browser = browser;
        _successPage = successPage;
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public ToolsEnvironment Environment { get; }
    public SessionState State { get; private set; } = SessionState.Starting;
    public ToolsAccount? Account { get; private set; }
    public StaffProfile? Profile { get; private set; }
    /// <summary>Why the last sign-in failed, until the next one starts</summary>
    public SignInFailure? LastFailure { get; private set; }

    public event EventHandler? Changed;

    public AdminApiClient Api => _api;

    /// <summary>At start: a saved sign-in goes straight to the staff check.</summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        ToolsAccount? saved;
        try
        {
            saved = _auth.Active();
        }
        catch (AunException)
        {
            saved = null;
        }

        if (saved is null)
        {
            Set(SessionState.SignedOut, null);
            return;
        }

        Account = saved;
        await CheckAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Opens the browser and waits for the redirect, then checks staff.</summary>
    public async Task SignInAsync(CancellationToken ct = default)
    {
        // A second Sign in replaces the first attempt (its listener closes)
        _signIn?.Cancel();
        using var user = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var timeout = new CancellationTokenSource(_timeout);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(user.Token, timeout.Token);
        _signIn = user;
        LastFailure = null;
        Set(SessionState.SigningIn, null);
        SignInFailure? failure = null;
        try
        {
            using var loopback = new LoopbackRedirect(_successPage.En, _successPage.Ar);
            var url = new Uri(_auth.BeginLogin(loopback.RedirectUri));
            _pendingSignIn = url;
            _browser.Open(url);
            var redirect = await loopback.WaitAsync(both.Token).ConfigureAwait(false);
            Account = await _auth.CompleteLoginAsync(redirect).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            failure = timeout.IsCancellationRequested && !user.IsCancellationRequested ? SignInFailure.TimedOut : SignInFailure.Cancelled;
        }
        catch (AunException.Cancelled)
        {
            failure = SignInFailure.Cancelled;
        }
        catch (AunException.Network)
        {
            failure = SignInFailure.Offline;
        }
        catch (SignInFailedException ex)
        {
            failure = timeout.IsCancellationRequested ? SignInFailure.TimedOut : user.IsCancellationRequested ? SignInFailure.Cancelled : ex.Reason;
        }
        catch (AunException)
        {
            failure = SignInFailure.Failed;
        }

        var superseded = !ReferenceEquals(_signIn, user);
        if (!superseded)
        {
            _signIn = null;
            _pendingSignIn = null;
        }
        // A replaced attempt leaves the state to the one that replaced it
        if (superseded) return;
        if (failure is { } reason)
        {
            Fail(reason);
            return;
        }

        await CheckAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Stops a sign-in that is waiting for the browser.</summary>
    public void CancelSignIn() => _signIn?.Cancel();

    /// <summary>Opens the waiting sign-in page again, for a browser tab that was closed or lost.</summary>
    public bool ReopenSignInPage()
    {
        if (State != SessionState.SigningIn || _pendingSignIn is not { } url) return false;
        _browser.Open(url);
        return true;
    }

    /// <summary>Asks admin-api who the account is; also the "Try again" of the Unreachable screen.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        Set(SessionState.Checking, null);
        string? token;
        try
        {
            token = await _auth.AccessTokenAsync().ConfigureAwait(false);
        }
        catch (AunException.Network)
        {
            Set(SessionState.Unreachable, null);
            return;
        }
        catch (AunException)
        {
            token = null;
        }

        if (token is null)
        {
            // The refresh token was refused (expired, revoked, or not staff on refresh)
            Account = null;
            Set(SessionState.SignedOut, null);
            return;
        }

        var check = await _api.CheckStaffAsync(token, ct).ConfigureAwait(false);
        switch (check.Outcome)
        {
            case StaffCheckOutcome.Staff:
                Set(SessionState.Staff, check.Profile);
                break;
            case StaffCheckOutcome.NotStaff:
                Set(SessionState.NotStaff, check.Profile);
                break;
            case StaffCheckOutcome.SignedOut:
                await SignOutAsync().ConfigureAwait(false);
                break;
            default:
                Set(SessionState.Unreachable, null);
                break;
        }
    }

    /// <summary>Signs out here and revokes the refresh token at Auth (RFC 7009).</summary>
    public async Task SignOutAsync()
    {
        var id = Account?.UserId;
        Account = null;
        if (id is not null)
        {
            try
            {
                await _auth.SignOutAsync(id).ConfigureAwait(false);
            }
            catch (AunException)
            {
                // The tokens are deleted locally even when Auth can't be reached to revoke them
            }
        }

        Set(SessionState.SignedOut, null);
    }

    /// <summary>A token for admin-api calls, or null when signed out.</summary>
    public async Task<string?> AccessTokenAsync()
    {
        try
        {
            return await _auth.AccessTokenAsync().ConfigureAwait(false);
        }
        catch (AunException)
        {
            return null;
        }
    }

    private void Fail(SignInFailure reason)
    {
        LastFailure = reason;
        Account = null;
        Set(SessionState.SignedOut, null);
    }

    private void Set(SessionState state, StaffProfile? profile)
    {
        State = state;
        Profile = profile;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _signIn?.Cancel();
        _auth.Dispose();
    }
}

/// <summary><see cref="IAuthBackend"/> on aun-core's AuthManager (as Desktop Studio and Admin).</summary>
public sealed class CoreAuthBackend : IAuthBackend
{
    private static readonly string[] Scopes = { "openid", "profile", "email", "offline_access" };
    /// <summary>The client's registered loopback redirect (no port); each sign-in passes its own port</summary>
    public const string RegisteredRedirect = "http://127.0.0.1" + LoopbackRedirect.CallbackPath;

    private readonly AuthManager _manager;

    public CoreAuthBackend(ToolsEnvironment environment, ISecureStore store)
    {
        _manager = new AuthManager(
            new AuthConfig(environment.Issuer.AbsoluteUri.TrimEnd('/'), environment.ClientId, RegisteredRedirect, Scopes, null, null, null),
            new SecureStorageAdapter(store));
    }

    public string BeginLogin(string redirectUri) => _manager.BeginLoginWithRedirect(LoginPrompt.Default, redirectUri).Url;

    public async Task<ToolsAccount> CompleteLoginAsync(string redirect)
    {
        var info = await _manager.CompleteLogin(redirect).ConfigureAwait(false);
        return new ToolsAccount(info.UserId, info.Email, info.DisplayName);
    }

    public ToolsAccount? Active()
    {
        var a = _manager.ActiveAccount();
        return a is { NeedsReauth: false } ? new ToolsAccount(a.UserId, a.Email, a.DisplayName) : null;
    }

    public async Task<string?> AccessTokenAsync() => await _manager.AccessToken().ConfigureAwait(false);

    public Task SignOutAsync(string userId) => _manager.SignOutAndRevoke(userId);

    public void Dispose() => _manager.Dispose();
}
