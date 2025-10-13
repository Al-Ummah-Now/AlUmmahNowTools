using BunnyCDN.Net.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlUmmahNow.Data.Models;

namespace AlUmmahNow.Data.Storage
{
    public class BunnyStorageClient<T> : IStorageClient<T>
    {
        BunnyCDNStorage bunnyCDNStorage;

        public BunnyStorageClient(string zoneName, string key, string mainRegion)
        {
            bunnyCDNStorage = new BunnyCDNStorage(zoneName, key, mainRegion);
        }

        public async Task DeleteAsync(string path)
        {
            await bunnyCDNStorage.DeleteObjectAsync(path);
        }

        public async Task<Stream> DownloadAsync(string path)
        {
            return await bunnyCDNStorage.DownloadObjectAsStreamAsync(path);
        }

        public async Task<List<StorageObject>> ListAsync(string path)
        {
            try
            {
                var result = await bunnyCDNStorage.GetStorageObjectsAsync(path);
                return result.Select(o =>
                    new StorageObject
                    {
                        Guid = o.Guid,
                        ObjectName = o.ObjectName,
                        DateCreated = o.DateCreated,
                        LastChanged = o.LastChanged,
                        Path = o.Path,
                        Length = o.Length,
                        IsDirectory = o.IsDirectory
                    }).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing objects in BunnyCDN: {ex.Message}");
                return new List<StorageObject>();
            }
        }

        public async Task<StorageObject?> GetAsync(string path)
        {
            try
            {
                var result = await ListAsync(path);
                var obj = result.FirstOrDefault();

                if (obj == null)
                {
                    return null;
                }

                return new StorageObject
                {
                    Guid = obj.Guid,
                    ObjectName = obj.ObjectName,
                    DateCreated = obj.DateCreated,
                    LastChanged = obj.LastChanged,
                    Path = obj.Path,
                    Length = obj.Length,
                    IsDirectory = obj.IsDirectory
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting object from BunnyCDN: {ex.Message}");
                return null;
            }
        }

        public async Task UploadAsync(Stream stream, string path)
        {
            await bunnyCDNStorage.UploadAsync(stream, path);
        }
    }
}