using System.Net.Http.Json;

namespace RideHailingApi.Services
{
    public interface IFcmNotificationService
    {
        Task SendAsync(string deviceToken, string title, string body, object? data = null);
        Task SendToUserAsync(int userId, string title, string body, object? data = null);
    }

    public class FcmNotificationService : IFcmNotificationService
    {
        private readonly IHttpClientFactory _http;
        private readonly DeviceTokenStore _tokenStore;
        private readonly string? _serverKey;
        private readonly ILogger<FcmNotificationService> _logger;

        public FcmNotificationService(
            IHttpClientFactory http,
            DeviceTokenStore tokenStore,
            IConfiguration config,
            ILogger<FcmNotificationService> logger)
        {
            _http = http;
            _tokenStore = tokenStore;
            _serverKey = config["FcmSettings:ServerKey"];
            _logger = logger;
        }

        public async Task SendToUserAsync(int userId, string title, string body, object? data = null)
        {
            var token = _tokenStore.Get(userId);
            if (string.IsNullOrEmpty(token)) return;
            await SendAsync(token, title, body, data);
        }

        public async Task SendAsync(string deviceToken, string title, string body, object? data = null)
        {
            // Dev mode: log to console when FCM server key is not configured
            if (string.IsNullOrEmpty(_serverKey))
            {
                var preview = deviceToken.Length > 20 ? deviceToken[..20] + "…" : deviceToken;
                _logger.LogInformation(
                    "🔔 [FCM DEV MODE] Token: {Token} | Title: {Title} | Body: {Body}",
                    preview, title, body);
                return;
            }

            var payload = new
            {
                to = deviceToken,
                notification = new { title, body, sound = "default" },
                data
            };

            try
            {
                var client = _http.CreateClient("fcm");
                var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://fcm.googleapis.com/fcm/send");
                request.Headers.Add("Authorization", $"key={_serverKey}");
                request.Content = JsonContent.Create(payload);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("FCM returned {Status} for token {Token}",
                        response.StatusCode, deviceToken[..Math.Min(20, deviceToken.Length)]);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FCM send exception");
            }
        }
    }
}
