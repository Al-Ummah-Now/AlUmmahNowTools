using Microsoft.AspNetCore.SignalR;

namespace AlUmmahNow.FileScanService.Services
{
    /// <summary>
    /// Background service that handles WebSocket communication
    /// This runs alongside the main file scanning worker
    /// </summary>
    public class WebSocketCommunicationService : BackgroundService
    {
        private readonly ILogger<WebSocketCommunicationService> _logger;
        private readonly IServiceProvider _serviceProvider;

        public WebSocketCommunicationService(
            ILogger<WebSocketCommunicationService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WebSocket Communication Service started");

            // This service doesn't need to do active work since WebSocket communication
            // is event-driven through the WebSocketManager. It just keeps the service alive.
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Perform periodic health checks or cleanup if needed
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    
                    // Log service status periodically
                    _logger.LogDebug("WebSocket Communication Service is running");
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in WebSocket Communication Service");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }

            _logger.LogInformation("WebSocket Communication Service stopped");
        }
    }
}