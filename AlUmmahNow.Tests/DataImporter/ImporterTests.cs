using Xunit;
using Moq;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Data.Database;
using AlUmmahNow.DataImporter;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using System.IO;

namespace AlUmmahNow.Tests.DataImporter
{
    public class ImporterTests : IDisposable
    {
        private readonly ImportDbContext _dbContext;

        public ImporterTests()
        {
            // Setup in-memory database for testing
            var options = new DbContextOptionsBuilder<ImportDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            
            _dbContext = new ImportDbContext(options);
            _dbContext.Database.EnsureCreated();
        }

        [Fact]
        public void Constructor_WithValidDependencies_InitializesCorrectly()
        {
            // Arrange
            var mockR2Client = new Mock<IR2Client>();
            var repository = new Repository<AlUmmahNow.DataImporter.Video>(mockR2Client.Object);
            var mockVideoStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Video>>();
            var mockImageStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Image>>();
            var bunnyStreamClient = new BunnyStreamClient("test-key");

            // Act
            var importer = new Importer(
                repository,
                mockVideoStorageClient.Object,
                mockImageStorageClient.Object,
                bunnyStreamClient,
                _dbContext);

            // Assert
            Assert.NotNull(importer);
        }

        [Fact]
        public async Task ImportMultiChannelAsync_WithNonExistentFolder_ThrowsDirectoryNotFoundException()
        {
            // Arrange
            var mockR2Client = new Mock<IR2Client>();
            var repository = new Repository<AlUmmahNow.DataImporter.Video>(mockR2Client.Object);
            var mockVideoStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Video>>();
            var mockImageStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Image>>();
            var bunnyStreamClient = new BunnyStreamClient("test-key");
            
            var importer = new Importer(
                repository,
                mockVideoStorageClient.Object,
                mockImageStorageClient.Object,
                bunnyStreamClient,
                _dbContext);

            var nonExistentFolder = "/non/existent/folder";
            var userId = "test-user";
            var baseVideoUrl = "https://videos.example.com/";
            var baseImageUrl = "https://images.example.com/";

            // Act & Assert
            var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
            {
                await importer.ImportMultiChannelAsync(nonExistentFolder, userId, baseVideoUrl, baseImageUrl);
            });

            Assert.Contains("Root folder not found", exception.Message);
        }

