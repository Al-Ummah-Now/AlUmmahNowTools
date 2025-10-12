using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlUmmahNow.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelWatchAndApprovalModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelWatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    WatchPath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IncludeSubdirectories = table.Column<bool>(type: "bit", nullable: false),
                    ProcessingDelayMs = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastFileDetected = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalFilesDetected = table.Column<int>(type: "int", nullable: false),
                    FileExtensions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelWatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelWatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FileModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovalNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FileExists = table.Column<bool>(type: "bit", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ThumbnailPath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingApprovals_ChannelWatches_ChannelWatchId",
                        column: x => x.ChannelWatchId,
                        principalTable: "ChannelWatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelWatches_ChannelName",
                table: "ChannelWatches",
                column: "ChannelName");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelWatches_CreatedAt",
                table: "ChannelWatches",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelWatches_IsActive",
                table: "ChannelWatches",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelWatches_IsActive_ChannelName",
                table: "ChannelWatches",
                columns: new[] { "IsActive", "ChannelName" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelWatches_IsActive_LastFileDetected",
                table: "ChannelWatches",
                columns: new[] { "IsActive", "LastFileDetected" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelWatches_LastFileDetected",
                table: "ChannelWatches",
                column: "LastFileDetected");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelWatches_WatchPath",
                table: "ChannelWatches",
                column: "WatchPath");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_ApprovedAt",
                table: "PendingApprovals",
                column: "ApprovedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_ChannelWatchId",
                table: "PendingApprovals",
                column: "ChannelWatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_ChannelWatchId_Status",
                table: "PendingApprovals",
                columns: new[] { "ChannelWatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_ContentHash",
                table: "PendingApprovals",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_DetectedAt",
                table: "PendingApprovals",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_ExpiresAt",
                table: "PendingApprovals",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_FileExists",
                table: "PendingApprovals",
                column: "FileExists");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_Priority",
                table: "PendingApprovals",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_Status",
                table: "PendingApprovals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_Status_DetectedAt",
                table: "PendingApprovals",
                columns: new[] { "Status", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_Status_ExpiresAt",
                table: "PendingApprovals",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingApprovals_Status_Priority",
                table: "PendingApprovals",
                columns: new[] { "Status", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingApprovals");

            migrationBuilder.DropTable(
                name: "ChannelWatches");
        }
    }
}
