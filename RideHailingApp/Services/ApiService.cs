using System.Net;
using System.Net.Http.Json;

namespace RideHailingApp.Services
{
    // Lớp giao tiếp duy nhất với backend.
    // Tự động gắn header X-Region (đọc từ GeoLocatorService) vào mọi request.
    // Phân biệt rõ 3 trạng thái: thành công / read-only (503) / lỗi.
    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private readonly GeoLocatorService _geo;

        public ApiService(GeoLocatorService geo)
        {
            _geo = geo;

            // Bypass SSL self-signed khi test localhost
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            string baseUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:5108"    // Android emulator
                : "https://localhost:7285";  // Windows desktop
            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        // ───────────────── Helpers ─────────────────

        private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body = null)
        {
            var req = new HttpRequestMessage(method, path);
            req.Headers.Add("X-Region", _geo.GetCachedRegion());
            if (body != null)
                req.Content = JsonContent.Create(body);
            return req;
        }

        private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage req)
        {
            try
            {
                var resp = await _httpClient.SendAsync(req);

                if (resp.IsSuccessStatusCode)
                {
                    var data = await resp.Content.ReadFromJsonAsync<T>();
                    return ApiResult<T>.Success(data!);
                }

                if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    return ApiResult<T>.ReadOnly("Hệ thống đang ở chế độ Read-Only (Server Chính bảo trì).");
                }

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

        public Task<ApiResult<LoginResponse>> LoginAsync(string userName, string password)
        {
            var req = BuildRequest(HttpMethod.Post, "/api/auth/login",
                new LoginRequest { UserName = userName, Password = password });
            return SendAsync<LoginResponse>(req);
        }

        public Task<ApiResult<object>> RegisterAsync(RegisterRequest body)
        {
            var req = BuildRequest(HttpMethod.Post, "/api/auth/register", body);
            return SendAsync<object>(req);
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

        public Task<ApiResult<object>> BookTripAsync(int userId, string pickup, string dropoff)
        {
            var body = new TripBookingRequest
            {
                UserID        = userId,
                PickupLocation  = pickup,
                DropoffLocation = dropoff,
                Region        = _geo.GetCachedRegion()
            };
            var req = BuildRequest(HttpMethod.Post, "/api/trips/book-trip", body);
            return SendAsync<object>(req);
        }

        public Task<ApiResult<List<TripHistoryItem>>> GetTripHistoryAsync(int userId)
        {
            var req = BuildRequest(HttpMethod.Get, $"/api/trips/history/{userId}");
            return SendAsync<List<TripHistoryItem>>(req);
        }

        // ───────────────── Health check ─────────────────

        // Kiểm tra Primary còn sống không → tự động cập nhật Preferences["isReadOnly"].
        // Gọi ngay sau khi đăng nhập để app biết trạng thái ngay từ đầu.
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
                return !isFailover; // true = Primary sống bình thường
            }
            catch
            {
                // Không kết nối được API → giữ nguyên flag hiện tại
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
            catch (Exception ex)
            {
                return $"Lỗi mạng: {ex.Message}";
            }
        }

        // Giữ method cũ để không phá MainPage.OnFindDriverClicked
        public async Task<string> DatXeAsync(int userId, string diemDon, string diemDen, string khuVuc)
        {
            _geo.SetRegionManually(khuVuc);
            var result = await BookTripAsync(userId, diemDon, diemDen);
            if (result.IsSuccess) return "Đặt xe thành công!";
            if (result.IsReadOnlyMode) return result.ErrorMessage ?? "Server ở chế độ Read-Only.";
            return result.ErrorMessage ?? "Lỗi không xác định.";
        }
    }
}
