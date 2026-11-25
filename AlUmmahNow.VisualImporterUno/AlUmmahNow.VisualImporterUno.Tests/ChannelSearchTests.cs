using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.VisualImporterUno.ViewModels;
using FluentAssertions;
using Xunit;

namespace AlUmmahNow.VisualImporterUno.Tests;

/// <summary>The rows of a channel box: replaced in one step, never empty on the way (an emptied list closes, and stays closed).</summary>
public sealed class ChannelSearchTests
{
    private readonly Dictionary<string, TaskCompletionSource<IReadOnlyList<ImportChannel>>> _answers = new();
    private readonly List<IReadOnlyList<ChannelSuggestion>> _rows = new();
    private readonly List<Exception?> _ended = new();
    private readonly ChannelSearch _search;

    public ChannelSearchTests()
    {
        _search = new ChannelSearch((query, ct) => Answer(query).Task, c => c.Name, "none", TimeSpan.Zero, _ended.Add);
        _search.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChannelSearch.Rows)) _rows.Add(_search.Rows); };
    }

    private TaskCompletionSource<IReadOnlyList<ImportChannel>> Answer(string query) =>
        _answers.TryGetValue(query, out var answer) ? answer : _answers[query] = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ImportChannel Channel(string name) => new(name, name, name, name + "@example.com");

    private async Task Search(string query, params string[] found)
    {
        Answer(query).SetResult(found.Select(Channel).ToArray());
        await _search.SearchAsync(query);
    }

    private IEnumerable<string> Titles => _search.Rows.Select(r => r.Title);

    [Fact]
    public async Task NewResults_ReplaceTheOldInOneStep_WithoutEverBeingEmpty()
    {
        await Search("a", "one", "two");
        _rows.Clear();
        await Search("ab", "two", "three");
        _rows.Should().ContainSingle().Which.Select(r => r.Title).Should().Equal("two", "three");
    }

    [Fact]
    public async Task TheFirstSearch_SaysSearching_ThenWhatItFound_NeverEmpty()
    {
        await Search("a", "one");
        _rows.Should().HaveCount(2).And.OnlyContain(rows => rows.Count == 1);
        _rows[0][0].Channel.Should().BeNull();
        _rows[1][0].Channel!.Name.Should().Be("one");
    }

    [Fact]
    public async Task NothingFound_IsARow_AndTheNextSearchSaysSearching()
    {
        await Search("a", "one");
        await Search("zz");
        Titles.Should().Equal("none");
        _search.Rows[0].Channel.Should().BeNull();
        _search.Found.Should().BeEmpty();
        await Search("ab", "two");
        Titles.Should().Equal("two");
        _rows.Should().OnlyContain(rows => rows.Count > 0);
    }

    [Fact]
    public async Task AnOlderAnswer_NeverReplacesANewerOne()
    {
        var older = _search.SearchAsync("a");
        await Task.Delay(50);
        await Search("ab", "new");
        Answer("a").SetResult([Channel("old")]);
        await older;
        Titles.Should().Equal("new");
        _ended.Should().Equal(new Exception?[] { null });
    }

    [Fact]
    public async Task Cancelled_WhileSearching_TheRowsAreAsBefore_AndTheAnswerChangesNothing()
    {
        var search = _search.SearchAsync("a");
        await Task.Delay(50);
        _search.Cancel();
        _search.Rows.Should().BeEmpty();
        Answer("a").SetResult([Channel("late")]);
        await search;
        _search.Rows.Should().BeEmpty();
        _ended.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelledWithResultsShown_TheyStay()
    {
        await Search("a", "one");
        var search = _search.SearchAsync("ab");
        _search.Cancel();
        Answer("ab").SetResult([Channel("late")]);
        await search;
        Titles.Should().Equal("one");
    }

    [Fact]
    public async Task AFailedSearch_SaysWhy_AndKeepsWhatWasShown()
    {
        await Search("a", "one");
        Answer("ab").SetException(new HttpRequestException("no answer"));
        await _search.SearchAsync("ab");
        Titles.Should().Equal("one");
        _ended.Last().Should().BeOfType<HttpRequestException>();
        // The server's own time limit is a failure too, not a search given up
        Answer("abc").SetException(new TaskCanceledException("too slow"));
        await _search.SearchAsync("abc");
        _ended.Last().Should().BeOfType<TaskCanceledException>();
    }
}
