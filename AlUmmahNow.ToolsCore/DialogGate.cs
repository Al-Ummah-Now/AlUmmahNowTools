namespace AlUmmahNow.ToolsCore;

public enum DialogKind
{
    /// <summary>Tells something: waits its turn, and gives way to a question</summary>
    Notice,
    /// <summary>Someone's own click: a form or a question; not shown over another one</summary>
    Question,
    /// <summary>The window is being closed: whatever is open gives way, and nothing else is shown until it is answered</summary>
    Quit,
}

/// <summary>One dialog at a time for the whole app: the platform throws at a second one.</summary>
public sealed class DialogGate
{
    private sealed record Open(DialogKind Kind, Action Hide, TaskCompletionSource Closed);

    private readonly object _gate = new();
    private Open? _open;
    private int _quits;

    /// <param name="show">Shows it and answers when it is closed</param>
    /// <param name="hide">Closes it from outside; what <paramref name="show"/> answers then counts as no answer</param>
    /// <returns>False: it was not shown</returns>
    public async Task<(bool Shown, T? Answer)> ShowAsync<T>(DialogKind kind, Func<Task<T>> show, Action hide)
    {
        var mine = new Open(kind, hide, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        if (kind == DialogKind.Quit) lock (_gate) _quits++;
        try
        {
            while (true)
            {
                Open? other;
                lock (_gate)
                {
                    other = _open;
                    if (other is null && (kind == DialogKind.Quit || _quits == 0))
                    {
                        _open = mine;
                        break;
                    }
                }
                if (kind == DialogKind.Question && (other is null || other.Kind != DialogKind.Notice)) return (false, default);
                if (other is null) await Task.Delay(100).ConfigureAwait(true);
                else
                {
                    if (kind == DialogKind.Quit || (kind == DialogKind.Question && other.Kind == DialogKind.Notice)) other.Hide();
                    await other.Closed.Task.ConfigureAwait(true);
                }
            }
            try
            {
                return (true, await show().ConfigureAwait(true));
            }
            finally
            {
                lock (_gate) _open = null;
                mine.Closed.SetResult();
            }
        }
        finally
        {
            if (kind == DialogKind.Quit) lock (_gate) _quits--;
        }
    }
}
