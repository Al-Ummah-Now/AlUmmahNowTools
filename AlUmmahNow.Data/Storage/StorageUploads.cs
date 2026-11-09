namespace AlUmmahNow.Data.Storage
{
    /// <summary>What <see cref="StorageUploads.UploadIfAbsentAsync{T}"/> did.</summary>
    public enum UploadOutcome
    {
        /// <summary>Nothing was stored at the key, so the file was uploaded.</summary>
        Uploaded,

        /// <summary>The key already holds an object of the same size (an earlier upload of this file); nothing was sent.</summary>
        AlreadyPresent,
    }

    /// <summary>
    /// The key already holds a <em>different</em> object (another size). The upload is refused instead of
    /// silently replacing a file that another channel, folder or run put there.
    /// </summary>
    public sealed class StorageKeyConflictException : IOException
    {
        public string Path { get; }
        public long RemoteLength { get; }
        public long LocalLength { get; }

        public StorageKeyConflictException(string path, long remoteLength, long localLength)
            : base($"Refusing to overwrite '{path}': it already holds a different file ({remoteLength} bytes, local file is {localLength} bytes). " +
                   "Delete the remote object first if it really should be replaced.")
        {
            Path = path;
            RemoteLength = remoteLength;
            LocalLength = localLength;
        }
    }

    public static class StorageUploads
    {
        /// <summary>
        /// Uploads <paramref name="file"/> to <paramref name="path"/> only if nothing is stored there yet.
        /// An existing object of the same size counts as this file (keys are unique per channel and relative
        /// path, so a same-size object at the key is an earlier upload of it); one of another size is a
        /// conflict and is never overwritten.
        /// </summary>
        /// <remarks>
        /// Bunny Storage has no conditional PUT, so this is check-then-put. Two runs racing on the same row
        /// are prevented by the importer's row claims; this check stops reruns and colliding keys from
        /// re-sending or replacing files.
        /// </remarks>
        public static async Task<UploadOutcome> UploadIfAbsentAsync<T>(this IStorageClient<T> client, FileInfo file, string path)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(file);

            var existing = await client.GetAsync(path);
            if (existing != null)
            {
                if (existing.Length == file.Length)
                {
                    return UploadOutcome.AlreadyPresent;
                }
                throw new StorageKeyConflictException(path, existing.Length, file.Length);
            }

            await using var stream = file.OpenRead();
            await client.UploadAsync(stream, path);
            return UploadOutcome.Uploaded;
        }

        /// <summary>
        /// The storage key for a file under a channel root: the root followed by the file's path relative to
        /// the channel folder, with '/' separators. Files directly in the channel folder keep the key they
        /// always had (<c>root + file name</c>); files in sub-folders no longer collide with each other.
        /// </summary>
        public static string KeyFor(string root, string channelFolder, string filePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(root);
            return EnsureTrailingSlash(root) + RelativeKey(channelFolder, filePath);
        }

        /// <summary>The path of <paramref name="filePath"/> relative to <paramref name="baseFolder"/>, with '/' separators.</summary>
        public static string RelativeKey(string baseFolder, string filePath)
        {
            var relative = System.IO.Path.GetRelativePath(baseFolder, filePath);
            if (relative.StartsWith("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
            {
                throw new ArgumentException($"'{filePath}' is not inside '{baseFolder}'.", nameof(filePath));
            }
            return relative.Replace('\\', '/');
        }

        public static string EnsureTrailingSlash(string root) => root.EndsWith('/') ? root : root + "/";
    }
}
