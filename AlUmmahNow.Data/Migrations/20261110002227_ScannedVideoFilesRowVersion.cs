using AlUmmahNow.Data.Database;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlUmmahNow.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScannedVideoFilesRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same idempotent step ImportDbSchema applies to EnsureCreated databases.
            foreach (var sql in ImportDbSchema.ScannedVideoFilesRowVersion)
            {
                migrationBuilder.Sql(sql);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ScannedVideoFiles");
        }
    }
}
