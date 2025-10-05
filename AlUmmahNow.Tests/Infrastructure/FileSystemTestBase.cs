using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AlUmmahNow.Tests.Infrastructure
{
    /// <summary>
    /// Base class for tests that require file system operations
    /// Creates temporary directories and files for testing
    /// </summary>
    public abstract class FileSystemTestBase : IAsyncLifetime
    {
        protected string TestRootDirectory { get; private set; } = null!;
        protected string TestVideosDirectory { get; private set; } = null!;
        protected string TestChannel1Directory { get; private set; } = null!;
        protected string TestChannel2Directory { get; private set; } = null!;
        protected string TestThumbsDirectory { get; private set; } = null!;

        protected Mock<ILogger<T>> CreateMockLogger<T>() => new Mock<ILogger<T>>();

        public virtual async Task InitializeAsync()
        {
            // Create temporary test directory structure
            TestRootDirectory = Path.Combine(Path.GetTempPath(), "AlUmmahNowTests", Guid.NewGuid().ToString());
            TestVideosDirectory = Path.Combine(TestRootDirectory, "Videos");
            TestChannel1Directory = Path.Combine(TestVideosDirectory, "Channel1");
            TestChannel2Directory = Path.Combine(TestVideosDirectory, "Channel2");
            TestThumbsDirectory = Path.Combine(TestChannel1Directory, ".thumbs");

            // Create directories
            Directory.CreateDirectory(TestChannel1Directory);
            Directory.CreateDirectory(TestChannel2Directory);
            Directory.CreateDirectory(TestThumbsDirectory);

            // Create test files
            await CreateTestFilesAsync();
        }

        public virtual Task DisposeAsync()
        {
            try
            {
                if (Directory.Exists(TestRootDirectory))
                {
                    Directory.Delete(TestRootDirectory, recursive: true);
                }
            }
            catch (Exception)
            {
                // Ignore cleanup errors in tests
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Create test video files and associated metadata
        /// </summary>
        protected virtual async Task CreateTestFilesAsync()
        {
            // Channel 1 files
            await CreateTestVideoFileAsync(TestChannel1Directory, "video1.mp4", 1024 * 1024 * 15); // 15MB
            await CreateTestVideoFileAsync(TestChannel1Directory, "video2.mkv", 1024 * 1024 * 25); // 25MB
            await CreateTestVideoFileAsync(TestChannel1Directory, "video3.avi", 1024 * 1024 * 20); // 20MB

            // Channel 2 files
            await CreateTestVideoFileAsync(TestChannel2Directory, "lecture1.mp4", 1024 * 1024 * 30); // 30MB
            await CreateTestVideoFileAsync(TestChannel2Directory, "lecture2.mov", 1024 * 1024 * 35); // 35MB

            // Create thumbnails for some files
            await CreateTestThumbnailAsync(TestThumbsDirectory, "video1.jpg", 1024 * 50); // 50KB
            await CreateTestThumbnailAsync(TestThumbsDirectory, "video2.jpg", 1024 * 45); // 45KB

            // Create info files for some videos
            await CreateTestInfoFileAsync(TestChannel1Directory, "video1.info.json");
            await CreateTestInfoFileAsync(TestChannel2Directory, "lecture1.info.json");

            // Create a duplicate file (same content as video1.mp4)
            await CreateTestVideoFileAsync(TestChannel2Directory, "duplicate.mp4", 1024 * 1024 * 15, 
                await File.ReadAllBytesAsync(Path.Combine(TestChannel1Directory, "video1.mp4")));
        }

        /// <summary>
        /// Create a test video file with specified size
        /// </summary>
        protected async Task<string> CreateTestVideoFileAsync(string directory, string fileName, long sizeInBytes, byte[]? content = null)
        {
            var filePath = Path.Combine(directory, fileName);
            
            if (content != null)
            {
                await File.WriteAllBytesAsync(filePath, content);
            }
            else
            {
                // Create a file with random content of specified size
                var buffer = new byte[Math.Min(sizeInBytes, 8192)];
                Random.Shared.NextBytes(buffer);

                using var stream = File.Create(filePath);
                var remaining = sizeInBytes;
                while (remaining > 0)
                {
                    var toWrite = (int)Math.Min(remaining, buffer.Length);
                    await stream.WriteAsync(buffer.AsMemory(0, toWrite));
                    remaining -= toWrite;
                }
            }

            // Set file times for testing
            var createdTime = DateTime.Now.AddDays(-Random.Shared.Next(1, 30));
            var modifiedTime = createdTime.AddHours(Random.Shared.Next(1, 24));
            
            File.SetCreationTime(filePath, createdTime);
            File.SetLastWriteTime(filePath, modifiedTime);

            return filePath;
        }

        /// <summary>
        /// Create a test thumbnail file
        /// </summary>
        protected async Task<string> CreateTestThumbnailAsync(string directory, string fileName, long sizeInBytes)
        {
            var filePath = Path.Combine(directory, fileName);
            
            // Create a simple test image (just random bytes for testing)
            var buffer = new byte[sizeInBytes];
            Random.Shared.NextBytes(buffer);
            
            await File.WriteAllBytesAsync(filePath, buffer);
            return filePath;
        }

        /// <summary>
        /// Create a test .info.json file with video metadata
        /// </summary>
        protected async Task<string> CreateTestInfoFileAsync(string directory, string fileName)
        {
            var filePath = Path.Combine(directory, fileName);
            
            var infoContent = $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "title": "Test Video",
              "description": "This is a test video for unit testing",
              "duration": 3600,
              "width": 1920,
              "height": 1080,
              "fps": 30,
              "vcodec": "h264",
              "acodec": "aac",
              "filesize": {{1024 * 1024 * 15}},
              "upload_date": "{{DateTime.Now:yyyyMMdd}}",
              "uploader": "Test Channel",
              "uploader_id": "test_channel_123",
              "view_count": 1000,
              "like_count": 50,
              "comment_count": 10,
              "tags": ["test", "video", "sample"],
              "categories": ["Education"],
              "webpage_url": "https://example.com/watch?v=test123",
              "thumbnail": "https://example.com/thumb.jpg",
              "format_id": "22",
              "ext": "mp4"
            }
            """;
            
            await File.WriteAllTextAsync(filePath, infoContent);
            return filePath;
        }

        /// <summary>
        /// Get file info for a test file
        /// </summary>
        protected FileInfo GetTestFileInfo(string directory, string fileName)
        {
            return new FileInfo(Path.Combine(directory, fileName));
        }

        /// <summary>
        /// Verify that a file exists in the test directory
        /// </summary>
        protected bool TestFileExists(string directory, string fileName)
        {
            return File.Exists(Path.Combine(directory, fileName));
        }

        /// <summary>
        /// Get the content of a test file as bytes
        /// </summary>
        protected async Task<byte[]> GetTestFileContentAsync(string directory, string fileName)
        {
            return await File.ReadAllBytesAsync(Path.Combine(directory, fileName));
        }

        /// <summary>
        /// Get the content of a test file as string
        /// </summary>
        protected async Task<string> GetTestFileTextAsync(string directory, string fileName)
        {
            return await File.ReadAllTextAsync(Path.Combine(directory, fileName));
        }
    }
}