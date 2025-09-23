using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AlUmmahNow.Data.Storage;
using AlUmmahNow.Data.Video;
using AlUmmahNow.Data.Repository;
using AlUmmahNow.Data.Models;
using ShellProgressBar;

namespace AlUmmahNow.DataImporter
{
    public class Importer
    {
        Repository<Video> _repo;
        IStorageClient<Video> _videoStorageClient;
        IStorageClient<Image> _imageStorageClient;
        BunnyStreamClient _bunnyStreamClient;

        public Importer(Repository<Video> repo, IStorageClient<Video> videoStorageClient, IStorageClient<Image> imageStorageClient, BunnyStreamClient bunnyStreamClient)
        {
            _repo = repo;
            _videoStorageClient = videoStorageClient;
            _imageStorageClient = imageStorageClient;
            _bunnyStreamClient = bunnyStreamClient;
        }

        public async Task Import(string folder, string userId, string channelName, string channelThumbnail, string videoStoreRoot, string videoStoreUrl, string imageStoreRoot, string imageStoreUrl)
        {
            var directory = new DirectoryInfo(folder);
            var thumbs = new DirectoryInfo(Path.Combine(folder, ".thumbs\\"));
            var files = directory.GetFiles().Where(f => f.Extension == ".mp4" || f.Extension == ".mkv").ToArray();
            var videos = await _videoStorageClient.ListAsync(videoStoreRoot);
            var thumbnails = await _imageStorageClient.ListAsync(imageStoreRoot);

            using (var pbar = new ProgressBar(files.Length, $"Search Folder files under {directory.FullName}", new ProgressBarOptions { }))
            {
                foreach (var file in files)
                {
                    try
                    {
                        var name = file.Name;
                        var video = await GetVideoInfo(file);
                        if (video != null)
                        {
                            pbar.Tick($"{name} found, {file.Name} already uploaded.");
                            continue;
                        }
                        var videoObject = videos.FirstOrDefault(v => v.ObjectName.EndsWith(name));
                        if (videoObject == null)
                        {
                            await UploadVideo(file, videoStoreRoot);
                            videos = await _videoStorageClient.ListAsync(videoStoreRoot);
                            videoObject = videos.FirstOrDefault(v => v.ObjectName.EndsWith(name));
                        }
                        var thumbnail = GetThumbnail(directory, thumbs, file);
                        if (thumbnail != null)
                        {
                            await UploadThumbnail(thumbnail, imageStoreRoot);
                        }
                        video = new Video
                        {
                            Title = name,
                            Description = name,
                            UserId = userId,
                            VideoUrl = videoStoreUrl + name,
                            ThumbnailUrl = thumbnail == null ? "" : imageStoreUrl + thumbnail.Name,
                            Channel = channelName,
                            ChannelThumbnailUrl = channelThumbnail,
                            DateModified = DateTime.UtcNow
                        };
                        await SaveVideoMetadata(file, video);
                        pbar.Tick($"{file.Name} uploaded.");
                    }
                    catch (Exception ex)
                    {
                        pbar.Tick($"{file.FullName} was not saved, {ex.Message}");
                    }
                }
            }
        }

