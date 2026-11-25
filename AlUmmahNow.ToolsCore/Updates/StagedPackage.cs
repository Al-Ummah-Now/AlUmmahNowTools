using System.Security.Cryptography;

namespace AlUmmahNow.ToolsCore.Updates;

/// <summary>
/// The one update package waiting to be installed, in the folder the platform's updater reads. Only a file this run
/// wrote counts: whatever is already there is removed first, and its size and SHA-256 are checked against the signed
/// manifest again right before it is handed over, so a file planted or replaced in between is never installed.
/// </summary>
public sealed class StagedPackage
{
    private readonly byte[] _sha256;
    private readonly long _size;
    private readonly string _root;

    private StagedPackage(string path, string root, CoreArtifact artifact)
    {
        Path = path;
        _root = root;
        _sha256 = Convert.FromHexString(artifact.Sha256);
        _size = (long)artifact.Size;
    }

    public string Path { get; }

    /// <summary>
    /// Makes the folder this user's alone (<see cref="PrivateFolder"/>), clears every package it holds and describes the
    /// one about to be written there.
    /// </summary>
    /// <exception cref="IOException">the folder isn't private</exception>
    public static StagedPackage Prepare(string folder, string fileName, CoreArtifact artifact, string? root = null)
    {
        root ??= PrivateFolder.UserRoot;
        PrivateFolder.Ensure(folder, root);
        Clear(folder);
        return new StagedPackage(System.IO.Path.Combine(folder, fileName), root, artifact);
    }

    /// <summary>Removes packages (and half-written ones) an earlier run or anything else left: nothing is installed from them.</summary>
    public static void Clear(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)).ToList())
            File.Delete(file);
    }

    /// <exception cref="UpdateVerificationException">the file is missing or is not the one the signed manifest lists</exception>
    public void Verify()
    {
        try
        {
            PrivateFolder.Ensure(System.IO.Path.GetDirectoryName(Path)!, _root);
            using var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            // A link would let the updater read something else than was checked
            if (new FileInfo(Path).LinkTarget is not null || file.Length != _size || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(file), _sha256))
                throw new UpdateVerificationException("hash");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateVerificationException("hash");
        }
    }

    public void Discard()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: never installed, because nothing is without Verify, and cleared at the next start
        }
    }
}
