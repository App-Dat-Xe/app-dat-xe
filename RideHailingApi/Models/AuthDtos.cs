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
        public string Password { get; set; } = string.Empty;
        public string RegisteredRegion { get; set; } = "South";
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
}
