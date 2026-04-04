using System.Net.Http.Json;

namespace RideHailingApp.Services // Thay bằng namespace chuẩn của app bạn
{
    public class ApiService
    {
        private readonly HttpClient _httpClient;

        public ApiService()
        {
            // 1. Vượt rào bảo mật SSL cục bộ (Bắt buộc khi test Localhost)
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            _httpClient = new HttpClient(handler);
            string baseUrl = DeviceInfo.Platform == DevicePlatform.Android
                             ? "https://10.0.2.2:7285"   // Cửa cho Android
                             : "https://localhost:7285"; // Cửa cho Windows

            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        // Hàm 1: Test đâm xuyên Database
        public async Task<string> TestKetNoiAsync(string khuVuc)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/trips/test-connection/{khuVuc}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return $"Lỗi Server: {response.StatusCode}";
            }
            catch (Exception ex)
            {
                return $"Lỗi mạng: {ex.Message}";
            }
        }
        public async Task<string> DatXeAsync(int userId, string diemDon, string diemDen, string khuVuc)
        {
            try
            {
                var requestData = new
                {
                    UserID = userId,
                    PickupLocation = diemDon,
                    DropoffLocation = diemDen,
                    Region = khuVuc
                };

                var response = await _httpClient.PostAsJsonAsync("/api/trips/book-trip", requestData);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return $"Sập toàn tập: {ex.Message}";
            }
        }
    }
}