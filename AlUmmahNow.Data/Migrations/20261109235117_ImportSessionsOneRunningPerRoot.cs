using System;
using AlUmmahNow.Data.Database;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlUmmahNow.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportSessionsOneRunningPerRoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same idempotent steps ImportDbSchema applies to EnsureCreated databases.
            foreach (var sql in ImportDbSchema.ImportSessionsOneRunningPerRoot)
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ImportSessions_RootFolderHash_InProgress",
                table: "ImportSessions");

            migrationBuilder.DropColumn(
                name: "HeartbeatAt",
                table: "ImportSessions");

            migrationBuilder.Sql("IF OBJECT_ID(N'DF_ImportSessions_RootFolderHash', N'D') IS NOT NULL ALTER TABLE dbo.ImportSessions DROP CONSTRAINT DF_ImportSessions_RootFolderHash;");

            migrationBuilder.DropColumn(
                name: "RootFolderHash",
                table: "ImportSessions");
        }
    }
}
