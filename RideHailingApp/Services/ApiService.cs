using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.IO;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace RideHailingApp.Services
{
    public class ApiService
    {
        private readonly HttpClient         _httpClient;
        private readonly GeoLocatorService  _geo;
        private readonly OfflineQueueService _offlineQueue;
        private string? _jwtToken;

        public ApiService(GeoLocatorService geo, OfflineQueueService offlineQueue)
        {
            _geo          = geo;
            _offlineQueue = offlineQueue;

            var handler = new HttpClientHandler();
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            string? runtimeUrl = null;
            try
            {
                var appDataPath = Path.Combine(FileSystem.AppDataDirectory, "backend_url.txt");
                if (File.Exists(appDataPath))
                    runtimeUrl = File.ReadAllText(appDataPath).Trim();
            }
            catch { }

            if (string.IsNullOrEmpty(runtimeUrl))
            {
                try
                {
                    using var s = FileSystem.OpenAppPackageFileAsync("backend_url.txt").GetAwaiter().GetResult();
                    using var sr = new StreamReader(s);
                    var txt = sr.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(txt)) runtimeUrl = txt;
                }
                catch { }
            }

            _httpClient.BaseAddress = new Uri("https://jawline-filling-amount.ngrok-free.dev");

            // Process queued bookings when connectivity is restored
            Connectivity.ConnectivityChanged += OnConnectivityChanged;
        }

        // ───────────────── Token Management ─────────────────

        public void SetToken(string token)
        {
            _jwtToken = token;
            Preferences.Set("jwtToken", token);
        }

        public string? GetToken() => _jwtToken ?? Preferences.Get("jwtToken", (string?)null);

        public void ClearToken()
        {
            _jwtToken = null;
            Preferences.Remove("jwtToken");
        }

        private bool IsTokenExpiringSoon()
        {
            var token = GetToken();
            if (string.IsNullOrEmpty(token)) return true;
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return true;
                var payload = parts[1];
                payload += new string('=', (4 - payload.Length % 4) % 4);
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("exp", out var expEl))
                {
                    var exp = DateTimeOffset.FromUnixTimeSeconds(expEl.GetInt64());
                    return exp < DateTimeOffset.UtcNow.AddMinutes(2);
                }
            }
            catch { }
            return true;
        }

        public async Task<bool> TryRefreshAccessTokenAsync()
        {
            try
            {
                var refreshToken = await SecureStorage.GetAsync("refreshToken");
                if (string.IsNullOrEmpty(refreshToken)) return false;

                var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
                req.Content = JsonContent.Create(new { RefreshToken = refreshToken });
                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return false;

                var data = await resp.Content.ReadFromJsonAsync<RefreshResponse>();
                if (data == null) return false;

                SetToken(data.AccessToken);
                await SecureStorage.SetAsync("refreshToken", data.RefreshToken);
                return true;
            }
            catch { return false; }
        }

        // ───────────────── Helpers ─────────────────

        private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body = null)
        {
            var req = new HttpRequestMessage(method, path);
            req.Headers.Add("X-Region", _geo.GetCachedRegion());
            var token = GetToken();
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null)
                req.Content = JsonContent.Create(body);
            return req;
        }

        private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage req)
        {
            // Auto-refresh if token is about to expire
            if (IsTokenExpiringSoon())
                await TryRefreshAccessTokenAsync();

            // Stamp the latest token on the request
            var freshToken = GetToken();
            if (!string.IsNullOrEmpty(freshToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);

            try
            {
                var resp = await _httpClient.SendAsync(req);

                if (resp.IsSuccessStatusCode)
                {
                    var data = await resp.Content.ReadFromJsonAsync<T>();
                    return ApiResult<T>.Success(data!);
                }

                if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
                    return ApiResult<T>.ReadOnly("Hệ thống đang ở chế độ Read-Only (Server Chính bảo trì).");

                var err = await resp.Content.ReadAsStringAsync();
                return ApiResult<T>.Fail($"Lỗi {(int)resp.StatusCode}: {err}");
            }
            catch (TaskCanceledException)
            {
                return ApiResult<T>.Fail("Hết thời gian chờ — kiểm tra kết nối mạng.");
            }
            catch (Exception ex)
            {
                return ApiResult<T>.Fail($"Lỗi mạng: {ex.Message}");
            }
        }

        // ───────────────── Auth ─────────────────

        public async Task<ApiResult<LoginResponse>> LoginAsync(string userName, string password, bool isDriver)
        {
            var req = BuildRequest(HttpMethod.Post, "/api/auth/login",
                new { UserName = userName, Password = password, IsDriver = isDriver });
            var result = await SendAsync<LoginResponse>(req);
            if (result.IsSuccess && result.Data != null && !string.IsNullOrEmpty(result.Data.RefreshToken))
                await SecureStorage.SetAsync("refreshToken", result.Data.RefreshToken);
            return result;
        }

        public Task<ApiResult<object>> RegisterAsync(RegisterRequest body)
        {
            var req = BuildRequest(HttpMethod.Post, "/api/auth/register", body);
            return SendAsync<object>(req);
        }

        public async Task<ApiResult<object>> LogoutAsync()
        {
            try
            {
                var refreshToken = await SecureStorage.GetAsync("refreshToken") ?? "";
                var req = BuildRequest(HttpMethod.Post, "/api/auth/logout",
                    new { RefreshToken = refreshToken });
                var result = await SendAsync<object>(req);
                ClearToken();
                SecureStorage.Remove("refreshToken");
                return result;
            }
            catch
            {
                ClearToken();
                SecureStorage.Remove("refreshToken");
                return ApiResult<object>.Success(null!);
            }
        }

        // ───────────────── Users ─────────────────

        public Task<ApiResult<UserDto>> GetProfileAsync(int userId)
        {
            var req = BuildRequest(HttpMethod.Get, $"/api/users/{userId}");
            return SendAsync<UserDto>(req);
        }

        public Task<ApiResult<object>> UpdateProfileAsync(int userId, UpdateProfileRequest body)
        {
            var req = BuildRequest(HttpMethod.Put, $"/api/users/{userId}", body);
            return SendAsync<object>(req);
        }

        // ───────────────── Trips ─────────────────

        public async Task<ApiResult<BookingResponse>> BookTripAsync(
            int userId, string pickup, string dropoff,
            string vehicleType = "Xe máy", double distanceKm = 0)
        {
            var body = new TripBookingRequest
            {
                UserID          = userId,
                PickupLocation  = pickup,
                DropoffLocation = dropoff,
                Region          = _geo.GetCachedRegion(),
                VehicleType     = vehicleType,
                DistanceKm      = distanceKm
            };

            if (Connectivity.NetworkAccess == NetworkAccess.None)
            {
                var json = JsonSerializer.Serialize(body);
                await _offlineQueue.EnqueueAsync("BookTrip", json);
                return ApiResult<BookingResponse>.ReadOnly(
                    "Không có kết nối mạng. Yêu cầu đặt xe đã được lưu và sẽ gửi khi có mạng.");
            }

            var req = BuildRequest(HttpMethod.Post, "/api/trips/book-trip", body);
            var result = await SendAsync<BookingResponse>(req);

            if (!result.IsSuccess && !result.IsReadOnlyMode)
            {
                var json = JsonSerializer.Serialize(body);
                await _offlineQueue.EnqueueAsync("BookTrip", json);
            }

            return result;
        }

        public Task<ApiResult<List<TripHistoryItem>>> GetTripHistoryAsync(int userId)
        {
            var req = BuildRequest(HttpMethod.Get, $"/api/trips/history/{userId}");
            return SendAsync<List<TripHistoryItem>>(req);
        }

        public Task<ApiResult<InvoiceResponse>> GetInvoiceAsync(int tripId)
        {
            var req = BuildRequest(HttpMethod.Get, $"/api/trips/{tripId}/invoice");
            return SendAsync<InvoiceResponse>(req);
        }

        public Task<ApiResult<object>> SubmitRatingAsync(int tripId, int score, string? comment = null)
        {
            var req = BuildRequest(HttpMethod.Post, $"/api/trips/{tripId}/rating",
                new RatingRequest { Score = score, Comment = comment });
            return SendAsync<object>(req);
        }

        // ───────────────── Driver Trips ─────────────────

        public Task<ApiResult<List<PendingTripItem>>> GetPendingTripsAsync(string region)
        {
            var req = BuildRequest(HttpMethod.Get, $"/api/trips/pending/{region}");
            return SendAsync<List<PendingTripItem>>(req);
        }

        public Task<ApiResult<List<TripHistoryItem>>> GetDriverHistoryAsync()
        {
            var req = BuildRequest(HttpMethod.Get, "/api/trips/driver/history");
            return SendAsync<List<TripHistoryItem>>(req);
        }

        public Task<ApiResult<object>> AcceptTripAsync(int tripId)
        {
            var req = BuildRequest(HttpMethod.Post, $"/api/trips/{tripId}/accept");
            return SendAsync<object>(req);
        }

        public Task<ApiResult<object>> ArriveTripAsync(int tripId)
        {
            var req = BuildRequest(HttpMethod.Post, $"/api/trips/{tripId}/arrive");
            return SendAsync<object>(req);
        }

        public Task<ApiResult<object>> PickupTripAsync(int tripId)
        {
            var req = BuildRequest(HttpMethod.Post, $"/api/trips/{tripId}/pickup");
            return SendAsync<object>(req);
        }

        public Task<ApiResult<object>> CompleteTripAsync(int tripId)
        {
            var req = BuildRequest(HttpMethod.Post, $"/api/trips/{tripId}/complete");
            return SendAsync<object>(req);
        }

        public Task<ApiResult<object>> CancelTripAsync(int tripId)
        {
            var req = BuildRequest(HttpMethod.Post, $"/api/trips/{tripId}/cancel");
            return SendAsync<object>(req);
        }

        // ───────────────── Offline Queue ─────────────────

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
                await ProcessOfflineQueueAsync();
        }

        public Task ProcessOfflineQueueAsync()
        {
            return _offlineQueue.ProcessQueueAsync(async item =>
            {
                if (item.RequestType == "BookTrip")
                {
                    var body = JsonSerializer.Deserialize<TripBookingRequest>(item.PayloadJson);
                    if (body == null) return false;
                    var req = BuildRequest(HttpMethod.Post, "/api/trips/book-trip", body);
                    var result = await SendAsync<BookingResponse>(req);
                    return result.IsSuccess;
                }
                return false;
            });
        }

        // ───────────────── Locations ─────────────────

        public async Task<List<LocationItem>> SearchLocationsAsync(string query)
        {
            try
            {
                var req = BuildRequest(HttpMethod.Get,
                    $"/api/locations/search?q={Uri.EscapeDataString(query)}");
                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return new();
                var items = await resp.Content.ReadFromJsonAsync<List<LocationItem>>();
                return items ?? new();
            }
            catch { return new(); }
        }

        public async Task<List<LocationItem>> GetPopularLocationsAsync()
        {
            try
            {
                var req = BuildRequest(HttpMethod.Get, "/api/locations/popular");
                var resp = await _httpClient.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return new();
                var items = await resp.Content.ReadFromJsonAsync<List<LocationItem>>();
                return items ?? new();
            }
            catch { return new(); }
        }

        // ───────────────── Device Token ─────────────────

        public Task<ApiResult<object>> RegisterDeviceTokenAsync(DeviceTokenRequest body)
        {
            var req = BuildRequest(HttpMethod.Post, "/api/users/device-token", body);
            return SendAsync<object>(req);
        }

        // ───────────────── Health check ─────────────────

        public async Task<bool> CheckAndSetReadOnlyAsync()
        {
            string region = _geo.GetCachedRegion();
            try
            {
                var resp = await _httpClient.GetAsync($"/api/trips/health/{region}");
                if (!resp.IsSuccessStatusCode) return false;
                var body = await resp.Content.ReadFromJsonAsync<HealthResponse>();
                bool isFailover = body?.IsFailover ?? false;
                Preferences.Set("isReadOnly", isFailover);
                return !isFailover;
            }
            catch
            {
                return !Preferences.Get("isReadOnly", false);
            }
        }

        public async Task<string> TestKetNoiAsync(string khuVuc)
        {
            try
            {
                var resp = await _httpClient.GetAsync($"/api/trips/test-connection/{khuVuc}");
                return resp.IsSuccessStatusCode
                    ? await resp.Content.ReadAsStringAsync()
                    : $"Lỗi Server: {resp.StatusCode}";
            }
            catch (Exception ex) { return $"Lỗi mạng: {ex.Message}"; }
        }

        public async Task<string> DatXeAsync(int userId, string diemDon, string diemDen, string khuVuc)
        {
            _geo.SetRegionManually(khuVuc);
            var result = await BookTripAsync(userId, diemDon, diemDen);
            if (result.IsSuccess) return "Đặt xe thành công!";
            if (result.IsReadOnlyMode) return result.ErrorMessage ?? "Server ở chế độ Read-Only.";
            return result.ErrorMessage ?? "Lỗi không xác định.";
        }

        // ───────────────── Pooling ─────────────────

        // Tìm cuốc có thể ghép với cuốc chính
        public Task<ApiResult<List<PoolingCandidateItem>>> GetPoolCandidatesAsync(
            int tripId,
            double mainPickupLat,
            double mainPickupLon,
            double mainDropoffLat,
            double mainDropoffLon)
        {
            string url = $"/api/trips/pool-candidates/{tripId}" +
                $"?mainPickupLat={mainPickupLat}" +
                $"&mainPickupLon={mainPickupLon}" +
                $"&mainDropoffLat={mainDropoffLat}" +
                $"&mainDropoffLon={mainDropoffLon}";

            var req = BuildRequest(HttpMethod.Get, url);
            return SendAsync<List<PoolingCandidateItem>>(req);
        }

        // Ghép 2 cuốc lại
        public Task<ApiResult<object>> PoolTripsAsync(int mainTripId, int secondaryTripId)
        {
            var body = new { MainTripID = mainTripId, SecondaryTripID = secondaryTripId };
            var req = BuildRequest(HttpMethod.Post, "/api/trips/pool", body);
            return SendAsync<object>(req);
        }

        // Lấy thông tin cuốc ghép
        public Task<ApiResult<PooledTripInfo>> GetPooledTripInfoAsync(int tripId)
        {
            var req = BuildRequest(HttpMethod.Get, $"/api/trips/pooled/{tripId}");
            return SendAsync<PooledTripInfo>(req);
        }
    }
}
