using RideHailingApp.Services;

namespace RideHailingApp;

public partial class EditProfilePage : ContentPage
{
    private readonly ApiService _api;
    private readonly SessionService _session;

    public EditProfilePage()
    {
        InitializeComponent();
        // Lấy services từ DI container
        _api = MauiProgram.Services.GetRequiredService<ApiService>();
        _session = MauiProgram.Services.GetRequiredService<SessionService>();
        LoadCurrentInfo();
    }

    private void LoadCurrentInfo()
    {
        NameEntry.Text = _session.FullName;
        EmailEntry.Text = _session.UserName;
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var newName = NameEntry.Text?.Trim();
        var newEmail = EmailEntry.Text?.Trim();
        var newPassword = NewPasswordEntry.Text;
        var confirm = ConfirmPasswordEntry.Text;

        if (string.IsNullOrEmpty(newName) || string.IsNullOrEmpty(newEmail))
        {
            ShowError("Họ tên và email không được để trống.");
            return;
        }

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

        var result = await _api.UpdateProfileAsync(_session.UserID, new UpdateProfileRequest
        {
            FullName = newName!,
            Phone = _session.Phone,
            NewPassword = string.IsNullOrEmpty(newPassword) ? null : newPassword
        });

        if (result.IsReadOnlyMode)
        {
            ShowError(result.ErrorMessage ?? "Hệ thống đang Read-Only, không thể cập nhật.");
            return;
        }

        if (result.IsSuccess)
        {
            _session.Save(_session.UserID, _session.UserName, newName!, _session.Phone, _session.RegisteredRegion);
            await DisplayAlert("Thành công", "Thông tin đã được cập nhật!", "OK");
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            ShowError(result.ErrorMessage ?? "Cập nhật thất bại.");
        }
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
