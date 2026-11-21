namespace AlUmmahNow.ToolsCore;

/// <summary>Why a sign-in did not finish; the shell shows a message per reason.</summary>
public enum SignInFailure
{
    /// <summary>The browser was closed or the sign-in was cancelled there</summary>
    Cancelled,
    /// <summary>Nothing came back within the time limit</summary>
    TimedOut,
    /// <summary>The Auth server could not be reached</summary>
    Offline,
    /// <summary>Anything else (the server refused the code, the store is locked, …)</summary>
    Failed,
}

public sealed class SignInFailedException : Exception
{
    public SignInFailedException(SignInFailure reason, string message, Exception? inner = null)
        : base(message, inner) => Reason = reason;

    public SignInFailure Reason { get; }
}

/// <summary>Opens the system browser (sign-in never happens inside the app).</summary>
public interface IBrowserLauncher
{
    void Open(Uri uri);
}

public sealed class ShellBrowserLauncher : IBrowserLauncher
{
    public void Open(Uri uri) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
}
