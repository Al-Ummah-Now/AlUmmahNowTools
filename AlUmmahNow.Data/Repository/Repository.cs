using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Models;

namespace AlUmmahNow.Data.Repository
{
    public class Repository<T> where T : class
    {
        private readonly IR2Client _r2Client;

        public Repository(IR2Client r2Client)
        {
            _r2Client = r2Client;
        }

        // Method using R2Client to list S3 buckets and return BucketDto
        public async Task<List<Bucket>> ListS3BucketsAsync()
        {
            try
            {
                var buckets = await _r2Client.ListBucketsAsync();
                return buckets.Select(b => new Bucket
                {
                    BucketName = b.BucketName,
                    CreationDate = b.CreationDate
                }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing S3 buckets: {ex.Message}");
                return new List<Bucket>();
            }
        }

        // Example method using R2Client to list objects in a bucket
        public async Task<List<string>> ListObjectsAsync(string bucketName)
        {
            try
            {
                var result = await _r2Client.ListObjectsV2Async(bucketName);
                return result.Select(o => o.Key).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing objects in S3 bucket: {ex.Message}");
                throw;
            }
        }

        // Method to serialize and save an object of type T to S3
        public async Task SaveObjectAsync(string bucketName, string key, T obj)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                var jsonString = JsonSerializer.Serialize(obj, options);
                await _r2Client.PutObjectAsync(bucketName, key, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving object to S3: {ex.Message}");
                throw;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        /// <summary>
        /// Writes <paramref name="obj"/> only if <paramref name="key"/> does not exist yet. Returns <c>false</c>
        /// (and writes nothing) when it does, so concurrent or repeated imports of one video create exactly
        /// one record.
        /// </summary>
        public Task<bool> CreateObjectAsync(string bucketName, string key, T obj)
            => _r2Client.PutObjectIfAbsentAsync(bucketName, key, JsonSerializer.Serialize(obj, JsonOptions));

        /// <summary>
        /// Reads and deserializes an object; <c>null</c> only when the key does not exist. Other errors are
        /// thrown, so a failed read is never taken as "missing" (which would let a caller overwrite it).
        /// </summary>
        public async Task<T?> GetObjectAsync(string bucketName, string key)
        {
            string jsonString;
            try
            {
                jsonString = await _r2Client.GetObjectAsync(bucketName, key);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
            {
                return null;
            }
            return jsonString is not null ? JsonSerializer.Deserialize<T>(jsonString, JsonOptions) : null;
        }

        // Method to delete an object from S3
        public async Task DeleteObjectAsync(string bucketName, string key)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                await _r2Client.DeleteObjectAsync(bucketName, key);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting object from S3: {ex.Message}");
                throw;
            }
        }
    }
}