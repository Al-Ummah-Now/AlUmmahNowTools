using AlUmmahNow.ToolsCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.Resources;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The window's content (wiki TOOLS-ADMIN TA-10): the shared header, then sign-in, the staff gate
/// or the workspace, following the session. Rebuilt when the language changes so x:Uid strings reload.
/// </summary>
public sealed partial class ToolsShell : UserControl
{
    private readonly ShellState _state;
    private readonly Func<FrameworkElement> _createWorkspace;
    private FrameworkElement? _workspace;

    public ToolsShell(ShellState state, Func<FrameworkElement> createWorkspace)
    {
        _state = state;
        _createWorkspace = createWorkspace;
        InitializeComponent();
        FlowDirection = state.Preferences.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Language = state.Preferences.Language;
        if (ShellState.TextFontFor(state.Preferences.Language) is { } font)
        {
            // Children inherit the font; the account menu opens in its own popup, so it gets it item by item
            FontFamily = font;
            SetMenuFont(AccountMenu.Items, font);
        }
        ApplyTheme();
        // Review frames of its questions, once the demo queue is there
        DemoDialogs.OnLoaded(this, "sign-out", () => AskToLeaveAsync(null));
        DemoDialogs.OnLoaded(this, "switch", () => AskToLeaveAsync(ToolsEnvironmentKind.Production));
        if (AppUpdates.Service is { } updates)
        {
            UpdateBarHost.Content = new UpdateBar(updates, AppUpdates.IsBusy);
            ForcedUpdateHost.Content = new ForcedUpdateView(updates);
        }
        AutomationProperties.SetName(LanguageButton, L("Shell_LanguageButtonName"));
        AutomationProperties.SetName(AccountButton, L("Shell_AccountButtonName"));
        SizeChanged += (_, _) => FitHeader();
        Loaded += (_, _) =>
        {
            _state.Changed += OnStateChanged;
            _state.ThemeChanged += OnThemeChanged;
            ApplyTheme();
            Render();
        };
        Unloaded += (_, _) =>
        {
            _state.Changed -= OnStateChanged;
            _state.ThemeChanged -= OnThemeChanged;
        };
    }

    /// <summary>The workspace, once shown (MainWindow forwards its navigation calls to it)</summary>
    public FrameworkElement? Workspace => _workspace;

    internal static string L(string key) => Loc.Get(key);

