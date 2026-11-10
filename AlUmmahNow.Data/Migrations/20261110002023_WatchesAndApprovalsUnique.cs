using AlUmmahNow.Data.Database;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlUmmahNow.Data.Migrations
{
    /// <inheritdoc />
    public partial class WatchesAndApprovalsUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same idempotent steps ImportDbSchema applies to EnsureCreated databases: add and backfill the
            // path hashes, resolve existing duplicates, then the filtered unique indexes.
            foreach (var sql in ImportDbSchema.WatchesAndApprovalsUnique)
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: ImportDbSchema.PendingApprovalPathIndex,
                table: "PendingApprovals");

            migrationBuilder.DropIndex(
                name: ImportDbSchema.ActiveWatchPathIndex,
                table: "ChannelWatches");

            migrationBuilder.Sql("IF OBJECT_ID(N'DF_PendingApprovals_FilePathHash', N'D') IS NOT NULL ALTER TABLE dbo.PendingApprovals DROP CONSTRAINT DF_PendingApprovals_FilePathHash;");
            migrationBuilder.Sql("IF OBJECT_ID(N'DF_ChannelWatches_WatchPathHash', N'D') IS NOT NULL ALTER TABLE dbo.ChannelWatches DROP CONSTRAINT DF_ChannelWatches_WatchPathHash;");

            migrationBuilder.DropColumn(
                name: "FilePathHash",
                table: "PendingApprovals");

            migrationBuilder.DropColumn(
                name: "WatchPathHash",
                table: "ChannelWatches");
        }
    }
}
