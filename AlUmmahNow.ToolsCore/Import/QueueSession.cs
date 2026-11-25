namespace AlUmmahNow.ToolsCore.Import;

/// <summary>Whether the queue runs, by who is signed in.</summary>
public enum QueueSession
{
    /// <summary>As it is: the account is being checked, or admin-api could not be reached</summary>
    AsItIs,
    Run,
    /// <summary>Nothing runs signed out, or for an account that is not staff</summary>
    Stop,
}

public static class QueueSessions
{
    public static QueueSession For(SessionState state, string? account) => (state, account) switch
    {
        (SessionState.Staff, not null) => QueueSession.Run,
        (SessionState.SignedOut or SessionState.SigningIn or SessionState.NotStaff, _) => QueueSession.Stop,
        _ => QueueSession.AsItIs,
    };
}
