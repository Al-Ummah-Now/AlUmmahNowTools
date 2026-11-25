using System.Text;
using System.Text.Json;
using AlUmmahNow.Core;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>
/// The real aun-core UpdateChecker (Core v0.4.0-rc.7) from C#, with the signed vector shared by aun-core and the server
/// (AlUmmahNowCore spec/app-updates/signed-vector.json, copied): the binding Tools relies on accepts it and refuses a change.
/// </summary>
public sealed class CoreUpdateCheckerTests
{
    private static JsonElement Vector()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "app-updates-signed-vector.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    public void The_shared_signed_vector_verifies_and_a_change_is_refused()
    {
        var v = Vector();
        using var checker = new UpdateChecker("AlUmmahNowTools", new[] { v.GetProperty("rootPublicKey").GetString()! }, "2026.11.22", false, null,
            v.GetProperty("keyList").GetString(), v.GetProperty("keyListSig").GetString());
        var manifest = v.GetProperty("manifest").GetString()!;
        var sig = v.GetProperty("manifestSig").GetString()!;

        var ok = checker.VerifyManifest(Encoding.UTF8.GetBytes(manifest), sig, "v2026.11.23");
        Assert.Null(ok.Refused);
        Assert.Equal("2026.11.23", ok.Release!.Version);
        Assert.Equal("فحص أسرع للمجلدات.", ok.Release.NotesAr);

        var tampered = checker.VerifyManifest(Encoding.UTF8.GetBytes(manifest.Replace("Faster", "Slower")), sig, "v2026.11.23");
        Assert.Equal("bad_signature", tampered.Refused);
        Assert.Null(tampered.Release);

        Assert.True(checker.AllowedDownloadUrl("https://release-assets.githubusercontent.com/x"));
        Assert.False(checker.AllowedDownloadUrl("https://evil.example/x"));
    }

    [Fact]
    public void The_compiled_in_key_list_verifies_under_the_compiled_in_root_key()
    {
        var root = Assert.Single(AlUmmahNow.ToolsCore.Updates.ToolsUpdateKeys.RootKeys);
        // A list the root key didn't sign throws here: such a build must never ship
        using var checker = new UpdateChecker("AlUmmahNowTools", new[] { root }, "2026.11.22", false, null,
            AlUmmahNow.ToolsCore.Updates.ToolsUpdateKeys.BundledKeyList, AlUmmahNow.ToolsCore.Updates.ToolsUpdateKeys.BundledKeyListSig);
        Assert.Contains("\"serial\":1", checker.StateJson());
        Assert.Throws<AunException.InvalidInput>(() => new UpdateChecker("AlUmmahNowTools", new[] { root }, "2026.11.22", false, null,
            AlUmmahNow.ToolsCore.Updates.ToolsUpdateKeys.BundledKeyList.Replace("AlUmmahNowTools", "AlUmmahNowDesktop"), AlUmmahNow.ToolsCore.Updates.ToolsUpdateKeys.BundledKeyListSig));
    }

    [Fact]
    public void Without_trusted_keys_nothing_verifies()
    {
        var v = Vector();
        using var checker = new UpdateChecker("AlUmmahNowTools", Array.Empty<string>(), "2026.11.22", false, null, null, null);
        var check = checker.VerifyManifest(Encoding.UTF8.GetBytes(v.GetProperty("manifest").GetString()!), v.GetProperty("manifestSig").GetString()!, "v2026.11.23");
        Assert.Equal("unknown_key", check.Refused);
    }
}
