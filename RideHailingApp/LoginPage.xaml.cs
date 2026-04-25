using RideHailingApp.Services;

namespace RideHailingApp;

public partial class LoginPage : ContentPage
{
    private readonly ApiService _apiService;
    private readonly GeoLocatorService _geo;

    public LoginPage()
    {
        InitializeComponent();
        _apiService = MauiProgram.Services.GetRequiredService<ApiService>();
        _geo        = MauiProgram.Services.GetRequiredService<GeoLocatorService>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await DetectRegionAsync();
    }

    // Tự động phát hiện region từ GPS, cập nhật label
    private async Task DetectRegionAsync()
    {
        RegionLabel.Text = "Đang xác định vị trí...";
        ServerStatusDot.TextColor = Color.FromArgb("#FFC107");
        ServerBanner.IsVisible = false;
        ReadOnlyBanner.IsVisible = false;

        string region = await _geo.GetRegionAsync();
        UpdateRegionUI(region);
    }

    // Gọi khi user chọn tỉnh/thành phố từ Picker
    private void OnRegionPickerChanged(object sender, EventArgs e)
    {
        if (RegionPicker.SelectedIndex < 0) return;
        string selected = RegionPicker.Items[RegionPicker.SelectedIndex];
        string region = selected.Contains("Miền Bắc") ? "North" : "South";
        _geo.SetRegionManually(region);
        UpdateRegionUI(region);
    }

    private void UpdateRegionUI(string region)
    {
        string display = region == "North" ? "Miền Bắc (Hà Nội)" : "Miền Nam (TP.HCM)";
        RegionLabel.Text = $"● Server {display}";
        ServerStatusDot.TextColor = Color.FromArgb("#00C853");
    }

    // Xử lý nút Đăng nhập gọi API thật
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
        LoginButton.Text      = "Đang xác thực...";
        ErrorLabel.IsVisible  = false;

        var result = await _apiService.LoginAsync(email, password);

        if (result.IsSuccess && result.Data != null)
        {
            _apiService.SetToken(result.Data.AccessToken);
            Preferences.Set("isLoggedIn",  true);
            Preferences.Set("userID",      result.Data.User.UserID);
            Preferences.Set("userEmail",   result.Data.User.UserName);
            Preferences.Set("userName",    result.Data.User.FullName);
            Preferences.Set("userRegion",  result.Data.User.RegisteredRegion);

            // Health-check: tự động phát hiện Primary còn sống không → set isReadOnly
            await _apiService.CheckAndSetReadOnlyAsync();

            bool isDriver = DriverModeSwitch.IsToggled;
            Preferences.Set("isDriver", isDriver);
            if (isDriver)
                Application.Current!.MainPage = new DriverShell();
            else
                Application.Current!.MainPage = new AppShell();
        }
        else if (result.IsReadOnlyMode)
        {
            // Primary sập ngay cả login — vào app chế độ Read-Only
            Preferences.Set("isReadOnly", true);
            ReadOnlyBanner.IsVisible = true;
            bool isDriver = DriverModeSwitch.IsToggled;
            Preferences.Set("isDriver", isDriver);
            Application.Current!.MainPage = isDriver ? new DriverShell() : new AppShell();
        }
        else
        {
            ShowError(result.ErrorMessage ?? "Email hoặc mật khẩu không đúng.");
            LoginButton.IsEnabled = true;
            LoginButton.Text      = "Đăng nhập";
        }
    }

    private void OnGoToRegisterClicked(object sender, EventArgs e)
    {
        Application.Current.MainPage = new NavigationPage(new RegisterPage());
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text      = message;
        ErrorLabel.IsVisible = true;
    }
}
