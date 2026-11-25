using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

public sealed class DialogGateTests
{
    private readonly DialogGate _gate = new();
    private int _open;
    private int _most;

    private sealed class Dialog
    {
        public readonly TaskCompletionSource<string> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<(bool Shown, string? Answer)> Result = null!;
    }

    private Dialog Show(DialogKind kind)
    {
        var dialog = new Dialog();
        dialog.Result = _gate.ShowAsync(kind, async () =>
        {
            _most = Math.Max(_most, Interlocked.Increment(ref _open));
            dialog.Shown.SetResult();
            try { return await dialog.Answer.Task; }
            finally { Interlocked.Decrement(ref _open); }
        }, () => dialog.Answer.TrySetResult("hidden"));
        return dialog;
    }

    [Theory]
    [InlineData(DialogKind.Question)]
    [InlineData(DialogKind.Quit)]
    public async Task A_question_over_another_one_is_not_shown(DialogKind first)
    {
        var one = Show(first);
        await one.Shown.Task;
        Assert.Equal((false, null), await Show(DialogKind.Question).Result);
        one.Answer.SetResult("yes");
        Assert.Equal((true, "yes"), await one.Result);
        // And then they are again
        var next = Show(DialogKind.Question);
        await next.Shown.Task;
        next.Answer.SetResult("no");
        Assert.Equal((true, "no"), await next.Result);
    }

    [Fact]
    public async Task A_notice_waits_its_turn_and_gives_way_to_a_question()
    {
        var question = Show(DialogKind.Question);
        await question.Shown.Task;
        var notice = Show(DialogKind.Notice);
        await Task.Delay(50);
        Assert.False(notice.Shown.Task.IsCompleted);
        question.Answer.SetResult("yes");
        await notice.Shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var asked = Show(DialogKind.Question);
        await asked.Shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal((true, "hidden"), await notice.Result);
        asked.Answer.SetResult("no");
        Assert.Equal((true, "no"), await asked.Result);
        Assert.Equal(1, _most);
    }

    [Theory]
    [InlineData(DialogKind.Notice)]
    [InlineData(DialogKind.Question)]
    public async Task Quit_takes_the_place_of_what_is_open_and_nothing_comes_between(DialogKind open)
    {
        var one = Show(open);
        await one.Shown.Task;
        var waiting = Show(DialogKind.Notice);
        var quit = Show(DialogKind.Quit);
        await quit.Shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal((true, "hidden"), await one.Result);
        Assert.False(waiting.Shown.Task.IsCompleted);
        Assert.Equal((false, null), await Show(DialogKind.Question).Result);
        quit.Answer.SetResult("keep");
        Assert.Equal((true, "keep"), await quit.Result);
        await waiting.Shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        waiting.Answer.SetResult("ok");
        await waiting.Result;
        Assert.Equal(1, _most);
    }

    [Fact]
    public async Task One_that_could_not_be_shown_leaves_the_gate_free()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _gate.ShowAsync<string>(DialogKind.Question, () => throw new InvalidOperationException("the window is closing"), () => { }));
        var next = Show(DialogKind.Question);
        await next.Shown.Task;
        next.Answer.SetResult("yes");
        Assert.Equal((true, "yes"), await next.Result);
    }

    [Fact]
    public async Task Never_two_at_once()
    {
        var all = Enumerable.Range(0, 60).Select(i => Task.Run(async () =>
        {
            var dialog = Show((DialogKind)(i % 3));
            _ = dialog.Shown.Task.ContinueWith(_ => Task.Delay(2).ContinueWith(_ => dialog.Answer.TrySetResult("ok")));
            await dialog.Result;
        })).ToArray();
        await Task.WhenAll(all).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, _most);
    }
}
