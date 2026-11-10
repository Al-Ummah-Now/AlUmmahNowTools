using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace AlUmmahNow.Data.Storage
{
    public class R2Client : IDisposable, IR2Client
    {
        private IAmazonS3 s3Client;

        public R2Client(string url, string accessKey, string secret)
        {
            var credentials = new BasicAWSCredentials(accessKey, secret);
            s3Client = new AmazonS3Client(credentials, new AmazonS3Config
            {
                ServiceURL = url,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });
        }

        /// <summary>Wraps an existing S3 client (tests, or a client configured elsewhere).</summary>
        public R2Client(IAmazonS3 client)
        {
            s3Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public IAmazonS3 GetClient()
        {
            return s3Client;
        }

        public void Dispose()
        {
            s3Client.Dispose();
        }

        public async Task<List<S3Bucket>> ListBucketsAsync()
        {
            var result = await s3Client.ListBucketsAsync();
            return result.Buckets;
        }

        public async Task<List<S3Object>> ListObjectsV2Async(string bucketName)
        {
            // Follow continuation tokens: one response holds at most 1000 keys.
            var objects = new List<S3Object>();
            var request = new ListObjectsV2Request
            {
                BucketName = bucketName,
            };
            ListObjectsV2Response result;
            do
            {
                result = await s3Client.ListObjectsV2Async(request);
                if (result.S3Objects != null) objects.AddRange(result.S3Objects);
                request.ContinuationToken = result.NextContinuationToken;
            } while (result.IsTruncated == true && !string.IsNullOrEmpty(result.NextContinuationToken));
            return objects;
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
                    var result = await s3Client.PutObjectAsync(request);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error putting object in S3 bucket: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> PutObjectIfAbsentAsync(string bucketName, string key, string contentBody)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(contentBody));
            var request = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                InputStream = stream,
                DisablePayloadSigning = true,
                IfNoneMatch = "*",
            };
            try
            {
                await s3Client.PutObjectAsync(request);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
            {
                return false;
            }
        }

        public async Task<string> GetObjectAsync(string bucketName, string key)
        {
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key,
            };
            var result = await s3Client.GetObjectAsync(request);
            using (var reader = new StreamReader(result.ResponseStream))
            {
                return await reader.ReadToEndAsync();
            }
        }
    }
}