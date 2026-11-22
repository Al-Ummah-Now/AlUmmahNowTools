using System.Globalization;
using System.Net;
using System.Text;
using AlUmmahNow.ToolsCore;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.Resources;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// The app's shell state (wiki TOOLS-ADMIN TA-10): preferences, and the sign-in session of the chosen
/// environment. One per process; the shell UI listens to <see cref="Changed"/>.
/// </summary>
public sealed class ShellState
{
    private readonly string _preferencesPath;

    public ShellState(string preferencesPath, ShellDemo? demo)
    {
        _preferencesPath = preferencesPath;
        Demo = demo;
        Preferences = demo?.Preferences ?? ToolsPreferences.Load(preferencesPath);
        Session = CreateSession(Preferences.Environment);
    }

    public static ShellState Current { get; private set; } = null!;

    public static ShellState Start(string preferencesPath, ShellDemo? demo) => Current = new ShellState(preferencesPath, demo);

    public ToolsPreferences Preferences { get; }
    public ToolsSession Session { get; private set; }
    public ShellDemo? Demo { get; }

    /// <summary>The session changed state, or another environment's session replaced it</summary>
    public event EventHandler? Changed;

    /// <summary>The language changed: the shell is rebuilt so every x:Uid string reloads</summary>
    public event EventHandler? LanguageChanged;

    public event EventHandler? ThemeChanged;

