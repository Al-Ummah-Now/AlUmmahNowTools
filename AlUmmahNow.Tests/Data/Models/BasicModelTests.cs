using AlUmmahNow.Data.Models.FileScanning;
using FluentAssertions;
using System;
using Xunit;

namespace AlUmmahNow.Tests.Data.Models
{
    public class ScanSessionBasicTests
    {
        [Fact]
        public void ScanSession_DefaultValues_AreSetCorrectly()
        {
            // Act
            var session = new ScanSession();

            // Assert - GUID is not auto-generated, so it will be empty
            session.Id.Should().NotBe(Guid.Empty);
            session.RootPath.Should().BeEmpty();
            session.Name.Should().BeEmpty();
            session.Status.Should().Be(ScanStatus.Pending);
            session.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            session.StartedAt.Should().BeNull();
            session.CompletedAt.Should().BeNull();
            session.ProgressPercentage.Should().Be(0);
            session.CurrentOperation.Should().BeNull();
            session.FilesFound.Should().Be(0);
            session.TotalSize.Should().Be(0);
            session.ErrorMessage.Should().BeNull();
        }

        [Fact]
        public void ScanSession_WithValidData_AcceptsAllProperties()
        {
            // Arrange
            var id = Guid.NewGuid();
            var rootPath = @"C:\Test\Videos";
            var name = "Test Session";
            var created = DateTime.UtcNow.AddHours(-1);
            var started = DateTime.UtcNow.AddMinutes(-30);
            var completed = DateTime.UtcNow.AddMinutes(-5);

            // Act
            var session = new ScanSession
            {
                Id = id,
                RootPath = rootPath,
                Name = name,
                Status = ScanStatus.Completed,
                CreatedAt = created,
                StartedAt = started,
                CompletedAt = completed,
                ProgressPercentage = 100,
                CurrentOperation = "Completed",
                FilesFound = 25,
                TotalSize = 1024 * 1024 * 500, // 500MB
                ErrorMessage = null
            };

            // Assert
            session.Id.Should().Be(id);
            session.RootPath.Should().Be(rootPath);
            session.Name.Should().Be(name);
            session.Status.Should().Be(ScanStatus.Completed);
            session.CreatedAt.Should().Be(created);
            session.StartedAt.Should().Be(started);
            session.CompletedAt.Should().Be(completed);
            session.ProgressPercentage.Should().Be(100);
            session.CurrentOperation.Should().Be("Completed");
            session.FilesFound.Should().Be(25);
            session.TotalSize.Should().Be(1024 * 1024 * 500);
            session.ErrorMessage.Should().BeNull();
        }

        [Theory]
        [InlineData(ScanStatus.Pending)]
        [InlineData(ScanStatus.InProgress)]
        [InlineData(ScanStatus.Completed)]
        [InlineData(ScanStatus.Failed)]
        [InlineData(ScanStatus.Cancelled)]
        [InlineData(ScanStatus.Paused)]
        public void ScanSession_Status_AcceptsAllValidValues(ScanStatus status)
        {
            // Arrange & Act
            var session = new ScanSession { Status = status };

            // Assert
            session.Status.Should().Be(status);
        }
    }

    public class ScannedVideoFileBasicTests
    {
        [Fact]
        public void ScannedVideoFile_DefaultValues_AreSetCorrectly()
        {
            // Act
            var file = new ScannedVideoFile();

            // Assert - GUID is not auto-generated, so it will be empty
            file.Id.Should().NotBe(Guid.Empty);
            file.ScanSessionId.Should().Be(Guid.Empty);
            file.FilePath.Should().BeEmpty();
            file.FileName.Should().BeEmpty();
            file.Directory.Should().BeEmpty();
            file.ChannelName.Should().BeEmpty();
            file.RelativePath.Should().BeEmpty();
            file.Extension.Should().BeEmpty();
            file.FileSize.Should().Be(0);
            file.ContentHash.Should().BeEmpty();
            file.MetadataHash.Should().BeEmpty();
            file.ImportStatus.Should().Be(FileImportStatus.NotImported);
            file.FirstScanned.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            file.LastScanned.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
            file.FileExists.Should().BeTrue();
            file.IsDuplicate.Should().BeFalse();
            file.OriginalFileId.Should().BeNull();
            file.ImportSessionId.Should().BeNull();
        }

        [Theory]
        [InlineData(FileImportStatus.NotImported)]
        [InlineData(FileImportStatus.Selected)]
        [InlineData(FileImportStatus.Pending)]
        [InlineData(FileImportStatus.Uploading)]
        [InlineData(FileImportStatus.Uploaded)]
        [InlineData(FileImportStatus.Failed)]
        [InlineData(FileImportStatus.Skipped)]
        public void ScannedVideoFile_ImportStatus_AcceptsAllValidValues(FileImportStatus status)
        {
            // Arrange & Act
            var file = new ScannedVideoFile { ImportStatus = status };

            // Assert
            file.ImportStatus.Should().Be(status);
        }

        [Fact]
        public void ScannedVideoFile_VideoMetadata_AcceptsNullableValues()
        {
            // Arrange & Act
            var file = new ScannedVideoFile
            {
                DurationSeconds = 3600.5,
                Width = 1920,
                Height = 1080,
                Bitrate = 5000000,
                Codec = "h264"
            };

            // Assert
            file.DurationSeconds.Should().Be(3600.5);
            file.Width.Should().Be(1920);
            file.Height.Should().Be(1080);
            file.Bitrate.Should().Be(5000000);
            file.Codec.Should().Be("h264");
        }

        [Fact]
        public void ScannedVideoFile_ThumbnailInfo_AcceptsNullableValues()
        {
            // Arrange & Act
            var file = new ScannedVideoFile
            {
                ThumbnailPath = @"C:\thumbs\video.jpg",
                ThumbnailSize = 1024 * 50,
                ThumbnailHash = "thumbnail_hash_value"
            };

            // Assert
            file.ThumbnailPath.Should().Be(@"C:\thumbs\video.jpg");
            file.ThumbnailSize.Should().Be(1024 * 50);
            file.ThumbnailHash.Should().Be("thumbnail_hash_value");
        }

        [Fact]
        public void ScannedVideoFile_InfoFileData_AcceptsNullableValues()
        {
            // Arrange
            var infoContent = "{ \"title\": \"Test Video\", \"duration\": 3600 }";
            var infoHash = "info_file_hash";

            // Act
            var file = new ScannedVideoFile
            {
                InfoFilePath = @"C:\videos\test.info.json",
                InfoFileContent = infoContent,
                InfoFileHash = infoHash
            };

            // Assert
            file.InfoFilePath.Should().Be(@"C:\videos\test.info.json");
            file.InfoFileContent.Should().Be(infoContent);
            file.InfoFileHash.Should().Be(infoHash);
        }

        [Fact]
        public void ScannedVideoFile_DuplicateRelationship_WorksCorrectly()
        {
            // Arrange
            var originalId = Guid.NewGuid();
            
            // Act
            var duplicateFile = new ScannedVideoFile
            {
                IsDuplicate = true,
                OriginalFileId = originalId
            };

            // Assert
            duplicateFile.IsDuplicate.Should().BeTrue();
            duplicateFile.OriginalFileId.Should().Be(originalId);
        }
    }
}