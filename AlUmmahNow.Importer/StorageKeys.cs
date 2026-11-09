using System.IO;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Storage;

namespace AlUmmahNow.Importer
{
    /// <summary>
    /// Storage keys for imported files, relative to the channel's store roots.
    /// </summary>
    /// <remarks>
    /// A key is the file's path inside the channel folder ('/' separated), so <c>Season1/ep1.mp4</c> and
    /// <c>Season2/ep1.mp4</c> no longer map to the same object, and each channel has its own roots. Files
    /// directly in the channel folder keep the key they had before (the bare file name), so objects already
    /// uploaded for them are found and not uploaded again.
    /// </remarks>
    public static class StorageKeys
    {
        /// <summary>The video's key under <see cref="ImportChannel.VideoStoreRoot"/> / <see cref="ImportChannel.VideoStoreUrl"/>.</summary>
        public static string VideoKey(ImportChannel channel, FileInfo videoFile)
            => StorageUploads.RelativeKey(channel.FolderPath, videoFile.FullName);

        /// <summary>
        /// The thumbnail's key under <see cref="ImportChannel.ImageStoreRoot"/>: the video's key with the
        /// thumbnail's extension, so it is unique per video wherever the image file sits (next to the video
        /// or in a <c>.thumbs</c> folder).
        /// </summary>
        public static string ThumbnailKey(string videoKey, FileInfo thumbnailFile)
        {
            var slash = videoKey.LastIndexOf('/');
            var directory = slash < 0 ? "" : videoKey[..(slash + 1)];
            var name = slash < 0 ? videoKey : videoKey[(slash + 1)..];
            return directory + Path.GetFileNameWithoutExtension(name) + thumbnailFile.Extension;
        }
    }
}
