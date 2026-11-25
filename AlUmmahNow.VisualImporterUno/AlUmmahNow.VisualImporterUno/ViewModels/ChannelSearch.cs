using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.VisualImporterUno.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace AlUmmahNow.VisualImporterUno.ViewModels;

/// <summary>A row of a channel box's list: a channel, or (no channel) a word about the search.</summary>
public sealed record ChannelSuggestion(ImportChannel? Channel, string Title, string? Detail = null)
{
    /// <summary>As it is drawn: a list is a popup, which puts an Arabic line's pieces in the wrong order.</summary>
    public string Shown => Channel is null ? Loc.PopupLine(Title) : Title;

    public Visibility DetailShown => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>What a screen reader says, as it is read.</summary>
    public override string ToString() => Title;
}

/// <summary>
/// Looks channels up for a box that suggests them (Import, Social sources). The rows are replaced in one step and, once a search
/// has started, are never empty on the way to the next ones: emptied and filled in one go, Uno's AutoSuggestBox closes its list
/// and opens it again, and the first close's Closed event, which arrives later, closes it for good (the owner's report 2026-11-25).
/// </summary>
public sealed partial class ChannelSearch : ObservableObject, IDisposable
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ImportChannel>>> _find;
    private readonly Func<ImportChannel, string> _name;
    private readonly string _none;
    private readonly TimeSpan _pause;
    private readonly Action<Exception?> _ended;
    private CancellationTokenSource? _search;
    private IReadOnlyList<ChannelSuggestion>? _before;

    /// <param name="none">The row's words when nothing is found</param>
    /// <param name="pause">How long typing rests before the server is asked</param>
    /// <param name="ended">A search has ended, with what went wrong; not called for one that was cancelled</param>
    public ChannelSearch(Func<string, CancellationToken, Task<IReadOnlyList<ImportChannel>>> find, Func<ImportChannel, string> name, string none, TimeSpan pause, Action<Exception?> ended) =>
        (_find, _name, _none, _pause, _ended) = (find, name, none, pause, ended);

    [ObservableProperty]
    private IReadOnlyList<ChannelSuggestion> _rows = [];

    /// <summary>The channels last found.</summary>
    public IReadOnlyList<ImportChannel> Found { get; private set; } = [];

    /// <summary>What the channels last found were searched for; null: they were not found by a search</summary>
    private string? _foundFor;

    /// <summary>The channels last found are this text's.</summary>
    public bool Answers(string text) => _foundFor == text && Found.Count > 0;

    /// <summary>Shows these channels; a search that has not answered yet is given up.</summary>
    public void Show(IReadOnlyList<ImportChannel> channels)
    {
        _search?.Cancel();
        Put(channels, null);
    }

    private void Put(IReadOnlyList<ImportChannel> channels, string? foundFor)
    {
        _before = null;
        _foundFor = foundFor;
        Found = channels;
        Rows = channels.Select(c => new ChannelSuggestion(c, _name(c), c.OwnerEmail)).ToArray();
    }

    /// <summary>Looks channels up by name or handle, a moment after typing stops. The rows stay until the answer is there.</summary>
    public async Task SearchAsync(string query)
    {
        _search?.Cancel();
        var search = _search = new CancellationTokenSource();
        if (Found.Count == 0 && _before is null)
        {
            _before = Rows;
            Rows = [new ChannelSuggestion(null, Loc.Get("Channel_Searching"))];
        }
        try
        {
            await Task.Delay(_pause, search.Token);
            var found = await _find(query, search.Token);
            if (search.IsCancellationRequested) return;
            if (found.Count > 0)
            {
                Put(found, query);
            }
            else
            {
                _before = null;
                _foundFor = query;
                Found = [];
                Rows = [new ChannelSuggestion(null, _none)];
            }
            _ended(null);
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (search.IsCancellationRequested) return;
            Back();
            _ended(ex);
        }
    }

    /// <summary>No answer is wanted any more: a channel was chosen, or the list was closed.</summary>
    public void Cancel()
    {
        _search?.Cancel();
        Back();
    }

    /// <summary>Without "Searching…".</summary>
    private void Back()
    {
        if (_before is not { } before) return;
        _before = null;
        Rows = before;
    }

    public void Dispose() => _search?.Cancel();
}