    private void OnStateChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Render);

    private void OnThemeChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ApplyTheme);

    private static void SetMenuFont(IList<MenuFlyoutItemBase> items, FontFamily font)
    {
        foreach (var item in items)
        {
            item.FontFamily = font;
            if (item is MenuFlyoutSubItem sub) SetMenuFont(sub.Items, font);
        }
    }

    // The device setting, read from the system colours: the app's own theme is changed below, so it can't tell
    private static bool DeviceIsDark =>
        new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 128;

    private void ApplyTheme()
    {
        var dark = _state.Preferences.EffectiveTheme(DeviceIsDark) == ToolsTheme.Dark;
        var theme = dark ? ElementTheme.Dark : ElementTheme.Light;
        RequestedTheme = theme;
        // Uno takes the application theme from the window's root element, so the theme goes there as well
        if (XamlRoot?.Content is FrameworkElement root) root.RequestedTheme = theme;
        // The sun switches to light, the moon to dark
        ThemeIcon.Glyph = dark ? "" : "";
        var name = L(dark ? "Shell_ThemeToLight" : "Shell_ThemeToDark");
        AutomationProperties.SetName(ThemeButton, name);
        ToolTipService.SetToolTip(ThemeButton, name);
    }

    /// <summary>Narrow windows keep the header to one row: the account shows its initials, then the logo stands alone.</summary>
    private void FitHeader()
    {
        var signedIn = AccountButton.Visibility == Visibility.Visible;
        AccountName.Visibility = ActualWidth < 640 ? Visibility.Collapsed : Visibility.Visible;
        BrandText.Visibility = signedIn && ActualWidth < 480 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Render()
    {
        var session = _state.Session;
        var environment = session.Environment.Kind;
        RenderEnvironment(environment);

        var signedIn = session.Account != null && session.State is SessionState.Staff or SessionState.NotStaff or SessionState.Unreachable or SessionState.Checking;
        AccountButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        FitHeader();
        if (signedIn)
        {
            var account = session.Account!;
            var name = account.DisplayName ?? account.Email ?? account.UserId;
            AccountName.Text = name;
            AccountInitials.Text = Initials(name);
            // The address stands alone: Uno's Skia text lays mixed Arabic and Latin runs out in the wrong order
            AccountEmailItem.Text = account.Email ?? name;
            AutomationProperties.SetName(AccountEmailItem, string.Format(L("Shell_SignedInAs"), account.Email ?? name));
        }

        var showSignIn = session.State is SessionState.SignedOut or SessionState.SigningIn or SessionState.Starting;
        SignInView.Visibility = showSignIn ? Visibility.Visible : Visibility.Collapsed;
        GateView.Visibility = session.State is SessionState.Checking or SessionState.NotStaff or SessionState.Unreachable ? Visibility.Visible : Visibility.Collapsed;
        var staff = session.State == SessionState.Staff;
        if (staff && _workspace == null)
        {
            _workspace = _createWorkspace();
            WorkspaceHost.Content = _workspace;
        }
        else if (!staff && _workspace != null && session.State != SessionState.Checking)
        {
            // Signed out or not staff: the workspace goes, nothing of it stays on screen
            WorkspaceHost.Content = null;
            (_workspace as IDisposable)?.Dispose();
            _workspace = null;
        }

        WorkspaceHost.Visibility = staff ? Visibility.Visible : Visibility.Collapsed;

        if (showSignIn) RenderSignIn(session);
        else if (GateView.Visibility == Visibility.Visible) RenderGate(session);
    }

    private void RenderEnvironment(ToolsEnvironmentKind kind)
    {
        foreach (var (button, k) in new[] { (EnvProduction, ToolsEnvironmentKind.Production), (EnvStage, ToolsEnvironmentKind.Stage), (EnvQa, ToolsEnvironmentKind.Qa) })
        {
            AutomationProperties.SetItemStatus(button, k == kind ? L("Shell_Selected") : string.Empty);
            button.Background = k == kind ? (Brush)Resources["SegmentSelectedBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.Foreground = k == kind ? (Brush)Application.Current.Resources["FanousOnPrimaryBrush"] : (Brush)Application.Current.Resources["FanousTextSecondaryBrush"];
        }

        EnvProductionItem.IsChecked = kind == ToolsEnvironmentKind.Production;
        EnvStageItem.IsChecked = kind == ToolsEnvironmentKind.Stage;
        EnvQaItem.IsChecked = kind == ToolsEnvironmentKind.Qa;

        // Production shows no badge; stage and QA say so on every screen
        EnvironmentBadge.Visibility = kind == ToolsEnvironmentKind.Production ? Visibility.Collapsed : Visibility.Visible;
        var stage = kind == ToolsEnvironmentKind.Stage;
        EnvironmentBadgeText.Text = L(stage ? "Shell_BadgeStage" : "Shell_BadgeQa");
        EnvironmentBadge.Background = new SolidColorBrush(stage ? Windows.UI.Color.FromArgb(0x33, 0x8B, 0x5C, 0xF6) : Windows.UI.Color.FromArgb(0x33, 0x22, 0xC5, 0x5E));
        EnvironmentBadge.BorderBrush = new SolidColorBrush(stage ? Windows.UI.Color.FromArgb(0xAA, 0xC4, 0xB5, 0xFD) : Windows.UI.Color.FromArgb(0xAA, 0x86, 0xEF, 0xAC));
        EnvironmentBadgeText.Foreground = new SolidColorBrush(stage ? Windows.UI.Color.FromArgb(0xFF, 0xDD, 0xD6, 0xFE) : Windows.UI.Color.FromArgb(0xFF, 0xBB, 0xF7, 0xD0));
    }

    private void RenderSignIn(ToolsSession session)
    {
        var waiting = session.State == SessionState.SigningIn;
        SignInButton.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
        WaitingPanel.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        // While the browser is open the chosen environment stays visible, just not changeable
        EnvironmentSegments.IsHitTestVisible = !waiting;
        EnvironmentSegments.Opacity = waiting ? 0.7 : 1;
        foreach (var b in new[] { EnvProduction, EnvStage, EnvQa }) b.IsTabStop = !waiting;
        if (session.LastFailure is { } failure && !waiting)
        {
            var cancelled = failure == SignInFailure.Cancelled;
            var res = Application.Current.Resources;
            var tone = (Brush)res[cancelled ? "FanousTextSecondaryBrush" : "FanousErrorTextBrush"];
            FailureText.Foreground = tone;
            FailureIcon.Foreground = tone;
            FailureIcon.Glyph = cancelled ? "\uE946" : "\uE7BA";
            FailureBox.BorderBrush = (Brush)res[cancelled ? "FanousOutlineBrush" : "FanousErrorTextBrush"];
            FailureBox.Background = cancelled
                ? (Brush)res["FanousSurfaceRaisedBrush"]
                : new SolidColorBrush(((SolidColorBrush)res["FanousErrorTextBrush"]).Color) { Opacity = 0.08 };
            FailureText.Text = L(failure switch
            {
                SignInFailure.Cancelled => "SignIn_FailureCancelled",
                SignInFailure.TimedOut => "SignIn_FailureTimedOut",
                SignInFailure.Offline => "SignIn_FailureOffline",
                _ => "SignIn_FailureFailed",
            });
            FailureBox.Visibility = Visibility.Visible;
        }
        else
        {
            FailureBox.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderGate(ToolsSession session)
    {
        var checking = session.State == SessionState.Checking;
        GateRing.IsActive = checking;
        GateRing.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
        GateIcon.Visibility = checking ? Visibility.Collapsed : Visibility.Visible;
        GateActions.Visibility = checking ? Visibility.Collapsed : Visibility.Visible;
        var account = session.Account;
        GateAccountChip.Visibility = account != null && !checking ? Visibility.Visible : Visibility.Collapsed;
        if (account != null)
        {
            var who = account.Email ?? account.DisplayName ?? account.UserId;
            GateAccountText.Text = who;
            AutomationProperties.SetName(GateAccountChip, string.Format(L("Shell_SignedInAs"), who));
        }
        switch (session.State)
        {
            case SessionState.Checking:
                GateTitle.Text = L("Gate_CheckingTitle");
                GateBody.Text = L("Gate_CheckingBody");
                break;
            case SessionState.NotStaff:
                GateIcon.Glyph = "";
                GateTitle.Text = L("Gate_NotStaffTitle");
                GateBody.Text = L("Gate_NotStaffBody");
                GatePrimaryText.Text = L("Gate_NotStaffPrimary");
                break;
            default:
                GateIcon.Glyph = "";
                GateTitle.Text = L("Gate_UnreachableTitle");
                GateBody.Text = L("Gate_UnreachableBody");
                GatePrimaryText.Text = L("Gate_UnreachablePrimary");
                break;
        }
    }

    private static string Initials(string name)
    {
        var parts = name.Split(new[] { ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        var first = char.ToUpperInvariant(parts[0][0]).ToString();
        return parts.Length > 1 && !name.Contains('@') ? first + char.ToUpperInvariant(parts[1][0]) : first;
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e) => _state.SetLanguage(_state.Preferences.Language == "ar" ? "en" : "ar");

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        var dark = _state.Preferences.EffectiveTheme(DeviceIsDark) == ToolsTheme.Dark;
        _state.SetTheme(dark ? ToolsTheme.Light : ToolsTheme.Dark);
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e) => await _state.Session.SignInAsync();

    private void OnCancelSignInClick(object sender, RoutedEventArgs e) => _state.Session.CancelSignIn();

    private void OnReopenBrowserClick(object sender, RoutedEventArgs e) => _state.Session.ReopenSignInPage();

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        await LeaveAsync(null, _state.Session.SignOutAsync);
    }

    private bool _leaving;

    /// <summary>One at a time: while the queue is stopping there is nothing left to ask about.</summary>
    private async Task LeaveAsync(ToolsEnvironmentKind? to, Func<Task> leave)
    {
        if (_leaving) return;
        _leaving = true;
        try
        {
            if (!await AskToLeaveAsync(to)) return;
            // While its tokens still are valid: what was sent is saved as sent
            await ImportWorkspace.HoldAsync();
            try { await leave(); }
            finally { ImportWorkspace.Resume(); }
        }
        finally
        {
            _leaving = false;
        }
    }

    /// <summary>Asks before the imports that are running stop: signing out, or another environment.</summary>
    internal static async Task<bool> AskToLeaveAsync(ToolsEnvironmentKind? to)
    {
        var files = ImportWorkspace.ActiveFiles;
        if (files == 0) return true;
        var uploads = ImportWorkspace.UnfinishedUploads;
        var lines = new List<string> { Loc.Plural("Leave_Files", files) };
        if (uploads > 0) lines.Add(Loc.Plural("Import_RemoveAllUploads", uploads));
        lines.Add(Loc.Get(to is null ? "Leave_SignOutResume" : "Leave_SwitchResume"));
        return await ConfirmDialog.AskAsync(new ConfirmRequest(
            Loc.Get(to is null ? "Leave_SignOutTitle" : "Leave_SwitchTitle"), lines,
            Loc.Get(to is null ? "Leave_SignOut" : "Leave_Switch"), Loc.Get(to is null ? "Leave_StaySignedIn" : "Import_Cancel")));
    }

    private async Task SwitchAsync(ToolsEnvironmentKind kind)
    {
        if (kind != _state.Session.Environment.Kind) await LeaveAsync(kind, () => _state.SwitchEnvironmentAsync(kind));
    }

    private async void OnGatePrimaryClick(object sender, RoutedEventArgs e)
    {
        var session = _state.Session;
        if (session.State == SessionState.NotStaff)
        {
            // Another account: sign this one out, then straight to the browser
            await session.SignOutAsync();
            await session.SignInAsync();
        }
        else
        {
            await session.CheckAsync();
        }
    }

    private async void OnEnvironmentSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<ToolsEnvironmentKind>(tag, out var kind)) await SwitchAsync(kind);
        Render();
    }

    private async void OnEnvironmentMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string tag } && Enum.TryParse<ToolsEnvironmentKind>(tag, out var kind)) await SwitchAsync(kind);
        // Not left: the menu shows where one is
        Render();
    }
}
