using System.Collections.Specialized;
using AlUmmahNow.Data.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AlUmmahNow.VisualImporterUno.Helpers;

/// <summary>
/// Live suggestions for an <see cref="AutoSuggestBox"/>: set <c>AutoSuggest.Candidates</c> to the
/// values it can suggest and the box lists the matches as the user types (SuggestionMatcher: best
/// first, case and Arabic-diacritic insensitive). Focusing an empty box lists them all.
/// <para>Free text (a new watch's channel name): bind the box's Text TwoWay.</para>
/// <para>A filter (the channel filters): bind <c>AutoSuggest.Value</c> TwoWay instead. Typing only
/// suggests; the value changes when a suggestion is chosen, on Enter (the top match, or the text when
/// nothing matches) and when the box is cleared, so a half-typed name never filters (or bulk-acts on)
/// the list.</para>
/// AutoSuggestBox is WinUI's combobox: arrow keys move through the list, Enter chooses, Escape
/// closes, and it is announced as a combo box with its suggestions.
/// Same behaviour as the web admins' autocomplete (AlUmmahNow, CMS, Auth).
/// </summary>
public static class AutoSuggest
{
    public static readonly DependencyProperty CandidatesProperty = DependencyProperty.RegisterAttached(
        "Candidates", typeof(IEnumerable<string>), typeof(AutoSuggest), new PropertyMetadata(null, OnCandidatesChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(string), typeof(AutoSuggest), new PropertyMetadata(null, OnValueChanged));

    public static string? GetValue(DependencyObject d) => (string?)d.GetValue(ValueProperty);

    public static void SetValue(DependencyObject d, string? value) => d.SetValue(ValueProperty, value);

    private static readonly DependencyProperty CommitsProperty = DependencyProperty.RegisterAttached(
        "Commits", typeof(bool), typeof(AutoSuggest), new PropertyMetadata(false));

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AutoSuggestBox box) return;
        if (!(bool)box.GetValue(CommitsProperty))
        {
            box.SetValue(CommitsProperty, true);
            box.QuerySubmitted += OnQuerySubmitted;
            box.TextChanged += OnTextClearedCommits;
        }
        var text = e.NewValue as string ?? string.Empty;
        if (box.Text != text) box.Text = text;
    }

    private static void OnQuerySubmitted(AutoSuggestBox box, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var typed = args.QueryText?.Trim() ?? string.Empty;
        var chosen = args.ChosenSuggestion as string
            ?? (typed.Length == 0 ? null : SuggestionMatcher.Suggest(GetCandidates(box) ?? Array.Empty<string>(), typed, 1).FirstOrDefault() ?? typed);
        box.Text = chosen ?? string.Empty;
        SetValue(box, string.IsNullOrEmpty(chosen) ? null : chosen);
        box.IsSuggestionListOpen = false;
    }

    private static void OnTextClearedCommits(AutoSuggestBox box, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && string.IsNullOrWhiteSpace(box.Text) && GetValue(box) is not null)
            SetValue(box, null);
    }

    public static IEnumerable<string>? GetCandidates(DependencyObject d) => (IEnumerable<string>?)d.GetValue(CandidatesProperty);

    public static void SetCandidates(DependencyObject d, IEnumerable<string>? value) => d.SetValue(CandidatesProperty, value);

    private static void OnCandidatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AutoSuggestBox box) return;
        if (e.OldValue is INotifyCollectionChanged oldList) oldList.CollectionChanged -= Refresh(box);
        if (e.NewValue is INotifyCollectionChanged newList) newList.CollectionChanged += Refresh(box);
        if (e.OldValue is null)
        {
            box.TextChanged += OnTextChanged;
            box.SuggestionChosen += OnSuggestionChosen;
            box.GotFocus += OnGotFocus;
        }
    }

    // One handler per box, so it can be removed again when the list is replaced
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AutoSuggestBox, NotifyCollectionChangedEventHandler> Handlers = new();

    private static NotifyCollectionChangedEventHandler Refresh(AutoSuggestBox box) =>
        Handlers.GetValue(box, b => (_, _) =>
        {
            if (b.FocusState != FocusState.Unfocused) Update(b);
        });

    private static void Update(AutoSuggestBox box) =>
        box.ItemsSource = SuggestionMatcher.Suggest(GetCandidates(box) ?? Array.Empty<string>(), box.Text);

    private static void OnTextChanged(AutoSuggestBox box, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Only typing asks again; a chosen suggestion or a bound value does not reopen the list
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) Update(box);
    }

    // Arrowing onto a suggestion shows it in the box (the platform does this too); in a filter the
    // value commits on Enter (QuerySubmitted carries the chosen suggestion) or a click
    private static void OnSuggestionChosen(AutoSuggestBox box, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string chosen) box.Text = chosen;
    }

    private static void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is AutoSuggestBox box && string.IsNullOrEmpty(box.Text))
        {
            Update(box);
            box.IsSuggestionListOpen = true;
        }
    }
}
