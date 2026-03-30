namespace RideHailingApp;

public partial class LoginPage : ContentPage
{
    // ── Mock: tài khoản giả để test ──
    private const string MOCK_EMAIL    = "test@gmail.com";
    private const string MOCK_PASSWORD = "123456";
    private const string MOCK_NAME     = "Nguyễn Văn A";

    public LoginPage()
    {
        InitializeComponent();
    }

    // Khi trang hiện ra: giả lập phát hiện vùng + server
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await SimulateRegionDetectionAsync();
    }

    // Giả lập xác định vị trí và kết nối server (không cần GPS thật, không cần DB)
    private async Task SimulateRegionDetectionAsync()
    {
        RegionLabel.Text = "Đang xác định vị trí...";
        ServerStatusDot.TextColor = Color.FromArgb("#FFC107");
        ServerBanner.IsVisible = true;
        ServerBannerText.Text = "Đang xác định vị trí GPS...";
        ReadOnlyBanner.IsVisible = false;

        await Task.Delay(1200); // giả lập thời gian lấy GPS

        // Đọc cờ giả lập failover (để demo cho thầy)
        bool simulateFailover = Preferences.Get("simulateFailover", false);

        if (simulateFailover)
        {
            // ⚠️ Giả lập: Primary sập → dùng Replica
            Preferences.Set("isReadOnly", true);
            RegionLabel.Text = "Server Miền Nam (Dự phòng)";
            ServerBannerText.Text = "⚠ Dùng server dự phòng (Replica HCM)";
            ServerStatusDot.TextColor = Color.FromArgb("#FFC107");
            ReadOnlyBanner.IsVisible = true;
        }
        else
        {
            // ✅ Bình thường: Primary hoạt động
            Preferences.Set("isReadOnly", false);
            RegionLabel.Text = "Server Miền Nam (TP.HCM)";
            ServerBannerText.Text = "✓ Đã kết nối Server Miền Nam";
            ServerStatusDot.TextColor = Color.FromArgb("#00C853");
            ReadOnlyBanner.IsVisible = false;
        }
    }

    // Xử lý nút Đăng nhập
    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var email    = EmailEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowError("Vui lòng nhập email và mật khẩu.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Text = "Đang đăng nhập...";
        ErrorLabel.IsVisible = false;

        await Task.Delay(800); // giả lập gọi server

        if (email == MOCK_EMAIL && password == MOCK_PASSWORD)
        {
            Preferences.Set("isLoggedIn", true);
            Preferences.Set("userEmail", MOCK_EMAIL);
            Preferences.Set("userName",  MOCK_NAME);
            Application.Current.MainPage = new AppShell();
        }
        else
        {
            ShowError("Email hoặc mật khẩu không đúng.\nThử: test@gmail.com / 123456");
            LoginButton.IsEnabled = true;
            LoginButton.Text = "Đang đăng nhập...";
        }
    }

    private void OnGoToRegisterClicked(object sender, EventArgs e)
    {
        Application.Current.MainPage = new NavigationPage(new RegisterPage());
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
