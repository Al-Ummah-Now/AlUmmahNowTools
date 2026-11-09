using System.Collections.Concurrent;
using AlUmmahNow.Data.Models;
using AlUmmahNow.Data.Storage;

namespace AlUmmahNow.Tests.Infrastructure;

/// <summary>
/// Thread-safe in-memory stand-in for Bunny Storage: a key → bytes map with the same semantics as the
/// real client (PUT replaces, GET returns null when absent). Counts PUTs so tests can assert no re-upload.
/// </summary>
public sealed class InMemoryStorageClient<T> : IStorageClient<T>
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private int _puts;

    public int Puts => Volatile.Read(ref _puts);
    public IReadOnlyDictionary<string, byte[]> Objects => _objects;

    /// <summary>Delay inside UploadAsync, to widen race windows in concurrency tests.</summary>
    public TimeSpan UploadDelay { get; set; }

    public async Task UploadAsync(Stream stream, string path)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        if (UploadDelay > TimeSpan.Zero) await Task.Delay(UploadDelay);
        _objects[path] = ms.ToArray();
        Interlocked.Increment(ref _puts);
    }

    public Task<StorageObject?> GetAsync(string path)
    {
        if (!_objects.TryGetValue(path, out var bytes)) return Task.FromResult<StorageObject?>(null);
        var slash = path.LastIndexOf('/');
        return Task.FromResult<StorageObject?>(new StorageObject
        {
            Path = path[..(slash + 1)],
            ObjectName = path[(slash + 1)..],
            Length = bytes.Length,
        });
    }

    public Task<List<StorageObject>> ListAsync(string path)
        => Task.FromResult(_objects.Keys.Where(k => k.StartsWith(path, StringComparison.Ordinal))
            .Select(k => new StorageObject { Path = path, ObjectName = k[path.Length..], Length = _objects[k].Length })
            .ToList());

    public Task<Stream> DownloadAsync(string path) => Task.FromResult<Stream>(new MemoryStream(_objects[path]));

    public Task DeleteAsync(string path)
    {
        _objects.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}
