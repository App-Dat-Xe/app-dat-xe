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
        public int UserID { get; set; }
        public string UserName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string RegisteredRegion { get; set; } = "";
    }

    public class LoginResponse
    {
        public string Region { get; set; } = "";
        public UserDto User { get; set; } = new();
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
        public DateTime? CreatedAt { get; set; }
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
}
