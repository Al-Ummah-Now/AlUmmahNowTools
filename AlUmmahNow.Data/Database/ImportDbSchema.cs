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

        public const string ImportSessionsOneRunningPerRootIndex = "UX_ImportSessions_RootFolderHash_InProgress";

        /// <summary>
        /// TOOLS-1: ImportSessions.RootFolderHash and HeartbeatAt, and a filtered unique index allowing one
        /// InProgress session per root. Existing sessions keep an empty hash (excluded by the filter); the
        /// importer sets it when it resumes one.
        /// </summary>
        public static readonly string[] ImportSessionsOneRunningPerRoot =
        {
            @"IF COL_LENGTH(N'dbo.ImportSessions', N'RootFolderHash') IS NULL
    ALTER TABLE dbo.ImportSessions ADD RootFolderHash nvarchar(64) NOT NULL
        CONSTRAINT DF_ImportSessions_RootFolderHash DEFAULT N'';",
            @"IF COL_LENGTH(N'dbo.ImportSessions', N'HeartbeatAt') IS NULL
    ALTER TABLE dbo.ImportSessions ADD HeartbeatAt datetime2 NULL;",
            $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{ImportSessionsOneRunningPerRootIndex}' AND object_id = OBJECT_ID(N'dbo.ImportSessions'))
    CREATE UNIQUE INDEX {ImportSessionsOneRunningPerRootIndex} ON dbo.ImportSessions (RootFolderHash)
        WHERE [Status] = 0 AND [RootFolderHash] <> N'';",
        };


        public const string ActiveWatchPathIndex = "UX_ChannelWatches_WatchPathHash_Active";
        public const string PendingApprovalPathIndex = "UX_PendingApprovals_FilePathHash_Pending";

        /// <summary>
        /// TOOLS-6: path hashes on ChannelWatches and PendingApprovals, backfilled; duplicate active watches of one
        /// path deactivated and duplicate pending approvals of one file deleted (the oldest is kept in both cases);
        /// then filtered unique indexes so concurrent detections and StartWatch calls cannot create them again.
        /// </summary>
        public static readonly string[] WatchesAndApprovalsUnique =
        {
            @"IF COL_LENGTH(N'dbo.ChannelWatches', N'WatchPathHash') IS NULL
    ALTER TABLE dbo.ChannelWatches ADD WatchPathHash nvarchar(64) NOT NULL
        CONSTRAINT DF_ChannelWatches_WatchPathHash DEFAULT N'';",
            @"IF COL_LENGTH(N'dbo.PendingApprovals', N'FilePathHash') IS NULL
    ALTER TABLE dbo.PendingApprovals ADD FilePathHash nvarchar(64) NOT NULL
        CONSTRAINT DF_PendingApprovals_FilePathHash DEFAULT N'';",
            @"UPDATE dbo.ChannelWatches
    SET WatchPathHash = CONVERT(nvarchar(64), HASHBYTES('SHA2_256', WatchPath), 2)
    WHERE WatchPathHash = N'';",
            @"UPDATE dbo.PendingApprovals
    SET FilePathHash = CONVERT(nvarchar(64), HASHBYTES('SHA2_256', FilePath), 2)
    WHERE FilePathHash = N'';",
            @"WITH ranked AS (
    SELECT IsActive, ROW_NUMBER() OVER (PARTITION BY WatchPathHash ORDER BY CreatedAt, Id) AS rn
    FROM dbo.ChannelWatches WHERE IsActive = 1)
UPDATE ranked SET IsActive = 0 WHERE rn > 1;",
            @"WITH ranked AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY FilePathHash ORDER BY DetectedAt, Id) AS rn
    FROM dbo.PendingApprovals WHERE Status = 0)
DELETE FROM ranked WHERE rn > 1;",
            $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{ActiveWatchPathIndex}' AND object_id = OBJECT_ID(N'dbo.ChannelWatches'))
    CREATE UNIQUE INDEX {ActiveWatchPathIndex} ON dbo.ChannelWatches (WatchPathHash) WHERE [IsActive] = 1;",
            $@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{PendingApprovalPathIndex}' AND object_id = OBJECT_ID(N'dbo.PendingApprovals'))
    CREATE UNIQUE INDEX {PendingApprovalPathIndex} ON dbo.PendingApprovals (FilePathHash) WHERE [Status] = 0;",
        };

        /// <summary>
        /// TOOLS-8: a rowversion on ScannedVideoFiles so tracked updates detect concurrent changes. SQL Server fills
        /// it for existing rows when the column is added.
        /// </summary>
        public static readonly string[] ScannedVideoFilesRowVersion =
        {
            @"IF COL_LENGTH(N'dbo.ScannedVideoFiles', N'RowVersion') IS NULL
    ALTER TABLE dbo.ScannedVideoFiles ADD RowVersion rowversion NOT NULL;",
        };

        /// <summary>Every upgrade step, oldest first.</summary>
        public static IEnumerable<string> SqlServerUpgradeSteps =>
            ImportVideosSessionPathUnique
                .Concat(ImportSessionsOneRunningPerRoot)
                .Concat(WatchesAndApprovalsUnique)
                .Concat(ScannedVideoFilesRowVersion);

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
