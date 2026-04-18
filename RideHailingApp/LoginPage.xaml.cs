using RideHailingApp.Services;

namespace RideHailingApp;

public partial class LoginPage : ContentPage
{
    private readonly ApiService _apiService;

    public LoginPage()
    {
        InitializeComponent();
        _apiService = MauiProgram.Services.GetRequiredService<ApiService>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await SetupUIStatusAsync();
    }

    // Chỉ dùng để khởi tạo giao diện lúc mới vào, không giả lập nữa
    private async Task SetupUIStatusAsync()
    {
        RegionLabel.Text = "Sẵn sàng kết nối...";
        ServerStatusDot.TextColor = Color.FromArgb("#00C853");
        ServerBanner.IsVisible = false;
        ReadOnlyBanner.IsVisible = false;
        await Task.CompletedTask;
    }

    // Xử lý nút Đăng nhập gọi API thật
    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowError("Vui lòng nhập email và mật khẩu.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Text = "Đang xác thực...";
        ErrorLabel.IsVisible = false;

        // GỌI API ĐĂNG NHẬP XUỐNG SQL SERVER
        var result = await _apiService.LoginAsync(email, password);

        if (result.IsSuccess && result.Data != null)
        {
            // LƯU THÔNG TIN THẬT TỪ DATABASE VÀO ĐIỆN THOẠI
            Preferences.Set("isLoggedIn", true);
            Preferences.Set("userID", result.Data.User.UserID);
            Preferences.Set("userEmail", result.Data.User.UserName);
            Preferences.Set("userName", result.Data.User.FullName);
            Preferences.Set("userRegion", result.Data.User.RegisteredRegion);
            Preferences.Set("isReadOnly", false);

            Application.Current.MainPage = new AppShell();
        }
        else if (result.IsReadOnlyMode)
        {
            ShowError("Hệ thống đang bảo trì. Chức năng đăng nhập tạm khóa.");
            LoginButton.IsEnabled = true;
            LoginButton.Text = "Đăng nhập";
        }
        else
        {
            ShowError(result.ErrorMessage ?? "Email hoặc mật khẩu không đúng.");
            LoginButton.IsEnabled = true;
            LoginButton.Text = "Đăng nhập";
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