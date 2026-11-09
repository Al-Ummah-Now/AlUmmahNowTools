using System;
using System.Security.Cryptography;
using System.Text;

namespace AlUmmahNow.Importer
{
    /// <summary>Deterministic catalogue ids for imported files.</summary>
    public static class VideoIds
    {
        /// <summary>
        /// The <c>videos/&lt;id&gt;</c> id for a file: derived from the channel and the file's storage key, so
        /// every run that imports the same file targets the same record (a rerun or a parallel run can't add a
        /// second one). RFC 9562 version 8 (name-based, SHA-256).
        /// </summary>
        public static Guid ForFile(string channelName, string videoKey)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"aun-import/v1\n{channelName}\n{videoKey}"));
            var bytes = hash.AsSpan(0, 16).ToArray();
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant
            return new Guid(bytes, bigEndian: true);
        }
    }
}
