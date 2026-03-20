using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RideHailingApp;

public partial class ProfilePage : ContentPage
{
    // ── Giữ 1 instance duy nhất, không tạo lại mỗi lần OnAppearing ──
    private readonly ProfileViewModel _viewModel;

    public ProfilePage()
    {
        InitializeComponent();
        _viewModel = new ProfileViewModel();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Đồng bộ trạng thái dark mode thực tế vào switch
        // (phòng trường hợp theme bị đổi từ bên ngoài)
        _viewModel.SyncDarkModeFromSystem();

        // Cập nhật banner + badge trực tiếp
        bool isReadOnly = Preferences.Get("isReadOnly", false);
        ReadOnlyBanner.IsVisible = isReadOnly;

        string regionName = Preferences.Get("regionName", "Miền Nam");
        ServerBadge.Text = isReadOnly ? "⚠ Replica (Dự phòng)" : $"● Server {regionName}";
        ServerBadge.TextColor = isReadOnly
            ? Microsoft.Maui.Graphics.Color.FromArgb("#FFC107")
            : Microsoft.Maui.Graphics.Color.FromArgb("#00C853");

        // Cập nhật lại menu failover (icon có thể thay đổi từ tab khác)
        _viewModel.RefreshMenus();
    }

    private async void OnMenuSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ProfileMenuItem menu)
            return;

        ((CollectionView)sender).SelectedItem = null;

        switch (menu.Key)
        {
            case "history":
                await Shell.Current.GoToAsync(nameof(TripHistoryPage));
                break;

            case "toggle_failover":
                bool current = Preferences.Get("simulateFailover", false);
                bool newVal = !current;
                Preferences.Set("simulateFailover", newVal);
                Preferences.Set("isReadOnly", newVal);

                string msg = newVal
                    ? "✅ Đã BẬT giả lập failover.\nApp chuyển sang chế độ Read-Only (Replica).\n\nQuay lại tab Đặt xe để thấy hiệu ứng."
                    : "✅ Đã TẮT giả lập failover.\nApp trở lại hoạt động bình thường (Primary).";

                await DisplayAlert("Demo Failover", msg, "OK");

                // Chỉ refresh menu, KHÔNG tạo ViewModel mới (sẽ reset switch)
                _viewModel.RefreshMenus();
                ReadOnlyBanner.IsVisible = newVal;
                ServerBadge.Text = newVal ? "⚠ Replica (Dự phòng)" : "● Server Miền Nam";
                ServerBadge.TextColor = newVal
                    ? Microsoft.Maui.Graphics.Color.FromArgb("#FFC107")
                    : Microsoft.Maui.Graphics.Color.FromArgb("#00C853");
                break;

            case "edit":
                await DisplayAlert("Chỉnh sửa thông tin", "Chức năng đang phát triển.", "OK");
                break;

            case "feedback":
                await DisplayAlert("Đánh giá & Góp ý", "Chức năng đang phát triển.", "OK");
                break;

            default:
                await DisplayAlert(menu.Title, "Chức năng đang phát triển.", "OK");
                break;
        }
    }

    private async void OnLogoutClicked(object sender, EventArgs e)
    {
        bool confirm = await DisplayAlert("Đăng xuất",
            "Bạn có chắc muốn đăng xuất không?",
            "Đăng xuất", "Hủy");

        if (!confirm) return;

        Preferences.Remove("isLoggedIn");
        Preferences.Remove("isReadOnly");
        Preferences.Remove("simulateFailover");

        Application.Current.MainPage = new NavigationPage(new LoginPage());
    }
}

// ═════════════════════════════════════════════
// ViewModel — implement INotifyPropertyChanged
// để Switch dark mode hoạt động được
// ═════════════════════════════════════════════
public class ProfileViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Thông tin người dùng ──
    public string UserName { get; set; }
    public string UserEmail { get; set; }
    public string TripCount { get; set; } = "12";
    public string TotalSpent { get; set; } = "480K";
    public string UserRating { get; set; } = "4.9 ⭐";

    public string UserInitial => string.IsNullOrEmpty(UserName) ? "?" :
        UserName.Trim().Split(' ').Last().ToUpper()[0].ToString();

    // ── Dark Mode — notify để Switch phản hồi ngay ──
    private bool _isDarkMode;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode == value) return;
            _isDarkMode = value;

            // Áp dụng theme ngay lập tức
            Application.Current.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;

            // Lưu vào Preferences để nhớ qua các lần mở app
            Preferences.Set("isDarkMode", value);

            OnPropertyChanged();
        }
    }

    // ── Menu items ──
    public ObservableCollection<ProfileMenuItem> ProfileMenus { get; set; }

    public ProfileViewModel()
    {
        UserName = Preferences.Get("userName", "Nguyễn Văn A");
        UserEmail = Preferences.Get("userEmail", "test@gmail.com");

        // Đọc trạng thái dark mode đã lưu từ lần trước
        _isDarkMode = Preferences.Get("isDarkMode", false);

        ProfileMenus = new ObservableCollection<ProfileMenuItem>();
        RefreshMenus();
    }

    /// <summary>
    /// Gọi khi quay lại trang — đồng bộ trạng thái switch với theme thực tế.
    /// Không đổi theme, chỉ cập nhật giá trị binding.
    /// </summary>
    public void SyncDarkModeFromSystem()
    {
        bool systemDark = Application.Current.UserAppTheme == AppTheme.Dark;
        if (_isDarkMode != systemDark)
        {
            _isDarkMode = systemDark;
            OnPropertyChanged(nameof(IsDarkMode));
        }
    }

    /// <summary>
    /// Cập nhật lại danh sách menu (icon failover có thể thay đổi).
    /// Không tạo lại ViewModel, chỉ clear + add lại items.
    /// </summary>
    public void RefreshMenus()
    {
        bool failoverOn = Preferences.Get("simulateFailover", false);

        ProfileMenus.Clear();
        ProfileMenus.Add(new ProfileMenuItem
        {
            Key = "history",
            Icon = "🕐",
            Title = "Lịch sử chuyến đi",
            Description = "Xem tất cả các chuyến đi của bạn"
        });
        ProfileMenus.Add(new ProfileMenuItem
        {
            Key = "toggle_failover",
            Icon = failoverOn ? "🔴" : "🟢",
            Title = failoverOn ? "TẮT giả lập Failover" : "BẬT giả lập Failover",
            Description = failoverOn
                ? "Đang ở chế độ Replica (Read-Only) — Nhấn để về Primary"
                : "Nhấn để giả lập Primary sập → chuyển Replica"
        });
        ProfileMenus.Add(new ProfileMenuItem
        {
            Key = "edit",
            Icon = "✏️",
            Title = "Chỉnh sửa thông tin",
            Description = "Cập nhật tên, email, số điện thoại"
        });
        ProfileMenus.Add(new ProfileMenuItem
        {
            Key = "feedback",
            Icon = "⭐",
            Title = "Đánh giá & Góp ý",
            Description = "Gửi phản hồi để cải thiện ứng dụng"
        });
    }
}
public class ProfileMenuItem
{
    public string Key { get; set; }
    public string Icon { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
}