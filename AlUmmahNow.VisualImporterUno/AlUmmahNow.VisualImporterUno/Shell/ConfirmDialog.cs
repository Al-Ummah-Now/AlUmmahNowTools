using AlUmmahNow.ToolsCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>A question before something that cannot simply be undone (Tools #46).</summary>
/// <param name="Lines">What it happens to, and what happens; each short enough for one line</param>
/// <param name="Action">The button that does it, named after what it does</param>
/// <param name="Keep">The other button; null: Cancel</param>
/// <param name="Danger">Something is lost</param>
/// <param name="TypeNumber">The number to type first</param>
public sealed record ConfirmRequest(string Title, IReadOnlyList<string> Lines, string Action, string? Keep = null, bool Danger = true, int? TypeNumber = null);

/// <summary>
/// The one way the app asks: the safe button is the default and has the focus (Enter and Esc keep everything as it is), the other
/// is named after what it does, in the danger colour when something is lost. One question at a time: what is asked meanwhile is not done.
/// </summary>
public static class ConfirmDialog
{
    /// <summary>Set by tests, which have no window.</summary>
    internal static Func<ConfirmRequest, Task<bool>>? Answer { get; set; }

    /// <summary>From any thread. No, when it cannot be asked: another question is open, or the window is going.</summary>
    public static async Task<bool> AskAsync(ConfirmRequest request, XamlRoot? root = null) => Answer is { } answer
        ? await answer(request)
        : await AppDialogs.ShowAsync(at => Build(request, at), DialogKind.Question, root) == ContentDialogResult.Primary;

    internal static ContentDialog Build(ConfirmRequest request, XamlRoot root)
    {
        var panel = new StackPanel { Spacing = 6 };
        // Each line is short enough for one line: a popup's Arabic order only holds on one
        foreach (var line in request.Lines.SelectMany(l => l.Split('\n')))
            panel.Children.Add(new TextBlock { Text = Loc.PopupLine(Loc.KeepLastWords(line)), TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = panel,
            PrimaryButtonText = request.Action,
            CloseButtonText = request.Keep ?? Loc.Get("Import_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            FlowDirection = Loc.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            XamlRoot = root,
        };
        if (request.TypeNumber is { } number)
        {
            var label = Loc.Format("Confirm_TypeNumber", number);
            panel.Children.Add(new TextBlock { Text = Loc.PopupLine(label), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
            var box = new TextBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxLength = 20, Tag = DialogLook.EnterDoesNothing };
            AutomationProperties.SetName(box, label);
            // What is typed starts at the box's start edge, the right in Arabic
            if (Loc.IsArabic) box.TextAlignment = TextAlignment.Right;
            dialog.IsPrimaryButtonEnabled = false;
            box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = TypedNumber.Is(box.Text, number);
            if (DemoDialogs.Name == "reject-all-typed") box.Text = Loc.Number(number);
            panel.Children.Add(box);
        }
        DialogLook.Apply(dialog, request.Danger);
        return dialog;
    }
}
