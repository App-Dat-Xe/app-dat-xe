namespace RideHailingApi.Models
{
    public class LoginRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsDriver { get; set; }
    }

    public class RegisterRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RegisteredRegion { get; set; } = "South";
    }

    public class ForgotPasswordRequest
    {
        public string UserNameOrEmail { get; set; } = string.Empty;
        public string Region { get; set; } = "South";
    }

    public class ResetPasswordRequest
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class AdminLoginRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    // Generic paginated response wrapper
    public class PagedResult<T>
    {
        public List<T> Data { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);
    }

    public class UserDto
    {
        public int UserID { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string RegisteredRegion { get; set; } = string.Empty;
    }

    public class UpdateProfileRequest
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? NewPassword { get; set; }
    }
    public class TripCancelRequest
    {
        public string? Reason { get; set; }
    }

    // Device token request for FCM registration
    public class DeviceTokenRequest
    {
        public int UserId { get; set; }
        public string DeviceToken { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty; // "android" | "ios"
    }

    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class RatingRequest
    {
        public int Score { get; set; }       // 1–5
        public string? Comment { get; set; }
    }

    public class LockRequest
    {
        public bool IsLocked { get; set; }
    }

    public class MaintenanceRequest
    {
        public string? Message { get; set; }
        public DateTime? EstimatedEndTime { get; set; }
    }
}
