using System.Security.Cryptography;
using System.Text;
using AlUmmahNow.ToolsCore.Updates;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>The staged package on disk (wiki AUN-APP-UPDATES §5.3): only the file this run wrote, checked again before it is installed.</summary>
public sealed class StagedPackageTests : IDisposable
{
    private static readonly byte[] Good = Encoding.UTF8.GetBytes("the verified package");
    private readonly string _dir = Directory.CreateTempSubdirectory("tools-staged-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CoreArtifact Artifact => new("tools", "windows", "x64", "velopack-full", "https://example.com/x", Convert.ToHexStringLower(SHA256.HashData(Good)), (ulong)Good.Length);

    private static string Reason(StagedPackage p) => Assert.Throws<UpdateVerificationException>(p.Verify).Reason;

    [Fact]
    public void The_verified_file_passes_and_discarding_removes_it()
    {
        var staged = StagedPackage.Prepare(_dir, "a-full.nupkg", Artifact);
        File.WriteAllBytes(staged.Path, Good);
        staged.Verify();
        staged.Discard();
        Assert.False(File.Exists(staged.Path));
        Assert.Equal("hash", Reason(staged));
    }

    [Fact]
    public void A_file_planted_before_staging_is_removed_not_taken()
    {
        File.WriteAllText(Path.Combine(_dir, "a-full.nupkg"), "planted");
        File.WriteAllText(Path.Combine(_dir, "AlUmmahNow.Tools-9999.1.1-full.nupkg"), "planted");
        File.WriteAllText(Path.Combine(_dir, "b.nupkg.partial"), "half");
        File.WriteAllText(Path.Combine(_dir, ".betaId"), "kept");
        var staged = StagedPackage.Prepare(_dir, "a-full.nupkg", Artifact);
        Assert.Equal(new[] { ".betaId" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Equal("hash", Reason(staged));
    }

    [Fact]
    public void A_file_swapped_after_staging_is_refused()
    {
        var staged = StagedPackage.Prepare(_dir, "a-full.nupkg", Artifact);
        File.WriteAllBytes(staged.Path, Good);
        staged.Verify();
        var other = (byte[])Good.Clone();
        other[0] ^= 1;
        File.WriteAllBytes(staged.Path, other); // the same size
        Assert.Equal("hash", Reason(staged));
        File.WriteAllBytes(staged.Path, Encoding.UTF8.GetBytes("longer than the verified package"));
        Assert.Equal("hash", Reason(staged));
    }

    [Fact]
    public void A_link_to_the_right_bytes_is_refused()
    {
        if (OperatingSystem.IsWindows()) return; // needs a privilege there
        var elsewhere = Path.Combine(_dir, "elsewhere");
        File.WriteAllBytes(elsewhere, Good);
        var staged = StagedPackage.Prepare(_dir, "a-full.nupkg", Artifact);
        File.CreateSymbolicLink(staged.Path, elsewhere);
        Assert.Equal("hash", Reason(staged));
    }
}
