using Xunit;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon.S3.Model;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Models;
using System.Text.Json;

namespace AlUmmahNow.Tests.Repository
{
    public class RepositoryTests
    {
        private readonly Mock<IR2Client> _mockR2Client;
        private readonly Repository<TestModel> _repository;

        public RepositoryTests()
        {
            _mockR2Client = new Mock<IR2Client>();
            _repository = new Repository<TestModel>(_mockR2Client.Object);
        }

        [Fact]
        public async Task ListS3BucketsAsync_ReturnsExpectedBuckets()
        {
            // Arrange
            var expectedBuckets = new List<S3Bucket>
            {
                new S3Bucket { BucketName = "test-bucket-1", CreationDate = DateTime.UtcNow },
                new S3Bucket { BucketName = "test-bucket-2", CreationDate = DateTime.UtcNow }
            };

            _mockR2Client.Setup(x => x.ListBucketsAsync())
                        .ReturnsAsync(expectedBuckets);

            // Act
            var result = await _repository.ListS3BucketsAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("test-bucket-1", result[0].BucketName);
            Assert.Equal("test-bucket-2", result[1].BucketName);
            _mockR2Client.Verify(x => x.ListBucketsAsync(), Times.Once);
        }

        [Fact]
        public async Task ListS3BucketsAsync_HandlesException_ReturnsEmptyList()
        {
            // Arrange
            _mockR2Client.Setup(x => x.ListBucketsAsync())
                        .ThrowsAsync(new Exception("S3 error"));

            // Act
            var result = await _repository.ListS3BucketsAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
            _mockR2Client.Verify(x => x.ListBucketsAsync(), Times.Once);
        }

        [Fact]
        public async Task ListObjectsAsync_ReturnsExpectedObjects()
        {
            // Arrange
            var bucketName = "test-bucket";
            var expectedObjects = new List<S3Object>
            {
                new S3Object { Key = "object1.json" },
                new S3Object { Key = "object2.json" }
            };

            _mockR2Client.Setup(x => x.ListObjectsV2Async(bucketName))
                        .ReturnsAsync(expectedObjects);

            // Act
            var result = await _repository.ListObjectsAsync(bucketName);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("object1.json", result[0]);
            Assert.Equal("object2.json", result[1]);
            _mockR2Client.Verify(x => x.ListObjectsV2Async(bucketName), Times.Once);
        }

        [Fact]
        public async Task ListObjectsAsync_HandlesException_ThrowsException()
        {
            // Arrange
            var bucketName = "test-bucket";
            _mockR2Client.Setup(x => x.ListObjectsV2Async(bucketName))
                        .ThrowsAsync(new Exception("S3 error"));

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _repository.ListObjectsAsync(bucketName));
            _mockR2Client.Verify(x => x.ListObjectsV2Async(bucketName), Times.Once);
        }

        [Fact]
        public async Task SaveObjectAsync_SerializesAndSavesObject()
        {
            // Arrange
            var bucketName = "test-bucket";
            var key = "test-key";
            var testObject = new TestModel { Id = 1, Name = "Test", IsActive = true };

            _mockR2Client.Setup(x => x.PutObjectAsync(bucketName, key, It.IsAny<string>()))
                        .Returns(Task.CompletedTask);

            // Act
            await _repository.SaveObjectAsync(bucketName, key, testObject);

            // Assert
            _mockR2Client.Verify(x => x.PutObjectAsync(
                bucketName, 
                key, 
                It.Is<string>(s => s.Contains("\"id\": 1") && s.Contains("\"name\": \"Test\""))), 
                Times.Once);
        }

        [Fact]
        public async Task SaveObjectAsync_HandlesException_ThrowsException()
        {
            // Arrange
            var bucketName = "test-bucket";
            var key = "test-key";
            var testObject = new TestModel { Id = 1, Name = "Test", IsActive = true };

            _mockR2Client.Setup(x => x.PutObjectAsync(bucketName, key, It.IsAny<string>()))
                        .ThrowsAsync(new Exception("S3 error"));

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _repository.SaveObjectAsync(bucketName, key, testObject));
        }

        [Fact]
        public async Task GetObjectAsync_ReturnsDeserializedObject()
        {
            // Arrange
            var bucketName = "test-bucket";
            var key = "test-key";
            var testObject = new TestModel { Id = 1, Name = "Test", IsActive = true };
            var jsonString = JsonSerializer.Serialize(testObject, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });

            _mockR2Client.Setup(x => x.GetObjectAsync(bucketName, key))
                        .ReturnsAsync(jsonString);

            // Act
            var result = await _repository.GetObjectAsync(bucketName, key);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(testObject.Id, result.Id);
            Assert.Equal(testObject.Name, result.Name);
            Assert.Equal(testObject.IsActive, result.IsActive);
            _mockR2Client.Verify(x => x.GetObjectAsync(bucketName, key), Times.Once);
        }

        [Fact]
        public async Task GetObjectAsync_ReturnsNull_WhenObjectNotFound()
        {
            // Arrange
            var bucketName = "test-bucket";
            var key = "test-key";

            _mockR2Client.Setup(x => x.GetObjectAsync(bucketName, key))
                        .ReturnsAsync((string?)null);

            // Act
            var result = await _repository.GetObjectAsync(bucketName, key);

            // Assert
            Assert.Null(result);
            _mockR2Client.Verify(x => x.GetObjectAsync(bucketName, key), Times.Once);
        }

        [Fact]
        public async Task GetObjectAsync_HandlesException_ReturnsNull()
        {
            // Arrange
            var bucketName = "test-bucket";
            var key = "test-key";

            _mockR2Client.Setup(x => x.GetObjectAsync(bucketName, key))
                        .ThrowsAsync(new Exception("S3 error"));

            // Act
            var result = await _repository.GetObjectAsync(bucketName, key);

            // Assert
            Assert.Null(result);
            _mockR2Client.Verify(x => x.GetObjectAsync(bucketName, key), Times.Once);
        }

        // Test model class
        public class TestModel
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool IsActive { get; set; }
        }
    }
}