using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading.Tasks;
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

        // Method to retrieve and deserialize an object of type T from S3
        public async Task<T?> GetObjectAsync(string bucketName, string key)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                var jsonString = await _r2Client.GetObjectAsync(bucketName, key);
                return jsonString is not null ? JsonSerializer.Deserialize<T>(jsonString, options) : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting object from S3: {ex.Message}");
                return null;
            }
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