using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// A box that suggests channels: typing searches, the arrow keys look through the list, Enter and a click choose, Escape and
/// leaving the box give up. Uno opens the list at every change of the text or the rows while the box has the focus: a list that
/// was closed stays closed, and its search is given up, until something is typed or the box is moved to again.
/// </summary>
internal sealed class ChannelBoxes
{
    private readonly AutoSuggestBox _box;
    private readonly ChannelSearch _search;
    private readonly Action<ImportChannel> _choose;
    private bool _closed;
    /// <summary>The text is this row's, which the arrow keys are on (or a click is about to choose), not chosen yet</summary>
    private ChannelSuggestion? _looking;
    /// <summary>The text before the arrow keys</summary>
    private string _typed = "";
    /// <summary>The text a choice or Escape left: moving back to it opens nothing</summary>
    private string? _settled;

    private ChannelBoxes(AutoSuggestBox box, ChannelSearch search, Action<ImportChannel> choose) => (_box, _search, _choose) = (box, search, choose);

    public static ChannelBoxes Attach(AutoSuggestBox box, ChannelSearch search, Action<ImportChannel> choose)
    {
        var boxes = new ChannelBoxes(box, search, choose);
        // A row that only says something about the search never becomes the text
        box.UpdateTextOnSelect = false;
        box.TextChanged += (_, args) => boxes.TextChanged(args.Reason);
        box.GotFocus += (_, e) => boxes.GotFocus(e.OriginalSource);
        box.LostFocus += (_, _) => box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, boxes.Left);
        box.SuggestionChosen += (_, args) => boxes.LookAt(args.SelectedItem);
        box.QuerySubmitted += (_, args) => boxes.Submit(args.ChosenSuggestion);
        box.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == VirtualKey.Escape) boxes.Escape();
        }), true);
        box.RegisterPropertyChangedCallback(AutoSuggestBox.IsSuggestionListOpenProperty, (_, _) =>
        {
            // Not at once: Uno opens it for what was typed before it tells, and closed and opened in one go it would be closed for good
            if (boxes._closed && box.IsSuggestionListOpen) box.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (boxes._closed) box.IsSuggestionListOpen = false;
            });
        });
        return boxes;
    }

    /// <summary>Closes the list, and nothing that is still to come opens it.</summary>
    public void Close()
    {
        _closed = true;
        _search.Cancel();
        _box.IsSuggestionListOpen = false;
        Deselect();
    }

    /// <summary>The arrow keys begin at the first row again.</summary>
    private void Deselect()
    {
        if (Find<Microsoft.UI.Xaml.Controls.Primitives.Popup>(_box)?.Child is { } child && (child as ListView ?? Find<ListView>(child)) is { SelectedIndex: >= 0 } list) list.SelectedIndex = -1;
    }

    private static T? Find<T>(DependencyObject parent) where T : class
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private void TextChanged(AutoSuggestionBoxTextChangeReason reason)
    {
        // On macOS the Tab that leaves the box is also typed into it
        if (_box.Text.Contains('\t'))
        {
            if (_looking is not null) BackToTyped();
            else _box.Text = _box.Text.Replace("\t", "");
        }
        else if (reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            (_closed, _looking, _settled, _typed) = (false, null, null, _box.Text);
            Deselect();
            _ = _search.SearchAsync(_box.Text);
            _box.IsSuggestionListOpen = true;
        }
        // By the arrow keys the list stays; anything else that puts a text here ends what was typed before
        else if (_looking is null)
        {
            Close();
        }
    }

    /// <summary>Suggestions when the admin moves to the box, never when the page puts the focus there itself.</summary>
    private void GotFocus(object source)
    {
        if (source is not TextBox { FocusState: not FocusState.Programmatic } || _looking is not null || _box.Text == _settled) return;
        (_closed, _settled, _typed) = (false, null, _box.Text);
        if (_search.Answers(_box.Text)) _box.IsSuggestionListOpen = true;
        else _ = _search.SearchAsync(_box.Text);
    }

    private void Left()
    {
        if (_box.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is TextBox text && Within(text)) return;
        BackToTyped();
        Close();
    }

    private bool Within(DependencyObject? element)
    {
        for (; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
            if (element == _box) return true;
        return false;
    }

    /// <summary>The arrow keys are on this row: its name is shown, nothing is chosen.</summary>
    private void LookAt(object? row)
    {
        if (row is not ChannelSuggestion { Channel: not null } channel) return;
        if (_looking is null) _typed = _box.Text;
        _looking = channel;
        _box.Text = channel.Title;
    }

    /// <summary>
    /// Enter or a click: the row the arrow keys are on, or the one clicked, is the choice. Never another one: Uno's list keeps a
    /// row selected after the arrow keys were given up, and hands it over.
    /// </summary>
    private void Submit(object? row)
    {
        if (row is ChannelSuggestion { Channel: { } channel } chosen && ReferenceEquals(chosen, _looking))
        {
            _looking = null;
            _choose(channel);
            _box.Text = chosen.Title;
        }
        else
        {
            BackToTyped();
        }
        _settled = _box.Text;
        Close();
    }

    private void Escape()
    {
        BackToTyped();
        _settled = _box.Text;
        Close();
    }

    private void BackToTyped()
    {
        if (_looking is null) return;
        _looking = null;
        _box.Text = _typed;
    }
}
