using System.Text.Json.Serialization;

namespace RideHailingApp.Services
{
    public class LoginRequest
    {
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class RegisterRequest
    {
        public string UserName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Password { get; set; } = "";
        public string RegisteredRegion { get; set; } = "South";
    }

    public class UserDto
    {
        [JsonPropertyName("id")]
        public int UserID { get; set; }
        public string UserName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string RegisteredRegion { get; set; } = "";
    }

    public class LoginResponse
    {
        public string AccessToken  { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public int    ExpiresIn    { get; set; }
        public string TokenType    { get; set; } = "Bearer";
        public UserDto User        { get; set; } = new();
    }

    public class RefreshResponse
    {
        public string AccessToken  { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public int    ExpiresIn    { get; set; }
    }

    public class RatingRequest
    {
        public int     Score   { get; set; }
        public string? Comment { get; set; }
    }

    public class InvoiceResponse
    {
        public int     TripId        { get; set; }
        public string  Pickup        { get; set; } = "";
        public string  Dropoff       { get; set; } = "";
        public string  VehicleType   { get; set; } = "";
        public double? DistanceKm    { get; set; }
        public decimal? Fare         { get; set; }
        public string  Status        { get; set; } = "";
        public string? CreatedAt     { get; set; }
        public int?    RatingScore   { get; set; }
        public string? RatingComment { get; set; }
        public string DisplayFare => Fare.HasValue ? $"{Fare.Value:#,##0}đ" : "—";
    }

    public class BookingResponse
    {
        public int TripId { get; set; }
        public string Message { get; set; } = "";
    }

    public class UpdateProfileRequest
    {
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string? NewPassword { get; set; }
    }

    public class TripBookingRequest
    {
        public int UserID { get; set; }
        public string PickupLocation { get; set; } = "";
        public string DropoffLocation { get; set; } = "";
        public string Region { get; set; } = "";
        public string VehicleType { get; set; } = "Xe máy";
        public double DistanceKm { get; set; }
    }

    public class TripHistoryItem
    {
        public int TripID { get; set; }
        public int UserID { get; set; }
        public int? DriverID { get; set; }
        public string PickupLocation { get; set; } = "";
        public string DropoffLocation { get; set; } = "";
        public string Region { get; set; } = "";
        public string Status { get; set; } = "";
        public string VehicleType { get; set; } = "";
        public decimal? Fare { get; set; }
        public DateTime? CreatedAt { get; set; }

        public string DisplayFare => Fare.HasValue ? $"{Fare.Value:#,##0}đ" : "—";
    }

    // Phản hồi từ GET /api/trips/health/{region}
    public class HealthResponse
    {
        public string Region   { get; set; } = "";
        public bool PrimaryOk  { get; set; }
        public bool ReplicaOk  { get; set; }
        public bool IsFailover { get; set; }
        public string Message  { get; set; } = "";
    }

    // Phản hồi từ GET /health/db/{region}
    public class DbHealthResponse
    {
        public string Region         { get; set; } = "";
        public bool IsDegradedMode   { get; set; }
        public bool IsFailover       { get; set; }
        public string CurrentTarget  { get; set; } = "";
        public bool PrimaryHealthy   { get; set; }
        public bool BackupHealthy    { get; set; }
    }

    // Phản hồi từ GET /health/maintenance
    public class MaintenanceResponse
    {
        public bool IsActive   { get; set; }
        public string? Message { get; set; }
    }

    public class PendingTripItem
    {
        public int TripID { get; set; }
        public int UserID { get; set; }
        public string PickupLocation { get; set; } = "";
        public string DropoffLocation { get; set; } = "";
        public string Region { get; set; } = "";
        public string VehicleType { get; set; } = "";
        public decimal? EstimatedFare { get; set; }
        public DateTime? CreatedAt { get; set; }

        public string DisplayVehicle => string.IsNullOrEmpty(VehicleType) ? "Xe máy" : VehicleType;
        public string DisplayFare    => EstimatedFare.HasValue ? $"{EstimatedFare.Value:#,##0}đ" : "—";
    }

    // Generic kết quả gọi API — phân biệt rõ các trạng thái cho client xử lý UI
    public class ApiResult<T>
    {
        public bool IsSuccess { get; set; }
        public bool IsReadOnlyMode { get; set; }   // true khi server trả 503 (Primary sập)
        public T? Data { get; set; }
        public string? ErrorMessage { get; set; }

        public static ApiResult<T> Success(T data) => new() { IsSuccess = true, Data = data };
        public static ApiResult<T> ReadOnly(string msg) => new() { IsReadOnlyMode = true, ErrorMessage = msg };
        public static ApiResult<T> Fail(string msg) => new() { ErrorMessage = msg };
    }

    // ===== Pooling Models =====
    public class PoolingCandidateItem
    {
        public int TripID { get; set; }
        public int UserID { get; set; }
        public string PickupLocation { get; set; } = "";
        public string DropoffLocation { get; set; } = "";
        public double PickupDistance { get; set; }
        public double DropoffDistance { get; set; }
        public int MinutesOld { get; set; }
        public DateTime? CreatedAt { get; set; }

        // Helper properties for UI
        public string DisplayDistance => $"{Math.Round(Math.Max(PickupDistance, DropoffDistance), 2)} km";
        public string DisplayPickup => PickupLocation?.Length > 30 
            ? PickupLocation[..27] + "..." 
            : PickupLocation ?? "";
        public string DisplayDropoff => DropoffLocation?.Length > 30 
            ? DropoffLocation[..27] + "..." 
            : DropoffLocation ?? "";
    }

    public class PooledTripInfo
    {
        public int MainTripID { get; set; }
        public int SecondaryTripID { get; set; }
        public int? MainUserID { get; set; }
        public int? SecondaryUserID { get; set; }
        public string MainPickup { get; set; } = "";
        public string MainDropoff { get; set; } = "";
        public string SecondaryPickup { get; set; } = "";
        public string SecondaryDropoff { get; set; } = "";
        public int CurrentPassengers { get; set; } = 2;
        public DateTime? PooledAt { get; set; }
    }
}
