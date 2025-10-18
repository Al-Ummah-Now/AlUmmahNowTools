using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RestSharp;
using AlUmmahNow.Data.Models;

namespace AlUmmahNow.Data.Video
{
    public class BunnyStreamClient
    {
        private readonly string _apiKey;
        private readonly string _baseUrl;

        public BunnyStreamClient(string apiKey, string baseUrl = "https://video.bunnycdn.com")
        {
            _apiKey = apiKey;
            _baseUrl = baseUrl;
        }

        public async Task<BunnyVideoResponse?> GetVideosAsync(string libraryId, int page, int itemsPerPage = 100, string? search = null, string? orderBy = "date")
        {
            try
            {
                var urlBuilder = new StringBuilder($"{_baseUrl}/library/{libraryId}/videos");
                urlBuilder.Append($"?page={page}");
                urlBuilder.Append($"&itemsPerPage={itemsPerPage}");
                urlBuilder.Append($"&orderBy={orderBy}");
                if (!string.IsNullOrEmpty(search))
                {
                    urlBuilder.Append($"&search={search}");
                }
                var url = urlBuilder.ToString();
                var options = new RestClientOptions(url);
                var client = new RestClient(options);
                var request = new RestRequest("");
                request.AddHeader("accept", "application/json");
                request.AddHeader("AccessKey", _apiKey);
                var response = await client.GetAsync(request);
                var opt = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };
                if (response.Content is null)
                    return null;

                return JsonSerializer.Deserialize<BunnyVideoResponse>(response.Content, opt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching videos from BunnyStream: {ex.Message}");
                throw;
            }
        }

        public async Task<BunnyVideo?> GetVideoAsync(string libraryId, string videoId)
        {
            var options = new RestClientOptions($"{_baseUrl}/library/{libraryId}/videos/{videoId}");
            var client = new RestClient(options);
            var request = new RestRequest("");
            request.AddHeader("accept", "application/json");
            request.AddHeader("AccessKey", _apiKey);
            var response = await client.GetAsync(request);
            var opt = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            return response.Content is null ? null : JsonSerializer.Deserialize<BunnyVideo>(response.Content, opt);
        }

        public async Task<bool> UploadVideoAsync(string libraryId, string videoId, Stream videoStream)
        {
            try
            {
                var options = new RestClientOptions($"{_baseUrl}/library/{libraryId}/videos/{videoId}");
                var client = new RestClient(options);
                var request = new RestRequest("", Method.Put);
                request.AddHeader("AccessKey", _apiKey);
                request.AddHeader("Content-Type", "application/octet-stream");
                request.AddParameter("application/octet-stream", videoStream, ParameterType.RequestBody);
                var response = await client.ExecuteAsync(request);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading video to BunnyStream: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteVideoAsync(string libraryId, string videoId)
        {
            try
            {
                var options = new RestClientOptions($"{_baseUrl}/library/{libraryId}/videos/{videoId}");
                var client = new RestClient(options);
                var request = new RestRequest("", Method.Delete);
                request.AddHeader("AccessKey", _apiKey);
                var response = await client.ExecuteAsync(request);
                return response.StatusCode == System.Net.HttpStatusCode.OK;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting video from BunnyStream: {ex.Message}");
                throw;
            }
        }
    }
    
    public class BunnyVideoResponse
    {
        public int TotalItems { get; set; }
        public int CurrentPage { get; set; }
        public int ItemsPerPage { get; set; }
        public List<BunnyVideo> Items { get; set; } = new List<BunnyVideo>();
    }
}