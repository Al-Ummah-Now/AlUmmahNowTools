using System.Collections.Concurrent;
using Amazon.S3;
using Amazon.S3.Model;
using AlUmmahNow.Data.Storage;

namespace AlUmmahNow.Tests.Infrastructure;

/// <summary>
/// Thread-safe in-memory R2 bucket store. Missing keys throw <see cref="AmazonS3Exception"/> with
/// status 404 / NoSuchKey like the real API.
/// </summary>
public class InMemoryR2Client : IR2Client
{
    protected readonly ConcurrentDictionary<(string Bucket, string Key), string> Store = new();
    private int _puts;

    public int Puts => Volatile.Read(ref _puts);

    public IReadOnlyDictionary<string, string> Bucket(string bucket)
        => Store.Where(kv => kv.Key.Bucket == bucket).ToDictionary(kv => kv.Key.Key, kv => kv.Value);

    /// <summary>Called before every put; throw from it to simulate a failing write.</summary>
    public Action<string, string>? BeforePut { get; set; }

    public Task<List<S3Bucket>> ListBucketsAsync()
        => Task.FromResult(Store.Keys.Select(k => k.Bucket).Distinct().Select(b => new S3Bucket { BucketName = b }).ToList());

    public Task<List<S3Object>> ListObjectsV2Async(string bucketName)
        => Task.FromResult(Store.Keys.Where(k => k.Bucket == bucketName).Select(k => new S3Object { Key = k.Key }).ToList());

    public virtual Task PutObjectAsync(string bucketName, string key, string contentBody)
    {
        BeforePut?.Invoke(bucketName, key);
        Store[(bucketName, key)] = contentBody;
        Interlocked.Increment(ref _puts);
        return Task.CompletedTask;
    }

    public Task<string> GetObjectAsync(string bucketName, string key)
        => Store.TryGetValue((bucketName, key), out var body)
            ? Task.FromResult(body)
            : throw new AmazonS3Exception("The specified key does not exist.") { StatusCode = System.Net.HttpStatusCode.NotFound, ErrorCode = "NoSuchKey" };

    public Task DeleteObjectAsync(string bucketName, string key)
    {
        Store.TryRemove((bucketName, key), out _);
        return Task.CompletedTask;
    }
}
