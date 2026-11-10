using Xunit;
using Moq;
using AlUmmahNow.Data.Storage;
using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AlUmmahNow.Tests.Storage
{
    public class R2ClientTests : IDisposable
    {
        private readonly R2Client _r2Client;
        private readonly Mock<IAmazonS3> _mockS3Client;
        private readonly TestableR2Client _testableR2Client;

        public R2ClientTests()
        {
            // Create real R2Client for basic tests
            _r2Client = new R2Client("https://test.r2.dev", "test-key", "test-secret");
            
            // Create mock for detailed testing
            _mockS3Client = new Mock<IAmazonS3>();
            _testableR2Client = new TestableR2Client(_mockS3Client.Object);
        }

        [Fact]
        public void Constructor_WithValidParameters_InitializesCorrectly()
        {
            // Act
            using var client = new R2Client("https://test.r2.dev", "test-key", "test-secret");

            // Assert
            Assert.NotNull(client);
            Assert.NotNull(client.GetClient());
        }

        [Fact]
        public void Constructor_WithNullUrl_ThrowsAmazonClientException()
        {
            // Act & Assert - AWS SDK throws AmazonClientException for null URL
            Assert.Throws<Amazon.Runtime.AmazonClientException>(() => new R2Client(null!, "test-key", "test-secret"));
        }

        [Fact]  
        public void Constructor_WithNullAccessKey_CreatesClientWithNullCredentials()
        {
            // Act & Assert - AWS SDK allows null credentials but will fail on usage
            var exception = Record.Exception(() => new R2Client("https://test.r2.dev", null!, "test-secret"));
            
            // Constructor succeeds, but usage would fail
            Assert.Null(exception);
        }

        [Fact]
        public void Constructor_WithNullSecret_ThrowsArgumentNullException()
        {
            // Act & Assert - AWS SDK throws ArgumentNullException for null secret key
            Assert.Throws<ArgumentNullException>(() => new R2Client("https://test.r2.dev", "test-key", null!));
        }

        [Fact]
        public async Task ListBucketsAsync_CallsS3ClientListBuckets()
        {
            // Arrange
            var expectedBuckets = new List<S3Bucket>
            {
                new S3Bucket { BucketName = "bucket1", CreationDate = DateTime.UtcNow },
                new S3Bucket { BucketName = "bucket2", CreationDate = DateTime.UtcNow }
            };

            var response = new ListBucketsResponse
            {
                Buckets = expectedBuckets
            };

            _mockS3Client.Setup(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()))
                        .ReturnsAsync(response);

            // Act
            var result = await _testableR2Client.ListBucketsAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("bucket1", result[0].BucketName);
            Assert.Equal("bucket2", result[1].BucketName);
            
            _mockS3Client.Verify(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ListObjectsV2Async_WithBucketName_CallsS3ClientWithCorrectRequest()
        {
            // Arrange
            var bucketName = "test-bucket";
            var expectedObjects = new List<S3Object>
            {
                new S3Object { Key = "object1.txt", Size = 100 },
                new S3Object { Key = "object2.txt", Size = 200 }
            };

            var response = new ListObjectsV2Response
            {
                S3Objects = expectedObjects
            };

            _mockS3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(response);

            // Act
            var result = await _testableR2Client.ListObjectsV2Async(bucketName);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("object1.txt", result[0].Key);
            Assert.Equal("object2.txt", result[1].Key);
            
            _mockS3Client.Verify(x => x.ListObjectsV2Async(
                It.Is<ListObjectsV2Request>(r => r.BucketName == bucketName),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PutObjectAsync_WithValidParameters_CallsS3ClientWithCorrectRequest()
        {
            // Arrange
            var bucketName = "test-bucket";
            var key = "test-key";
            var content = "test content";

            var response = new PutObjectResponse
            {
                ETag = "test-etag"
            };

            _mockS3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(response);

            // Act
            await _testableR2Client.PutObjectAsync(bucketName, key, content);

            // Assert
            _mockS3Client.Verify(x => x.PutObjectAsync(
                It.Is<PutObjectRequest>(r => 
                    r.BucketName == bucketName && 
                    r.Key == key &&
                    r.DisablePayloadSigning == true),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PutObjectAsync_WhenS3ThrowsException_PropagatesException()
        {
            // Arrange
            var bucketName = "test-bucket";
            var key = "test-key";
            var content = "test content";

            _mockS3Client.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new AmazonS3Exception("S3 Error"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
                () => _testableR2Client.PutObjectAsync(bucketName, key, content));
            
            Assert.Equal("S3 Error", exception.Message);
        }

        [Fact]
        public async Task GetObjectAsync_WithValidParameters_ReturnsObjectContent()
        {
            // Arrange  
            var bucketName = "test-bucket";
            var key = "test-key";
            var expectedContent = "test content";

            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(expectedContent));
            var response = new GetObjectResponse
            {
                ResponseStream = stream
            };

            _mockS3Client.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(response);

            // Act
            var result = await _testableR2Client.GetObjectAsync(bucketName, key);

            // Assert
            Assert.Equal(expectedContent, result);
            
            _mockS3Client.Verify(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(r => 
                    r.BucketName == bucketName && 
                    r.Key == key),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void GetClient_ReturnsUnderlyingS3Client()
        {
            // Act
            var client = _testableR2Client.GetClient();

            // Assert
            Assert.NotNull(client);
            Assert.Same(_mockS3Client.Object, client);
        }

        [Fact]
        public void Dispose_DisposesUnderlyingS3Client()
        {
            // Act
            _testableR2Client.Dispose();

            // Assert
            _mockS3Client.Verify(x => x.Dispose(), Times.Once);
        }

        [Fact]
        public async Task ListBucketsAsync_WhenS3ClientThrows_PropagatesException()
        {
            // Arrange
            _mockS3Client.Setup(x => x.ListBucketsAsync(It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new AmazonS3Exception("Connection failed"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
                () => _testableR2Client.ListBucketsAsync());
            
            Assert.Equal("Connection failed", exception.Message);
        }

        [Fact]
        public async Task ListObjectsV2Async_WhenS3ClientThrows_PropagatesException()
        {
            // Arrange
            var bucketName = "test-bucket";
            
            _mockS3Client.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new AmazonS3Exception("Bucket not found"));

            // Act & Assert
            var exception = await Assert.ThrowsAsync<AmazonS3Exception>(
                () => _testableR2Client.ListObjectsV2Async(bucketName));
            
            Assert.Equal("Bucket not found", exception.Message);
        }

        public void Dispose()
        {
            _r2Client?.Dispose();
            _testableR2Client?.Dispose();
        }

        // Testable version of R2Client that accepts an IAmazonS3 dependency
        private class TestableR2Client : IDisposable, IR2Client
        {
            private readonly IAmazonS3 _s3Client;

            public TestableR2Client(IAmazonS3 s3Client)
            {
                _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
            }

            public IAmazonS3 GetClient() => _s3Client;

            public void Dispose() => _s3Client.Dispose();

            public async Task<List<S3Bucket>> ListBucketsAsync()
            {
                var result = await _s3Client.ListBucketsAsync();
                return result.Buckets;
            }

            public async Task<List<S3Object>> ListObjectsV2Async(string bucketName)
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = bucketName,
                };
                var result = await _s3Client.ListObjectsV2Async(request);
                return result.S3Objects;
            }

            public async Task PutObjectAsync(string bucketName, string key, string contentBody)
            {
                try
                {
                    using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contentBody)))
                    {
                        stream.Position = 0;
                        var request = new PutObjectRequest
                        {
                            BucketName = bucketName,
                            Key = key,
                            InputStream = stream,
                            DisablePayloadSigning = true,
                        };
                        await _s3Client.PutObjectAsync(request);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error putting object in S3 bucket: {ex.Message}");
                    throw;
                }
            }

            public Task<bool> PutObjectIfAbsentAsync(string bucketName, string key, string contentBody)
                => new R2Client(_s3Client).PutObjectIfAbsentAsync(bucketName, key, contentBody);

            public async Task<string> GetObjectAsync(string bucketName, string key)
            {
                var request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                };
                var result = await _s3Client.GetObjectAsync(request);
                using (var reader = new StreamReader(result.ResponseStream))
                {
                    return await reader.ReadToEndAsync();
                }
            }
        }
    }
}