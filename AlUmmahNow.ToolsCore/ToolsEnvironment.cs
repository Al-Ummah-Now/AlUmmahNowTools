namespace AlUmmahNow.ToolsCore;

/// <summary>Where the app signs in and which admin API it talks to.</summary>
public enum ToolsEnvironmentKind
{
    Production,
    Stage,
    Qa,
}

/// <summary>
/// One environment: the Auth issuer, the Admin Tools (desktop) client registered there (wiki
/// TOOLS-ADMIN TA-2; a public PKCE client on a loopback redirect) and the admin API.
/// </summary>
public sealed record ToolsEnvironment(ToolsEnvironmentKind Kind, Uri Issuer, string ClientId, Uri AdminApi)
{
    /// <summary>Production and stage share the client row (the same Auth migration), QA has its own.</summary>
    public static readonly ToolsEnvironment Production = new(
        ToolsEnvironmentKind.Production,
        new Uri("https://auth.alummahnow.net"),
        "cf0d26c6-4cd1-4bbf-8247-c63a5d417b15",
        new Uri("https://admin.api.alummahnow.net"));

    public static readonly ToolsEnvironment Stage = new(
        ToolsEnvironmentKind.Stage,
        new Uri("https://auth.stage.alummahnow.net"),
        "cf0d26c6-4cd1-4bbf-8247-c63a5d417b15",
        new Uri("https://admin.api.stage.alummahnow.net"));

    public static readonly ToolsEnvironment Qa = new(
        ToolsEnvironmentKind.Qa,
        new Uri("https://auth.qa.alummahnow.net"),
        "75f36653-4dfd-44af-8f67-588a5b7e9613",
        new Uri("https://admin.api.qa.alummahnow.net"));

    public static IReadOnlyList<ToolsEnvironment> All { get; } = new[] { Production, Stage, Qa };

    public static ToolsEnvironment For(ToolsEnvironmentKind kind) => All.First(e => e.Kind == kind);

    /// <summary>Each environment keeps its own tokens: a QA account never signs in to production.</summary>
    public string StoreName => "tools-" + Kind.ToString().ToLowerInvariant();
}
