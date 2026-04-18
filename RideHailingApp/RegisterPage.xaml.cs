using RideHailingApp.Services;

namespace RideHailingApp;

public partial class RegisterPage : ContentPage
{
    private readonly ApiService _apiService;

    public RegisterPage()
    {
        InitializeComponent();
        // Lấy ApiService từ trung tâm
        _apiService = MauiProgram.Services.GetRequiredService<ApiService>();
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        var fullName = FullNameEntry.Text?.Trim();
        var email = EmailEntry.Text?.Trim();
        var phone = PhoneEntry.Text?.Trim();
        var password = PasswordEntry.Text;
        var confirm = ConfirmPasswordEntry.Text;

        // Validate
        if (string.IsNullOrEmpty(fullName) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(password))
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

        // Xác định vùng từ Picker (0 = South, 1 = North)
        string region = RegionPicker.SelectedIndex == 1 ? "North" : "South";

        // GÓI DỮ LIỆU ĐỂ GỬI LÊN API
        var request = new RegisterRequest
        {
            UserName = email, // Dùng email làm UserName đăng nhập
            FullName = fullName,
            Phone = phone,
            Password = password,
            RegisteredRegion = region
        };

        // GỌI API ĐĂNG KÝ
        var result = await _apiService.RegisterAsync(request);

        if (result.IsSuccess)
        {
            await DisplayAlert("Thành công!", $"Tài khoản đã được tạo tại Server {region}.\nVui lòng đăng nhập.", "OK");
            Application.Current.MainPage = new NavigationPage(new LoginPage());
        }
        else if (result.IsReadOnlyMode)
        {
            await DisplayAlert("Bảo trì", "Server chính đang sập, không thể tạo tài khoản mới lúc này. Vui lòng thử lại sau.", "OK");
        }
        else
        {
            ShowError(result.ErrorMessage ?? "Đăng ký thất bại. Email hoặc SĐT có thể đã tồn tại.");
        }
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