using multilingualAudioTravelApp.Services;

namespace multilingualAudioTravelApp;

public partial class EditProfilePage : ContentPage
{
    private readonly DatabaseService _dbService = new DatabaseService();
    private string _currentEmail;

    public EditProfilePage()
    {
        InitializeComponent();
        LoadCurrentInfo();
    }

    private void LoadCurrentInfo()
    {
        _currentEmail = Preferences.Get("userEmail", "");
        NameEntry.Text = Preferences.Get("userName", "");
        EmailEntry.Text = _currentEmail;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var newName = NameEntry.Text?.Trim();
        var newEmail = EmailEntry.Text?.Trim();
        var newPassword = NewPasswordEntry.Text;
        var confirm = ConfirmPasswordEntry.Text;

        // Kiểm tra tên và email
        if (string.IsNullOrEmpty(newName) || string.IsNullOrEmpty(newEmail))
        {
            ShowError("Họ tên và email không được để trống.");
            return;
        }

        // Nếu có nhập mật khẩu mới thì kiểm tra
        if (!string.IsNullOrEmpty(newPassword))
        {
            if (newPassword.Length < 6)
            {
                ShowError("Mật khẩu mới phải có ít nhất 6 ký tự.");
                return;
            }

            if (newPassword != confirm)
            {
                ShowError("Mật khẩu xác nhận không khớp.");
                return;
            }
        }

        ErrorLabel.IsVisible = false;

        // Lưu tất cả trong 1 lần
        var success = await _dbService.UpdateProfileAsync(
            _currentEmail,
            newName,
            newEmail,
            string.IsNullOrEmpty(newPassword) ? null : newPassword
        );

        if (success)
        {
            _currentEmail = newEmail;
            await DisplayAlert("Thành công", "Thông tin đã được cập nhật!", "OK");
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            ShowError("Email này đã được sử dụng bởi tài khoản khác.");
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}