    public static void ApplyLanguage(string language)
    {
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        var culture = new CultureInfo(language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>
    /// The text font for Arabic. Uno's Skia text splits a line wherever the font changes and lays each piece out on its
    /// own, so Arabic needs one font that carries Arabic letters and Latin punctuation alike, or every full stop lands
    /// at the wrong end of the line. Windows draws text natively, so the system font stays there.
    /// </summary>
    public static FontFamily? TextFontFor(string language) =>
#if HAS_UNO
        language == "ar" ? new FontFamily("ms-appx:///Assets/Fonts/IBMPlexSansArabic.ttf") : null;
#else
        null;
#endif

    public void SetLanguage(string language)
    {
        if (Preferences.Language == language) return;
        Preferences.Language = language;
        Save();
        ApplyLanguage(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The whisper model chosen on the Import page (TA-13), kept for next time.</summary>
    public void SetSpeechModel(string id)
    {
        if (Preferences.SpeechModel == id) return;
        Preferences.SpeechModel = id;
        Save();
    }

    public void SetTheme(ToolsTheme theme)
    {
        Preferences.Theme = theme;
        Save();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Each environment keeps its own sign-in: switching restores that environment's.</summary>
    public async Task SwitchEnvironmentAsync(ToolsEnvironmentKind kind)
    {
        if (Session.Environment.Kind == kind) return;
        Preferences.Environment = kind;
        Save();
        var old = Session;
        old.Changed -= OnSessionChanged;
        old.CancelSignIn();
        Session = CreateSession(kind);
        old.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
        await Session.RestoreAsync();
    }

    private ToolsSession CreateSession(ToolsEnvironmentKind kind)
    {
        var environment = ToolsEnvironment.For(kind);
        var page = (En: "Signed in. You can close this tab and go back to Al Ummah Now Tools.", Ar: "تم تسجيل الدخول. يمكنك إغلاق هذا التبويب والعودة إلى أدوات الأمة الآن.");
        ToolsSession session = Demo is { } demo
            ? new ToolsSession(environment, demo.Auth, new AdminApiClient(environment.AdminApi, demo.Api), demo.Browser, page)
            : new ToolsSession(environment, new CoreAuthBackend(environment, SecureStores.Create(environment.StoreName)), new AdminApiClient(environment.AdminApi), new ShellBrowserLauncher(), page);
        session.Changed += OnSessionChanged;
        return session;
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private void Save()
    {
        if (Demo != null) return;
        try
        {
            Preferences.Save(_preferencesPath);
        }
        catch (IOException ex)
        {
            Serilog.Log.Warning(ex, "[Shell] Could not save preferences");
        }
    }
}

/// <summary>
/// Screenshots and UI tests: a fake sign-in, nothing sent anywhere (<c>TOOLS_DEMO=1</c>).
/// <c>TOOLS_DEMO_STATE</c> picks the screen: <c>signed-out</c>, <c>signing-in</c>, <c>failed</c>,
/// <c>checking</c>, <c>not-staff</c>, <c>unreachable</c> or <c>staff</c> (the default). <c>TOOLS_DEMO_ROLE=super_admin</c>
/// signs in a super admin (social sources), else an admin.
/// </summary>
public sealed class ShellDemo
{
    public ShellDemo(ToolsPreferences preferences, string state)
    {
        Preferences = preferences;
        State = state;
        Auth = new DemoAuth(signedIn: state is "checking" or "not-staff" or "unreachable" or "staff", staff: state != "not-staff", offline: state == "offline");
        Api = new DemoApi(state);
        Browser = new DemoBrowser(redirects: state is not ("signing-in" or "failed"));
    }

    public string State { get; }

    public ToolsPreferences Preferences { get; }
    internal DemoAuth Auth { get; }
    internal DemoApi Api { get; }
    internal IBrowserLauncher Browser { get; }

    /// <summary>Brings the session to the screen asked for</summary>
    public async Task DriveAsync(ToolsSession session)
    {
        switch (State)
        {
            case "signing-in":
                _ = session.SignInAsync();
                break;
            case "offline":
                await session.SignInAsync();
                break;
            case "failed":
                var signIn = session.SignInAsync();
                await Task.Delay(200);
                session.CancelSignIn();
                await signIn;
                break;
            default:
                await session.RestoreAsync();
                break;
        }
    }

    public static ShellDemo? FromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("TOOLS_DEMO") != "1") return null;
        var prefs = new ToolsPreferences
        {
            Language = Environment.GetEnvironmentVariable("TOOLS_DEMO_LANG") == "ar" ? "ar" : "en",
            Theme = Environment.GetEnvironmentVariable("TOOLS_DEMO_THEME") == "light" ? ToolsTheme.Light : ToolsTheme.Dark,
            Environment = Enum.TryParse<ToolsEnvironmentKind>(Environment.GetEnvironmentVariable("TOOLS_DEMO_ENV"), true, out var e) ? e : ToolsEnvironmentKind.Production,
        };
        // Screenshot runs never launch the folder scanner
        AlUmmahNow.VisualImporterUno.ViewModels.ServiceStatusViewModel.AutoStart = false;
        return new ShellDemo(prefs, Environment.GetEnvironmentVariable("TOOLS_DEMO_STATE") ?? "staff");
    }

    internal sealed class DemoAuth : IAuthBackend
    {
        private ToolsAccount? _account;

        private readonly ToolsAccount _person;

        private readonly bool _offline;

        public DemoAuth(bool signedIn, bool staff, bool offline)
        {
            _offline = offline;
            _person = staff ? Staff : Visitor;
            _account = signedIn ? _person : null;
        }

        private static readonly ToolsAccount Staff = new("usr_demo", "mariam.haddad@alummahnow.net", "Mariam Haddad");
        private static readonly ToolsAccount Visitor = new("usr_visitor", "yusuf.rahman@mail.alummahnow.net", "Yusuf Rahman");

        public string BeginLogin(string redirectUri) => "https://auth.example/authorize?redirect_uri=" + Uri.EscapeDataString(redirectUri);
        public Task<ToolsAccount> CompleteLoginAsync(string redirect) =>
            _offline ? Task.FromException<ToolsAccount>(new AlUmmahNow.Core.AunException.Network("demo: offline")) : Task.FromResult(_account = _person);
        public ToolsAccount? Active() => _account;
        public Task<string?> AccessTokenAsync() => Task.FromResult(_account == null ? null : "demo");

        public Task SignOutAsync(string userId)
        {
            _account = null;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    internal sealed class DemoApi : HttpMessageHandler
    {
        private readonly string _state;

        public DemoApi(string state) => _state = state;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_state == "checking") await Task.Delay(Timeout.Infinite, cancellationToken);
            if (_state == "unreachable") throw new HttpRequestException("demo: admin API down");
            if (_state == "not-staff") return new HttpResponseMessage(HttpStatusCode.Forbidden);
            // TOOLS_DEMO_ROLE=super_admin: the account also manages social sources (TA-13)
            var superAdmin = Environment.GetEnvironmentVariable("TOOLS_DEMO_ROLE") == "super_admin";
            var me = "{\"success\":true,\"result\":{\"userId\":\"usr_demo\",\"email\":\"mariam.haddad@alummahnow.net\",\"roles\":[\"" + (superAdmin ? "super_admin" : "admin") +
                "\"],\"permissions\":[\"imports:manage\"" + (superAdmin ? ",\"import_sources:manage\"" : "") + "]}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(me, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Plays the browser by calling the app's loopback redirect at once</summary>
    private sealed class DemoBrowser : IBrowserLauncher
    {
        private readonly bool _redirects;

        public DemoBrowser(bool redirects) => _redirects = redirects;

        public void Open(Uri uri)
        {
            if (!_redirects) return;
            var redirect = System.Web.HttpUtility.ParseQueryString(uri.Query)["redirect_uri"];
            if (redirect == null) return;
            _ = Task.Run(async () =>
            {
                await Task.Delay(400);
                using var http = new HttpClient();
                await http.GetAsync(redirect + "?code=demo&state=demo");
            });
        }
    }
}
