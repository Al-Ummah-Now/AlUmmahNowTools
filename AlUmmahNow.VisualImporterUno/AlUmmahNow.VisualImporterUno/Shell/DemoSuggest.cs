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
                // Watched folders' box is in the form its first button opens; the Approvals filters are folded
                if (root is Frame { Content: Views.WatchManagementPage { ViewModel: { IsCreateDialogOpen: false } watch } }) watch.OpenCreateWatchDialogCommand.Execute(null);
                if (Find<Expander>(root) is { IsExpanded: false } folded) folded.IsExpanded = true;
                return;
            }
            if (File.Exists(path + ".focus"))
            {
                File.Delete(path + ".focus");
                Find<TextBox>(box)?.Focus(FocusState.Keyboard);
            }
            // What the page gets from the scanner while the box is in use
            if (File.Exists(path + ".refresh"))
            {
                File.Delete(path + ".refresh");
                if (root is Frame { Content: Views.WatchManagementPage watched }) watched.ViewModel.RefreshWatchesCommand.Execute(null);
                if (root is Frame { Content: Views.ApprovalWorkflowPage waiting }) waiting.ViewModel.RefreshApprovalsCommand.Execute(null);
            }
            if (!focused)
            {
                if (Find<TextBox>(box) is not { } text) return;
                focused = text.Focus(FocusState.Keyboard);
                text.SelectAll();
            }
            var items = (box.ItemsSource as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
            var state = $"open={(box.IsSuggestionListOpen ? 1 : 0)}\titems={items.Count}\tfocus={FocusManager.GetFocusedElement(box.XamlRoot!)?.GetType().Name}\ttext={box.Text}\tfirst={items.FirstOrDefault()}\trow={Row(box)}\tunder={Under(box)}\tchosen={Chosen(root)}";
            if (state == last) return;
            last = state;
            File.AppendAllText(path, $"{clock.ElapsedMilliseconds}\t{state.ReplaceLineEndings(" ")}\n");
        };
        timer.Start();
    }

    /// <summary>The middle of the open list's first row, in the window's own pixels: where the script clicks.</summary>
    private static string Row(AutoSuggestBox box)
    {
        if (!box.IsSuggestionListOpen || Find<Microsoft.UI.Xaml.Controls.Primitives.Popup>(box)?.Child is not { } list || Find<ListViewItem>(list) is not { ActualHeight: > 0 } row) return "";
        var scale = box.XamlRoot!.RasterizationScale;
        var at = row.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(row.ActualWidth / 2, row.ActualHeight / 2));
        return FormattableString.Invariant($"{at.X * scale:0},{at.Y * scale:0}");
    }

    private static string? Chosen(DependencyObject root) => (root as Frame)?.Content switch
    {
        Views.ImportView import => import.ViewModel.SelectedChannel?.Id,
        Views.SocialSourcesPage social => social.ViewModel.SelectedChannel?.Id,
        _ => null,
    };

    /// <summary>How far the open list's left edge is from the box's.</summary>
    private static string Under(AutoSuggestBox box)
    {
        if (!box.IsSuggestionListOpen || Find<Microsoft.UI.Xaml.Controls.Primitives.Popup>(box)?.Child is not FrameworkElement { ActualWidth: > 0 } list) return "";
        double Left(FrameworkElement e) => e.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, e.ActualWidth, e.ActualHeight)).Left;
        return FormattableString.Invariant($"{Left(list) - Left(box):0}");
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
