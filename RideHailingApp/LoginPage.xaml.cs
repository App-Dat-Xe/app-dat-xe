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
        var userName = UserNameEntry.Text?.Trim();
        var password = PasswordEntry.Text;

        if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
        {
            ShowError("Vui lòng nhập tên đăng nhập và mật khẩu.");
            return;
        }

        LoginButton.IsEnabled = false;
        LoginButton.Text      = "Đang xác thực...";
        ErrorLabel.IsVisible  = false;

        bool isDriver = DriverModeSwitch.IsToggled;
        var result = await _apiService.LoginAsync(userName, password, isDriver);

        if (result.IsSuccess && result.Data != null)
        {
            _apiService.SetToken(result.Data.AccessToken);
            Preferences.Set("isLoggedIn",  true);
            Preferences.Set("userID",      result.Data.User.UserID);
            Preferences.Set("userEmail",   result.Data.User.UserName);
            Preferences.Set("userName",    result.Data.User.FullName);
            Preferences.Set("userRegion",  result.Data.User.RegisteredRegion);

            await _apiService.CheckAndSetReadOnlyAsync();

            isDriver = DriverModeSwitch.IsToggled;
            Preferences.Set("isDriver", isDriver);
            if (isDriver)
                Application.Current!.MainPage = new DriverShell();
            else
                Application.Current!.MainPage = new AppShell();
        }
        else if (result.IsReadOnlyMode)
        {
            Preferences.Set("isReadOnly", true);
            ReadOnlyBanner.IsVisible = true;
            isDriver = DriverModeSwitch.IsToggled;
            Preferences.Set("isDriver", isDriver);
            Application.Current!.MainPage = isDriver ? new DriverShell() : new AppShell();
        }
        else
        {
            var msg = result.ErrorMessage ?? "Tên đăng nhập hoặc mật khẩu không đúng.";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("message", out var m))
                    msg = m.GetString() ?? msg;
                else if (doc.RootElement.TryGetProperty("error", out var e2))
                    msg = e2.GetString() ?? msg;
            }
            catch { }
            ShowError(msg);
        }
        LoginButton.IsEnabled = true;
        LoginButton.Text      = "Đăng nhập";
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