        [Fact]
        public async Task ImportMultiChannelAsync_WithEmptyFolder_CompletesSuccessfully()
        {
            // Arrange
            var mockR2Client = new Mock<IR2Client>();
            var repository = new Repository<AlUmmahNow.DataImporter.Video>(mockR2Client.Object);
            var mockVideoStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Video>>();
            var mockImageStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Image>>();
            var bunnyStreamClient = new BunnyStreamClient("test-key");
            
            var importer = new Importer(
                repository,
                mockVideoStorageClient.Object,
                mockImageStorageClient.Object,
                bunnyStreamClient,
                _dbContext);

            // Create a temporary empty directory
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var userId = "test-user";
                var baseVideoUrl = "https://videos.example.com/";
                var baseImageUrl = "https://images.example.com/";

                // Act
                var sessionId = await importer.ImportMultiChannelAsync(tempDir, userId, baseVideoUrl, baseImageUrl);

                // Assert
                Assert.NotEqual(Guid.Empty, sessionId);
                
                var status = await importer.GetImportStatusAsync(sessionId);
                Assert.Equal(AlUmmahNow.Data.Models.ImportStatus.Completed, status.Status);
                Assert.Equal(0, status.TotalVideosFound);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task ImportMultiChannelAsync_WithChannelFolders_CreatesChannelsInDatabase()
        {
            // Arrange
            var mockR2Client = new Mock<IR2Client>();
            var repository = new Repository<AlUmmahNow.DataImporter.Video>(mockR2Client.Object);
            var mockVideoStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Video>>();
            var mockImageStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Image>>();
            var bunnyStreamClient = new BunnyStreamClient("test-key");
            
            var importer = new Importer(
                repository,
                mockVideoStorageClient.Object,
                mockImageStorageClient.Object,
                bunnyStreamClient,
                _dbContext);

            // Create temporary directory structure with channel folders and mock video files
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            
            var channel1Dir = Path.Combine(tempDir, "Channel1");
            var channel2Dir = Path.Combine(tempDir, "Channel2");
            Directory.CreateDirectory(channel1Dir);
            Directory.CreateDirectory(channel2Dir);
            
            // Create mock video files
            File.WriteAllText(Path.Combine(channel1Dir, "video1.mp4"), "mock video content");
            File.WriteAllText(Path.Combine(channel2Dir, "video2.mp4"), "mock video content");

            // Mock the storage client calls to avoid actual uploads
            mockVideoStorageClient.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                                 .Returns(Task.CompletedTask);
                                 
            mockImageStorageClient.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                                 .Returns(Task.CompletedTask);

            // Mock the repository calls
            mockR2Client.Setup(x => x.PutObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                       .Returns(Task.CompletedTask);

            try
            {
                var userId = "test-user";
                var baseVideoUrl = "https://videos.example.com/";
                var baseImageUrl = "https://images.example.com/";

                // Act
                var sessionId = await importer.ImportMultiChannelAsync(tempDir, userId, baseVideoUrl, baseImageUrl);

                // Assert
                var status = await importer.GetImportStatusAsync(sessionId);
                Assert.Equal(2, status.Channels.Count);
                Assert.Contains(status.Channels, c => c.ChannelName == "Channel1");
                Assert.Contains(status.Channels, c => c.ChannelName == "Channel2");
                Assert.Equal(2, status.TotalVideosFound);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Fact]
        public async Task ResumeImportAsync_WithNonExistentSession_ReturnsFalse()
        {
            // Arrange
            var mockR2Client = new Mock<IR2Client>();
            var repository = new Repository<AlUmmahNow.DataImporter.Video>(mockR2Client.Object);
            var mockVideoStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Video>>();
            var mockImageStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Image>>();
            var bunnyStreamClient = new BunnyStreamClient("test-key");
            
            var importer = new Importer(
                repository,
                mockVideoStorageClient.Object,
                mockImageStorageClient.Object,
                bunnyStreamClient,
                _dbContext);

            var nonExistentSessionId = Guid.NewGuid();

            // Act
            var result = await importer.ResumeImportAsync(nonExistentSessionId);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task GetImportStatusAsync_WithNonExistentSession_ThrowsInvalidOperationException()
        {
            // Arrange
            var mockR2Client = new Mock<IR2Client>();
            var repository = new Repository<AlUmmahNow.DataImporter.Video>(mockR2Client.Object);
            var mockVideoStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Video>>();
            var mockImageStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Image>>();
            var bunnyStreamClient = new BunnyStreamClient("test-key");
            
            var importer = new Importer(
                repository,
                mockVideoStorageClient.Object,
                mockImageStorageClient.Object,
                bunnyStreamClient,
                _dbContext);

            var nonExistentSessionId = Guid.NewGuid();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await importer.GetImportStatusAsync(nonExistentSessionId);
            });

            Assert.Contains("Session", exception.Message);
            Assert.Contains("not found", exception.Message);
        }

        [Fact]
        public async Task ImportFromBunnyApi_WithInvalidCredentials_ThrowsException()
        {
            // Arrange
            var mockR2Client = new Mock<IR2Client>();
            var repository = new Repository<AlUmmahNow.DataImporter.Video>(mockR2Client.Object);
            var mockVideoStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Video>>();
            var mockImageStorageClient = new Mock<IStorageClient<AlUmmahNow.DataImporter.Image>>();
            var bunnyStreamClient = new BunnyStreamClient("invalid-test-key");
            
            var importer = new Importer(
                repository,
                mockVideoStorageClient.Object,
                mockImageStorageClient.Object,
                bunnyStreamClient,
                _dbContext);

            var libraryId = "12345";
            var userId = "test-user";
            var channelName = "Test Channel";
            var channelThumbnail = "https://example.com/thumb.jpg";
            var videoStoreRoot = "/videos/";
            var videoStoreUrl = "https://videos.example.com/";
            var imageStoreRoot = "/images/";
            var imageStoreUrl = "https://images.example.com/";

            // Act & Assert
            var exception = await Record.ExceptionAsync(async () =>
            {
                await importer.ImportFromBunnyApi(libraryId, userId, channelName, channelThumbnail,
                    videoStoreRoot, videoStoreUrl, imageStoreRoot, imageStoreUrl);
            });

            // Should throw due to invalid API credentials
            Assert.NotNull(exception);
        }

        public void Dispose()
        {
            _dbContext?.Dispose();
        }
    }
}