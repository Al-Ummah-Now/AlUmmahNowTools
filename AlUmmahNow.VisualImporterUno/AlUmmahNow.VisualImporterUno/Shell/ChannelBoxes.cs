using AlUmmahNow.ToolsCore.Import;
using AlUmmahNow.VisualImporterUno.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// A box that suggests channels: typing searches, the arrow keys and a click choose, Enter and Escape close. Uno opens the list
/// at every change of the text or the rows while the box has the focus, also when the text was changed by choosing: a list that
/// was closed stays closed until something is typed.
/// </summary>
internal static class ChannelBoxes
{
    public static void Attach(AutoSuggestBox box, ChannelSearch search, Action<ImportChannel> choose)
    {
        var closed = false;
        // A row that only says something about the search never becomes the text
        box.UpdateTextOnSelect = false;
        box.TextChanged += (sender, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                if (closed) box.IsSuggestionListOpen = false;
                return;
            }
            closed = false;
            _ = search.SearchAsync(box.Text);
            box.IsSuggestionListOpen = true;
        };
        // Suggestions when the admin moves to the box, never when the page puts the focus there itself
        box.GotFocus += (sender, e) =>
        {
            if (e.OriginalSource is not TextBox { FocusState: not FocusState.Programmatic }) return;
            closed = false;
            if (search.Found.Count > 0) box.IsSuggestionListOpen = true;
            else _ = search.SearchAsync(box.Text);
        };
        box.SuggestionChosen += (_, args) =>
        {
            if (args.SelectedItem is not ChannelSuggestion { Channel: { } channel } row) return;
            search.Cancel();
            choose(channel);
            box.Text = row.Title;
        };
        box.QuerySubmitted += (_, _) => closed = true;
        box.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != VirtualKey.Escape) return;
            closed = true;
            search.Cancel();
        }), true);
    }
}
