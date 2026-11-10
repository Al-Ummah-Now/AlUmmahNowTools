using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AlUmmahNow.Data.Models;

namespace AlUmmahNow.Data.Storage
{
    public interface IStorageClient<T>
    {
        Task UploadAsync(Stream stream, string path);
        Task<StorageObject?> GetAsync(string path);
        Task<List<StorageObject>> ListAsync(string path);
        Task<Stream> DownloadAsync(string path);
    }
}