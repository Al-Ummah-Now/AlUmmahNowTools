using AlUmmahNow.Data.Database;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlUmmahNow.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportVideosSessionPathUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same idempotent steps ImportDbSchema applies to EnsureCreated databases: add and backfill
            // FilePathHash, drop duplicate rows within a session, then the unique index.
            foreach (var sql in ImportDbSchema.ImportVideosSessionPathUnique)
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ImportVideos_SessionId_FilePathHash",
                table: "ImportVideos");

            migrationBuilder.Sql("IF OBJECT_ID(N'DF_ImportVideos_FilePathHash', N'D') IS NOT NULL ALTER TABLE dbo.ImportVideos DROP CONSTRAINT DF_ImportVideos_FilePathHash;");

            migrationBuilder.DropColumn(
                name: "FilePathHash",
                table: "ImportVideos");
        }
    }
}
