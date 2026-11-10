using AlUmmahNow.Data.Models.FileScanning;
using Microsoft.EntityFrameworkCore;

namespace AlUmmahNow.Data.Database
{
    /// <summary>
    /// Conditional <see cref="ScannedVideoFile.ImportStatus"/> changes. Each is one <c>UPDATE … WHERE Id = @id AND
    /// ImportStatus IN (…)</c>, so the UI, the import worker and other service hosts on the same database can never
    /// overwrite each other's status: whoever changes the row first wins and the others see <c>false</c>.
    /// </summary>
    public static class ScannedFileStatus
    {
        /// <summary>States the user may tick or untick. A file being imported, or already imported, is out of reach.</summary>
        public static readonly FileImportStatus[] UserSelectable =
        {
            FileImportStatus.NotImported, FileImportStatus.Selected, FileImportStatus.Failed, FileImportStatus.Skipped,
        };

        /// <summary>Ticks (Selected) or unticks (NotImported) a file, unless an import has already claimed it.</summary>
        public static Task<bool> SetSelectedAsync(ImportDbContext db, Guid fileId, bool selected, CancellationToken ct = default) =>
            TransitionAsync(db, fileId, UserSelectable, selected ? FileImportStatus.Selected : FileImportStatus.NotImported, ct);

        /// <summary>Moves a file from one of <paramref name="from"/> to <paramref name="to"/>; false if it was in another state.</summary>
        public static async Task<bool> TransitionAsync(
            ImportDbContext db, Guid fileId, IReadOnlyCollection<FileImportStatus> from, FileImportStatus to, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var updated = await db.ScannedVideoFiles
                .Where(f => f.Id == fileId && from.Contains(f.ImportStatus))
                .ExecuteUpdateAsync(u => u
                    .SetProperty(f => f.ImportStatus, to)
                    .SetProperty(f => f.LastScanned, now), ct);
            return updated == 1;
        }
    }
}
