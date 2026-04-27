namespace RideHailingApp.Services
{
    // Lưu thông tin user sau khi login. Dùng Preferences (đơn giản, đủ cho demo).
    // Production: nên thay bằng SecureStorage để bảo vệ token.
    public class SessionService
    {
        public bool IsLoggedIn => Preferences.Get("isLoggedIn", false);
        public int UserID => Preferences.Get("userId", 0);
        public string UserName => Preferences.Get("userName", "");
        public string FullName => Preferences.Get("fullName", "");
        public string Phone => Preferences.Get("userPhone", "");
        public string RegisteredRegion => Preferences.Get("registeredRegion", "South");

        public void Save(int userId, string userName, string fullName, string phone, string registeredRegion)
        {
            Preferences.Set("isLoggedIn", true);
            Preferences.Set("userId", userId);
            Preferences.Set("userName", userName);
            Preferences.Set("fullName", fullName);
            Preferences.Set("userPhone", phone);
            Preferences.Set("registeredRegion", registeredRegion);
        }
        public void Clear()
        {
            Preferences.Remove("isLoggedIn");
            Preferences.Remove("userId");
            Preferences.Remove("userName");
            Preferences.Remove("fullName");
            Preferences.Remove("userPhone");
            Preferences.Remove("registeredRegion");
        }
    }
}
