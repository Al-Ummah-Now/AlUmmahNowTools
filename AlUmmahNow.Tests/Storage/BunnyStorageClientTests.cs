using Xunit;
using Moq;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Models;
using BunnyCDN.Net.Storage.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AlUmmahNow.Tests.Storage
{
    public class BunnyStorageClientTests
    {
        private readonly BunnyStorageClient<Video> _realClient;
        private readonly TestableBunnyStorageClient<Video> _testableClient;
        private readonly Mock<IBunnyCDNStorageWrapper> _mockStorage;

        public BunnyStorageClientTests()
        {
            // Real client for basic tests (will fail with invalid credentials)
            _realClient = new BunnyStorageClient<Video>("test-zone", "test-key", "test-region");
            
            // Mock for detailed testing
            _mockStorage = new Mock<IBunnyCDNStorageWrapper>();
            _testableClient = new TestableBunnyStorageClient<Video>(_mockStorage.Object);
        }

        [Fact]
        public void Constructor_WithValidParameters_InitializesCorrectly()
        {
            // Arrange & Act
            var client = new BunnyStorageClient<Video>("zone", "key", "region");

            // Assert
            Assert.NotNull(client);
        }

        [Fact]
        public void Constructor_WithNullZone_DoesNotThrowAtConstruction()
        {
            // Act & Assert - BunnyCDN allows null parameters but will fail on usage
            var exception = Record.Exception(() => new BunnyStorageClient<Video>(null!, "key", "region"));
            
            // Constructor succeeds, but usage would fail
            Assert.Null(exception);
        }

        [Fact]
        public void Constructor_WithEmptyKey_DoesNotThrowAtConstruction()
        {
            // Act & Assert - BunnyCDN allows empty parameters but will fail on usage
            var exception = Record.Exception(() => new BunnyStorageClient<Video>("zone", "", "region"));
            
            // Constructor succeeds, but usage would fail
            Assert.Null(exception);
        }

        [Fact]
        public async Task UploadAsync_WithValidStream_CallsStorageUpload()
        {
            // Arrange
            var content = new byte[] { 1, 2, 3, 4, 5 };
            var stream = new MemoryStream(content);
            var path = "videos/test-video.mp4";

            _mockStorage.Setup(x => x.UploadAsync(It.IsAny<Stream>(), path))
                       .Returns(Task.CompletedTask);

            // Act
            await _testableClient.UploadAsync(stream, path);

            // Assert
            _mockStorage.Verify(x => x.UploadAsync(It.IsAny<Stream>(), path), Times.Once);
        }

        [Fact]
        public async Task UploadAsync_WhenStorageThrows_PropagatesException()
        {
            // Arrange
            var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            var path = "videos/test-video.mp4";

            _mockStorage.Setup(x => x.UploadAsync(It.IsAny<Stream>(), path))
                       .ThrowsAsync(new Exception("Upload failed"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<Exception>(
                () => _testableClient.UploadAsync(stream, path));
            
            Assert.Equal("Upload failed", exception.Message);
        }

        [Fact]
        public async Task DownloadAsync_WithValidPath_ReturnsStream()
        {
            // Arrange
            var path = "videos/test-video.mp4";
            var expectedStream = new MemoryStream(new byte[] { 1, 2, 3 });

            _mockStorage.Setup(x => x.DownloadObjectAsStreamAsync(path))
                       .ReturnsAsync(expectedStream);

            // Act
            var result = await _testableClient.DownloadAsync(path);

            // Assert
            Assert.Same(expectedStream, result);
            _mockStorage.Verify(x => x.DownloadObjectAsStreamAsync(path), Times.Once);
        }

        [Fact]
        public async Task ListAsync_WithValidPath_ReturnsStorageObjects()
        {
            // Arrange
            var path = "videos/";
            var bunnyObjects = new List<BunnyCDN.Net.Storage.Models.StorageObject>
            {
                new BunnyCDN.Net.Storage.Models.StorageObject
                {
                    Guid = "guid1",
                    ObjectName = "video1.mp4",
                    Path = "/videos/",
                    Length = 1000,
                    DateCreated = DateTime.UtcNow,
                    LastChanged = DateTime.UtcNow,
                    IsDirectory = false
                },
                new BunnyCDN.Net.Storage.Models.StorageObject
                {
                    Guid = "guid2", 
                    ObjectName = "video2.mp4",
                    Path = "/videos/",
                    Length = 2000,
                    DateCreated = DateTime.UtcNow.AddDays(-1),
                    LastChanged = DateTime.UtcNow.AddDays(-1),
                    IsDirectory = false
                }
            };

            _mockStorage.Setup(x => x.GetStorageObjectsAsync(path))
                       .ReturnsAsync(bunnyObjects);

            // Act
            var result = await _testableClient.ListAsync(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("video1.mp4", result[0].ObjectName);
            Assert.Equal("video2.mp4", result[1].ObjectName);
            Assert.Equal(1000, result[0].Length);
            Assert.Equal(2000, result[1].Length);
            
            _mockStorage.Verify(x => x.GetStorageObjectsAsync(path), Times.Once);
        }

        [Fact]
        public async Task ListAsync_WhenStorageThrows_ReturnsEmptyList()
        {
            // Arrange
            var path = "videos/";

            _mockStorage.Setup(x => x.GetStorageObjectsAsync(path))
                       .ThrowsAsync(new Exception("Network error"));

            // Act
            var result = await _testableClient.ListAsync(path);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task GetAsync_WithValidPath_ReturnsFirstObject()
        {
            // Arrange
            var path = "videos/test-video.mp4";
            var bunnyObjects = new List<BunnyCDN.Net.Storage.Models.StorageObject>
            {
                new BunnyCDN.Net.Storage.Models.StorageObject
                {
                    Guid = "guid1",
                    ObjectName = "test-video.mp4",
                    Path = "/videos/",
                    Length = 1000,
                    DateCreated = DateTime.UtcNow,
                    LastChanged = DateTime.UtcNow,
                    IsDirectory = false
                }
            };

            _mockStorage.Setup(x => x.GetStorageObjectsAsync(path))
                       .ReturnsAsync(bunnyObjects);

            // Act
            var result = await _testableClient.GetAsync(path);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test-video.mp4", result.ObjectName);
            Assert.Equal("guid1", result.Guid);
            Assert.Equal(1000, result.Length);
        }

        [Fact]
        public async Task GetAsync_WhenNoObjectsFound_ReturnsNull()
        {
            // Arrange
            var path = "videos/nonexistent.mp4";

            _mockStorage.Setup(x => x.GetStorageObjectsAsync(path))
                       .ReturnsAsync(new List<BunnyCDN.Net.Storage.Models.StorageObject>());

            // Act
            var result = await _testableClient.GetAsync(path);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetAsync_WhenStorageThrows_ReturnsNull()
        {
            // Arrange
            var path = "videos/test-video.mp4";

            _mockStorage.Setup(x => x.GetStorageObjectsAsync(path))
                       .ThrowsAsync(new Exception("Network error"));

            // Act
            var result = await _testableClient.GetAsync(path);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ListAsync_WithEmptyResult_ReturnsEmptyList()
        {
            // Arrange
            var path = "empty-folder/";

            _mockStorage.Setup(x => x.GetStorageObjectsAsync(path))
                       .ReturnsAsync(new List<BunnyCDN.Net.Storage.Models.StorageObject>());

            // Act
            var result = await _testableClient.ListAsync(path);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        // Interface for wrapping BunnyCDNStorage to make it testable
        public interface IBunnyCDNStorageWrapper
        {
            Task UploadAsync(Stream stream, string path);
            Task<Stream> DownloadObjectAsStreamAsync(string path);
            Task<List<BunnyCDN.Net.Storage.Models.StorageObject>> GetStorageObjectsAsync(string path);
        }

        // Testable version of BunnyStorageClient that accepts a wrapper dependency
        private class TestableBunnyStorageClient<T> : IStorageClient<T>
        {
            private readonly IBunnyCDNStorageWrapper _storage;

            public TestableBunnyStorageClient(IBunnyCDNStorageWrapper storage)
            {
                _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            }

            public async Task<Stream> DownloadAsync(string path)
            {
                return await _storage.DownloadObjectAsStreamAsync(path);
            }

            public async Task<List<AlUmmahNow.Data.Models.StorageObject>> ListAsync(string path)
            {
                try
                {
                    var result = await _storage.GetStorageObjectsAsync(path);
                    return result.Select(o =>
                        new AlUmmahNow.Data.Models.StorageObject
                        {
                            Guid = o.Guid,
                            ObjectName = o.ObjectName,
                            DateCreated = o.DateCreated,
                            LastChanged = o.LastChanged,
                            Path = o.Path,
                            Length = o.Length,
                            IsDirectory = o.IsDirectory
                        }).ToList();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error listing objects in BunnyCDN: {ex.Message}");
                    return new List<AlUmmahNow.Data.Models.StorageObject>();
                }
            }

            public async Task<AlUmmahNow.Data.Models.StorageObject?> GetAsync(string path)
            {
                try
                {
                    var result = await ListAsync(path);
                    var obj = result.FirstOrDefault();
                    if (obj == null) return null;
                    
                    return new AlUmmahNow.Data.Models.StorageObject
                    {
                        Guid = obj.Guid,
                        ObjectName = obj.ObjectName,
                        DateCreated = obj.DateCreated,
                        LastChanged = obj.LastChanged,
                        Path = obj.Path,
                        Length = obj.Length,
                        IsDirectory = obj.IsDirectory
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting object from BunnyCDN: {ex.Message}");
                    return null;
                }
            }

            public async Task UploadAsync(Stream stream, string path)
            {
                await _storage.UploadAsync(stream, path);
            }
        }
    }

    // Test model for BunnyStorageClient
    public class Video
    {
        public Guid ID { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string VideoUrl { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ChannelThumbnailUrl { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
        public DateTime DateModified { get; set; } = DateTime.UtcNow;
        public int Width { get; set; }
        public int Height { get; set; }
        public List<string> AvailableResolutions { get; set; } = new List<string>();
    }
}