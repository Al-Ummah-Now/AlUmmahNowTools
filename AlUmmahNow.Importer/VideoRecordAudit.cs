using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Importer.Core;

namespace AlUmmahNow.Importer
{
    /// <summary>
    /// Read-only check for catalogue records written before storage keys included the sub-folder path.
    /// </summary>
    /// <remarks>
    /// Sub-folder files with the same name (<c>Season1/ep1.mp4</c>, <c>Season2/ep1.mp4</c>) were uploaded
    /// to the same Bunny key, so the last upload replaced the others and several <c>videos/&lt;id&gt;</c>
    /// records point at one object. Nothing is moved or deleted: every existing object stays where it is
    /// and every record keeps its URL. This lists the records that share a video URL so an operator can
    /// re-import those folders (new imports use per-path keys) and retire the stale records.
    /// </remarks>
    public static class VideoRecordAudit
    {
        public sealed record SharedUrl(string VideoUrl, IReadOnlyList<Video> Records);

        /// <summary>Groups of two or more records with the same <see cref="Video.VideoUrl"/>.</summary>
        public static IReadOnlyList<SharedUrl> FindSharedVideoUrls(IEnumerable<Video> records)
            => records
                .Where(r => !string.IsNullOrWhiteSpace(r.VideoUrl))
                .GroupBy(r => r.VideoUrl, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => new SharedUrl(g.Key, g.OrderBy(r => r.DateCreated).ToList()))
                .OrderBy(g => g.VideoUrl, StringComparer.Ordinal)
                .ToList();

        /// <summary>Reads every record in <paramref name="bucket"/> and returns the shared-URL groups.</summary>
        public static async Task<IReadOnlyList<SharedUrl>> FindSharedVideoUrlsAsync(Repository<Video> repo, string bucket = "videos")
        {
            var records = new List<Video>();
            foreach (var key in await repo.ListObjectsAsync(bucket))
            {
                var record = await repo.GetObjectAsync(bucket, key);
                if (record != null) records.Add(record);
            }
            return FindSharedVideoUrls(records);
        }
    }
}
