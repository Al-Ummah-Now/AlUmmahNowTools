using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlUmmahNow.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootFolder = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalVideosFound = table.Column<int>(type: "int", nullable: false),
                    VideosProcessed = table.Column<int>(type: "int", nullable: false),
                    VideosUploaded = table.Column<int>(type: "int", nullable: false),
                    VideosFailed = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RootPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProgressPercentage = table.Column<double>(type: "float", nullable: false),
                    CurrentOperation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DirectoriesProcessed = table.Column<int>(type: "int", nullable: false),
                    TotalDirectories = table.Column<int>(type: "int", nullable: false),
                    FilesFound = table.Column<int>(type: "int", nullable: false),
                    TotalSize = table.Column<long>(type: "bigint", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PathHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChannelId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FolderPath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VideoStoreRoot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VideoStoreUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImageStoreRoot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImageStoreUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalVideos = table.Column<int>(type: "int", nullable: false),
                    ProcessedVideos = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportChannels_ImportSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ImportSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScannedChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    TotalFiles = table.Column<int>(type: "int", nullable: false),
                    TotalSize = table.Column<long>(type: "bigint", nullable: false),
                    UniqueFiles = table.Column<int>(type: "int", nullable: false),
                    UniqueSize = table.Column<long>(type: "bigint", nullable: false),
                    DuplicateFiles = table.Column<int>(type: "int", nullable: false),
                    DuplicateSize = table.Column<long>(type: "bigint", nullable: false),
                    FilesWithThumbnails = table.Column<int>(type: "int", nullable: false),
                    FilesWithInfo = table.Column<int>(type: "int", nullable: false),
                    ReadyForImport = table.Column<int>(type: "int", nullable: false),
                    ImportedFiles = table.Column<int>(type: "int", nullable: false),
                    FailedFiles = table.Column<int>(type: "int", nullable: false),
                    AverageFileSize = table.Column<double>(type: "float", nullable: false),
                    LargestFileSize = table.Column<long>(type: "bigint", nullable: false),
                    SmallestFileSize = table.Column<long>(type: "bigint", nullable: false),
                    TotalDurationSeconds = table.Column<double>(type: "float", nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FirstScanned = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DirectoryExists = table.Column<bool>(type: "bit", nullable: false),
                    SubdirectoryCount = table.Column<int>(type: "int", nullable: false),
                    DepthLevel = table.Column<int>(type: "int", nullable: false),
                    ParentChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScanSessionId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannedChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScannedChannels_ScanSessions_ScanSessionId",
                        column: x => x.ScanSessionId,
                        principalTable: "ScanSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScannedChannels_ScanSessions_ScanSessionId1",
                        column: x => x.ScanSessionId1,
                        principalTable: "ScanSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScannedChannels_ScannedChannels_ParentChannelId",
                        column: x => x.ParentChannelId,
                        principalTable: "ScannedChannels",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScannedVideoFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Directory = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    ChannelName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RelativePath = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MetadataHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ThumbnailPath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ThumbnailSize = table.Column<long>(type: "bigint", nullable: true),
                    ThumbnailHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InfoFilePath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InfoFileContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InfoFileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DurationSeconds = table.Column<double>(type: "float", nullable: true),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    Bitrate = table.Column<long>(type: "bigint", nullable: true),
                    Codec = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDuplicate = table.Column<bool>(type: "bit", nullable: false),
                    OriginalFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ImportStatus = table.Column<int>(type: "int", nullable: false),
                    ImportSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FirstScanned = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastScanned = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FileExists = table.Column<bool>(type: "bit", nullable: false),
                    LastKnownModified = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScanSessionId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannedVideoFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScannedVideoFiles_ScanSessions_ScanSessionId",
                        column: x => x.ScanSessionId,
                        principalTable: "ScanSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScannedVideoFiles_ScanSessions_ScanSessionId1",
                        column: x => x.ScanSessionId1,
                        principalTable: "ScanSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScannedVideoFiles_ScannedVideoFiles_OriginalFileId",
                        column: x => x.OriginalFileId,
                        principalTable: "ScannedVideoFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ImportVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    VideoId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VideoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    VideoMetadata = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportVideos_ImportChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "ImportChannels",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImportVideos_ImportSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ImportSessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportChannels_ChannelName",
                table: "ImportChannels",
                column: "ChannelName");

            migrationBuilder.CreateIndex(
                name: "IX_ImportChannels_SessionId",
                table: "ImportChannels",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportSessions_StartTime",
                table: "ImportSessions",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_ImportSessions_Status",
                table: "ImportSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImportVideos_ChannelId",
                table: "ImportVideos",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportVideos_SessionId",
                table: "ImportVideos",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportVideos_Status",
                table: "ImportVideos",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_DepthLevel",
                table: "ScannedChannels",
                column: "DepthLevel");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_DirectoryExists",
                table: "ScannedChannels",
                column: "DirectoryExists");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_LastUpdated",
                table: "ScannedChannels",
                column: "LastUpdated");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_Name",
                table: "ScannedChannels",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_ParentChannelId",
                table: "ScannedChannels",
                column: "ParentChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_ScanSessionId",
                table: "ScannedChannels",
                column: "ScanSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_ScanSessionId_DepthLevel",
                table: "ScannedChannels",
                columns: new[] { "ScanSessionId", "DepthLevel" });

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_ScanSessionId_Name",
                table: "ScannedChannels",
                columns: new[] { "ScanSessionId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ScannedChannels_ScanSessionId1",
                table: "ScannedChannels",
                column: "ScanSessionId1");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_ChannelName",
                table: "ScannedVideoFiles",
                column: "ChannelName");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_ContentHash",
                table: "ScannedVideoFiles",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_ContentHash_FileSize",
                table: "ScannedVideoFiles",
                columns: new[] { "ContentHash", "FileSize" });

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_FileExists",
                table: "ScannedVideoFiles",
                column: "FileExists");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_ImportStatus",
                table: "ScannedVideoFiles",
                column: "ImportStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_IsDuplicate",
                table: "ScannedVideoFiles",
                column: "IsDuplicate");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_LastScanned",
                table: "ScannedVideoFiles",
                column: "LastScanned");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_MetadataHash",
                table: "ScannedVideoFiles",
                column: "MetadataHash");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_OriginalFileId",
                table: "ScannedVideoFiles",
                column: "OriginalFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_ScanSessionId",
                table: "ScannedVideoFiles",
                column: "ScanSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_ScanSessionId_ChannelName",
                table: "ScannedVideoFiles",
                columns: new[] { "ScanSessionId", "ChannelName" });

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_ScanSessionId_ImportStatus",
                table: "ScannedVideoFiles",
                columns: new[] { "ScanSessionId", "ImportStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ScannedVideoFiles_ScanSessionId1",
                table: "ScannedVideoFiles",
                column: "ScanSessionId1");

            migrationBuilder.CreateIndex(
                name: "IX_ScanSessions_CreatedAt",
                table: "ScanSessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScanSessions_LastUpdated",
                table: "ScanSessions",
                column: "LastUpdated");

            migrationBuilder.CreateIndex(
                name: "IX_ScanSessions_PathHash",
                table: "ScanSessions",
                column: "PathHash");

            migrationBuilder.CreateIndex(
                name: "IX_ScanSessions_Status",
                table: "ScanSessions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportVideos");

            migrationBuilder.DropTable(
                name: "ScannedChannels");

            migrationBuilder.DropTable(
                name: "ScannedVideoFiles");

            migrationBuilder.DropTable(
                name: "ImportChannels");

            migrationBuilder.DropTable(
                name: "ScanSessions");

            migrationBuilder.DropTable(
                name: "ImportSessions");
        }
    }
}
