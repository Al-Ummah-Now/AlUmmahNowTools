using AlUmmahNow.Data.Storage;
using ShellProgressBar;

namespace AlUmmahNow.BunnyBulkUploader
{
    internal class Uploader
    {
        IStorageClient<Video> _videoStorageClient;

        public Uploader(IStorageClient<Video> videoStorageClient)
        {
            _videoStorageClient = videoStorageClient;
        }

        public async Task Upload(string folder, string videoStoreRoot)
        {
            var directory = new DirectoryInfo(folder);
            var files = directory.GetFiles().Where(f => f.Extension == ".mp4" || f.Extension == ".mkv").ToArray();

            using (var pbar = new ProgressBar(files.Length, $"Search Folder files under {directory.FullName}", new ProgressBarOptions { }))
            {
                foreach (var file in files)
                {
                    try
                    {
                        await UploadVideo(file, videoStoreRoot);
                        pbar.Tick($"{file.Name} uploaded.");
                    }
                    catch (Exception ex)
                    {
                        pbar.Tick($"{file.FullName} was not saved, {ex.Message}");
                    }
                }
            }
        }

        private async Task UploadVideo(FileInfo file, string videoStoreRoot)
        {
            using (var reader = file.OpenRead())
            {
                await _videoStorageClient.UploadAsync(reader, videoStoreRoot + file.Name);
            }
        }

    }
}
