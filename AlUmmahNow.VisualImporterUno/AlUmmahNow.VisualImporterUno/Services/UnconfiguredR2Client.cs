using AlUmmahNow.Data.Storage;
using AlUmmahNow.VisualImporterUno.Shell;
using Amazon.S3.Model;

namespace AlUmmahNow.VisualImporterUno.Services;

/// <summary>
/// Storage when this computer has no storage settings: the Import page still opens and finds videos, and only an
/// upload says what's missing. Building the real client without an address throws, which used to take the whole
/// workspace down with it.
/// </summary>
public sealed class UnconfiguredR2Client : IR2Client
{
    private static Exception NotSet() => new InvalidOperationException(Loc.Get("Import_StorageNotSet"));

    public Task<List<S3Bucket>> ListBucketsAsync() => throw NotSet();
    public Task<List<S3Object>> ListObjectsV2Async(string bucketName) => throw NotSet();
    public Task PutObjectAsync(string bucketName, string key, string contentBody) => throw NotSet();
    public Task<bool> PutObjectIfAbsentAsync(string bucketName, string key, string contentBody) => throw NotSet();
    public Task<string> GetObjectAsync(string bucketName, string key) => throw NotSet();
}
