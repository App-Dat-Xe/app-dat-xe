namespace RideHailingApp;

public partial class RegisterPage : ContentPage
{
    public RegisterPage()
    {
        InitializeComponent();
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var fullName = FullNameEntry.Text?.Trim();
        var email    = EmailEntry.Text?.Trim();
        var phone    = PhoneEntry.Text?.Trim();
        var password = PasswordEntry.Text;
        var confirm  = ConfirmPasswordEntry.Text;

        // Validate
        if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowError("Vui lòng điền đầy đủ thông tin.");
            return;
        }

        if (password != confirm)
        {
            ShowError("Mật khẩu xác nhận không khớp.");
            return;
        }

        if (password.Length < 6)
        {
            ShowError("Mật khẩu phải có ít nhất 6 ký tự.");
            return;
        }

        ErrorLabel.IsVisible = false;

        await Task.Delay(800); // giả lập gọi server

        // Xác định vùng từ Picker
        string region = RegionPicker.SelectedIndex == 1 ? "north" : "south";
        string regionName = region == "north" ? "Miền Bắc" : "Miền Nam";

        // Lưu thông tin giả lập
        Preferences.Set("userName",  fullName);
        Preferences.Set("userEmail", email);
        Preferences.Set("userRegion", region);

        await DisplayAlert(
            "Đăng ký thành công!",
            $"Tài khoản đã được tạo.\nVùng kết nối: {regionName}\nVui lòng đăng nhập.",
            "OK");

        Application.Current.MainPage = new NavigationPage(new LoginPage());
    }

    private void OnGoToLoginClicked(object sender, EventArgs e)
    {
        Application.Current.MainPage = new NavigationPage(new LoginPage());
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
