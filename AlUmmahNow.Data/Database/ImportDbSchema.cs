using Microsoft.EntityFrameworkCore;

namespace AlUmmahNow.Data.Database
{
    /// <summary>
    /// Brings an existing import database up to the current model.
    /// </summary>
    /// <remarks>
    /// The apps create the database with <c>EnsureCreated</c>, which never alters a database that already
    /// exists, so indexes and columns added later would only reach new installs. <see cref="EnsureUpToDateAsync"/>
    /// creates a missing database and then applies each upgrade step below. Every step is idempotent T-SQL
    /// (it checks the catalog first), and the EF migrations run the same SQL, so a database maintained with
    /// <c>dotnet ef database update</c> and one created by <c>EnsureCreated</c> end up identical.
    /// Non-SQL Server providers (SQLite/in-memory in tests) get the full model from <c>EnsureCreated</c>.
    /// </remarks>
    public static class ImportDbSchema
    {
        public const string ImportVideosSessionPathIndex = "UX_ImportVideos_SessionId_FilePathHash";

        /// <summary>
        /// TOOLS-2: ImportVideos.FilePathHash, backfilled from FilePath, duplicates within a session removed
        /// (keeping the most advanced row), then a unique index on (SessionId, FilePathHash).
        /// </summary>
        public static readonly string[] ImportVideosSessionPathUnique =
        {
            @"IF COL_LENGTH(N'dbo.ImportVideos', N'FilePathHash') IS NULL
    ALTER TABLE dbo.ImportVideos ADD FilePathHash nvarchar(64) NOT NULL
        CONSTRAINT DF_ImportVideos_FilePathHash DEFAULT N'';",
            @"UPDATE dbo.ImportVideos
    SET FilePathHash = CONVERT(nvarchar(64), HASHBYTES('SHA2_256', FilePath), 2)
    WHERE FilePathHash = N'';",
            @"WITH ranked AS (
    SELECT ROW_NUMBER() OVER (
        PARTITION BY SessionId, FilePathHash
        ORDER BY CASE Status WHEN 3 THEN 0 WHEN 2 THEN 1 WHEN 4 THEN 2 ELSE 3 END, CreatedAt, Id) AS rn
    FROM dbo.ImportVideos)
DELETE FROM ranked WHERE rn > 1;",
            $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{ImportVideosSessionPathIndex}' AND object_id = OBJECT_ID(N'dbo.ImportVideos'))
    CREATE UNIQUE INDEX {ImportVideosSessionPathIndex} ON dbo.ImportVideos (SessionId, FilePathHash);",
        };

        /// <summary>Every upgrade step, oldest first.</summary>
        public static IEnumerable<string> SqlServerUpgradeSteps => ImportVideosSessionPathUnique;

        /// <summary>Creates the database if needed and applies every upgrade step (SQL Server only).</summary>
        public static async Task EnsureUpToDateAsync(ImportDbContext db, CancellationToken ct = default)
        {
            await db.Database.EnsureCreatedAsync(ct);
            if (!db.Database.IsSqlServer())
            {
                return;
            }
            foreach (var sql in SqlServerUpgradeSteps)
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
        }
    }
}
