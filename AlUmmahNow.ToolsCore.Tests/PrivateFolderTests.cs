using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using AlUmmahNow.ToolsCore.Updates;
using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

/// <summary>The staging folder belongs to this user alone (wiki AUN-APP-UPDATES §5.3). The Unix and Windows halves each run on their system.</summary>
public sealed class PrivateFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tools-private-").FullName;
    private string Folder => Path.Combine(_root, "app", "packages");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Refused() => Assert.Throws<IOException>(() => PrivateFolder.Ensure(Folder, _root)).Message;

    [Fact]
    public void It_is_made_for_this_user_only_and_passes_again()
    {
        PrivateFolder.Ensure(Folder, _root);
        PrivateFolder.Ensure(Folder, _root);
        Assert.True(Directory.Exists(Folder));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Folder));
        else Assert.True(OnlyTrusted(Folder));
    }

    [Fact]
    public void A_folder_outside_the_users_root_is_refused_and_nothing_is_made()
    {
        var elsewhere = Path.Combine(_root, "elsewhere", "packages");
        Assert.Throws<IOException>(() => PrivateFolder.Ensure(elsewhere, Path.Combine(_root, "home")));
        Assert.False(Directory.Exists(elsewhere));
    }

    [Fact]
    public void A_link_in_the_path_is_refused()
    {
        if (OperatingSystem.IsWindows()) return; // needs a privilege there; a junction test is below
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(_root, "app"), other);
        Assert.Contains("is a link", Refused());
        Assert.False(Directory.Exists(Path.Combine(other, "packages")));
    }

    [Fact]
    public void A_parent_others_can_write_is_refused()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.Combine(_root, "app"));
        foreach (var extra in new[] { UnixFileMode.OtherWrite, UnixFileMode.GroupWrite })
        {
            File.SetUnixFileMode(Path.Combine(_root, "app"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | extra);
            Assert.Contains("written by others", Refused());
        }
    }

    [Fact]
    public void A_folder_of_someone_else_is_refused()
    {
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess) return;
        // /usr belongs to root on every Unix this runs on
        Assert.Contains("belongs to someone else", Assert.Throws<IOException>(() => PrivateFolder.Ensure("/usr", "/usr")).Message);
    }

    [Fact]
    public void A_folder_left_open_is_closed_again_before_use()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Folder);
        File.SetUnixFileMode(Path.Combine(_root, "app"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(Folder, (UnixFileMode)0x1FF);
        PrivateFolder.Ensure(Folder, _root);
        Assert.Equal((UnixFileMode)0x1C0, File.GetUnixFileMode(Folder));
    }

    [Fact]
    public void Opened_to_others_after_staging_the_package_is_refused()
    {
        if (OperatingSystem.IsWindows()) return;
        var good = Encoding.UTF8.GetBytes("package");
        var staged = StagedPackage.Prepare(Folder, "a-full.nupkg", new CoreArtifact("tools", "macos", "arm64", "velopack-full", "https://example.com/x", Convert.ToHexStringLower(SHA256.HashData(good)), (ulong)good.Length), _root);
        File.WriteAllBytes(staged.Path, good);
        staged.Verify();
        File.SetUnixFileMode(Path.Combine(_root, "app"), (UnixFileMode)0x1FF);
        Assert.Equal("hash", Assert.Throws<UpdateVerificationException>(staged.Verify).Reason);
    }

    [Fact]
    public void Windows_a_parent_everyone_can_write_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        OpenToEveryone(Directory.CreateDirectory(Path.Combine(_root, "app")).FullName);
        Assert.Contains("written by others", Refused());
    }

    [Fact]
    public void Windows_a_folder_left_open_is_closed_again_before_use()
    {
        if (!OperatingSystem.IsWindows()) return;
        PrivateFolder.Ensure(Folder, _root);
        OpenToEveryone(Folder);
        Assert.False(OnlyTrusted(Folder));
        PrivateFolder.Ensure(Folder, _root);
        Assert.True(OnlyTrusted(Folder));
    }

    [Fact]
    public void Windows_a_junction_in_the_path_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        var other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        using var mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path.Combine(_root, "app")}\" \"{other}\"") { CreateNoWindow = true })!;
        mk.WaitForExit();
        Assert.Equal(0, mk.ExitCode);
        Assert.Contains("is a link", Refused());
        Assert.False(Directory.Exists(Path.Combine(other, "packages")));
        Directory.Delete(Path.Combine(_root, "app"));
    }

    [Fact]
    public void This_users_own_root_is_private_on_this_system()
    {
        // The folder updates are staged in lives under it: if this fails, self-update is off here
        var folder = Path.Combine(PrivateFolder.UserRoot, $".tools-private-test-{Guid.NewGuid():N}");
        try
        {
            PrivateFolder.Ensure(folder, PrivateFolder.UserRoot);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, false, false, false)] // Linux: notice and link
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void Self_update_is_for_windows_and_macos_with_a_root_key(bool key, bool windows, bool mac, bool expected) =>
        Assert.Equal(expected, SelfUpdate.Supported(key, windows, mac));

    [SupportedOSPlatform("windows")]
    private static void OpenToEveryone(string path)
    {
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Modify, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static bool OnlyTrusted(string path)
    {
        var world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        return new DirectoryInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().All(r => !r.IdentityReference.Equals(world));
    }
}
