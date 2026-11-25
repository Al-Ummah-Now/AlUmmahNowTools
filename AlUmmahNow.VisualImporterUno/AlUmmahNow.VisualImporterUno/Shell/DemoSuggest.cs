using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The check of the boxes that suggest while one types (scripts/suggest-check.mjs), demo only: <c>TOOLS_DEMO_SUGGEST_LOG=file</c>
/// moves to the page's first such box and writes down whenever its list opens, closes or changes. The script types with the
/// system's keyboard and reads the file.
/// </summary>
internal static class DemoSuggest
{
    public static void Start(DependencyObject root, Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        if (Environment.GetEnvironmentVariable("TOOLS_DEMO") != "1" || Environment.GetEnvironmentVariable("TOOLS_DEMO_SUGGEST_LOG") is not { Length: > 0 } path) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var focused = false;
        var last = "";
        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(30);
        timer.Tick += (_, _) =>
        {
            if (clock.ElapsedMilliseconds < 4000) return;
            if (Find<AutoSuggestBox>(root) is not { ActualWidth: > 0 } box)
            {
                // The Approvals filters are folded
                if (Find<Expander>(root) is { IsExpanded: false } folded) folded.IsExpanded = true;
                return;
            }
            if (!focused)
            {
                if (Find<TextBox>(box) is not { } text) return;
                focused = text.Focus(FocusState.Keyboard);
                text.SelectAll();
            }
            var items = (box.ItemsSource as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
            var state = $"open={(box.IsSuggestionListOpen ? 1 : 0)}\titems={items.Count}\tfocus={FocusManager.GetFocusedElement(box.XamlRoot!)?.GetType().Name}\ttext={box.Text}\tfirst={items.FirstOrDefault()}";
            if (state == last) return;
            last = state;
            File.AppendAllText(path, $"{clock.ElapsedMilliseconds}\t{state.ReplaceLineEndings(" ")}\n");
        };
        timer.Start();
    }

    private static T? Find<T>(DependencyObject parent) where T : class
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (Find<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
