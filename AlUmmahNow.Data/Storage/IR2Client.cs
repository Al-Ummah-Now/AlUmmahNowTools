using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlUmmahNow.Data.Storage
{
    public interface IR2Client
    {
        Task<List<S3Bucket>> ListBucketsAsync();
        Task<List<S3Object>> ListObjectsV2Async(string bucketName);
        Task PutObjectAsync(string bucketName, string key, string contentBody);

        /// <summary>
        /// Creates the object only if the key does not exist yet (<c>If-None-Match: *</c>).
        /// Returns <c>false</c> without writing when it already exists.
        /// </summary>
        Task<bool> PutObjectIfAbsentAsync(string bucketName, string key, string contentBody);
        Task<string> GetObjectAsync(string bucketName, string key);
        Task DeleteObjectAsync(string bucketName, string key);
    }
}