        public async Task ImportFromBunnyApi(string libraryId, string userId, string channelName, string channelThumbnail, string videoStoreRoot, string videoStoreUrl, string imageStoreRoot, string imageStoreUrl)
        {
            //await Purge();
            var response = await _bunnyStreamClient.GetVideosAsync(libraryId, 1);
            var thumbnails = await _imageStorageClient.ListAsync(imageStoreRoot);

            using (var pbar = new ProgressBar(response.TotalItems, $"Importing videos from Bunny.net API", new ProgressBarOptions { }))
            {
                do
                {
                    foreach (var bunnyVideo in response.Items)
                    {
                        try
                        {
                            var name = bunnyVideo.Title;
                            var video = await _repo.GetObjectAsync("videos", bunnyVideo.Guid);
                            if (video != null)
                            {
                                //pbar.Tick($"{name} found, {name} already uploaded.");
                                //continue;
                            }
                            string? thumbnailUrl;
                            var thumbnail = thumbnails.FirstOrDefault(f => f.ObjectName == name.Replace(Path.GetExtension(name), ".jpg"));
                            thumbnailUrl = thumbnail == null ? $"https://vz-215a1ffd-a5a.b-cdn.net/{bunnyVideo.Guid}/{bunnyVideo.ThumbnailFileName}" : imageStoreUrl + thumbnail.ObjectName;
                            video = new Video
                            {
                                ID = Guid.Parse(bunnyVideo.Guid),
                                Title = name,
                                Description = bunnyVideo.Description,
                                UserId = userId,
                                VideoUrl = bunnyVideo.VideoUrl,
                                ThumbnailUrl = thumbnailUrl,
                                Channel = channelName,
                                ChannelThumbnailUrl = channelThumbnail,
                                DateModified = DateTime.UtcNow,
                                DateCreated = bunnyVideo.DateUploaded,
                                Width = bunnyVideo.Width,
                                Height = bunnyVideo.Height,
                                AvailableResolutions = bunnyVideo.AvailableResolutions?.Split(',').ToList() ?? new List<string>(),
                            };
                            await _repo.SaveObjectAsync("videos", video.ID.ToString(), video);
                            pbar.Tick($"{name} uploaded.");
                        }
                        catch (Exception ex)
                        {
                            pbar.Tick($"{bunnyVideo.Title} was not saved, {ex.Message}");
                        }
                    }
                    response = await _bunnyStreamClient.GetVideosAsync(libraryId, response.CurrentPage + 1);
                } while ((response.CurrentPage - 1) * response.ItemsPerPage < response.TotalItems);
            }
        }

        private async Task Purge()
        {
            try
            {
                var videos = new List<string>();
                do
                {
                    videos = await _repo.ListObjectsAsync("videos");
                    foreach (var item in videos)
                    {
                        await _repo.DeleteObjectAsync("videos", item);
                    }
                } while (videos.Count > 0);
            }
            catch (Exception)
            {

                throw;
            }
        }

        private async Task<Video?> GetVideoInfo(FileInfo file)
        {
            var infoFile = new FileInfo(Path.ChangeExtension(file.FullName, ".r2.json"));
            if (infoFile.Exists)
            {
                using (var reader = infoFile.OpenRead())
                {
                    var jsonString = await new StreamReader(reader).ReadToEndAsync();
                    return JsonSerializer.Deserialize<Video>(jsonString);
                }
            }
            return null;
        }

        private async Task UploadVideo(FileInfo file, string videoStoreRoot)
        {
            using (var reader = file.OpenRead())
            {
                await _videoStorageClient.UploadAsync(reader, videoStoreRoot + file.Name);
            }
        }

        private FileInfo? GetThumbnail(DirectoryInfo directory, DirectoryInfo thumbs, FileInfo file)
        {
            var thumbnail = directory.GetFiles().FirstOrDefault(f => f.Name == file.Name.Replace(file.Extension, ".jpg"));
            if (thumbnail == null && thumbs.Exists)
            {
                thumbnail = thumbs.GetFiles(file.Name.Replace(file.Extension, ".jpg")).FirstOrDefault();
            }
            return thumbnail;
        }

        private async Task UploadThumbnail(FileInfo thumbnail, string imageStoreRoot)
        {
            using (var reader = thumbnail.OpenRead())
            {
                await _imageStorageClient.UploadAsync(reader, imageStoreRoot + thumbnail.Name);
            }
        }

        private async Task SaveVideoMetadata(FileInfo file, Video video)
        {
            await _repo.SaveObjectAsync("videos", video.ID.ToString(), video);
            using (var saved = File.OpenWrite(Path.ChangeExtension(file.FullName, ".r2.json")))
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                var jsonString = JsonSerializer.Serialize(video, options);
                var bytes = Encoding.UTF8.GetBytes(jsonString);
                await saved.WriteAsync(bytes, 0, bytes.Length);
            }
        }
    }
}