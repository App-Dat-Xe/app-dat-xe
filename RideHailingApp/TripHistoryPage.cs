using RideHailingApp.Services;

namespace RideHailingApp;

public partial class TripHistoryPage : ContentPage
{
    private readonly ApiService _apiService;

    public TripHistoryPage()
    {
        InitializeComponent();
        _apiService = MauiProgram.Services.GetRequiredService<ApiService>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadTripHistoryAsync();
    }

    private async Task LoadTripHistoryAsync()
    {
        // Hiển thị / ẩn banner Read-Only
        bool isReadOnly = Preferences.Get("isReadOnly", false);
        ReadOnlyBanner.IsVisible = isReadOnly;
        if (isReadOnly)
        {
            string regionName = Preferences.Get("regionName", "HCM");
            ReplicaServerLabel.Text = $"replica-{(regionName.Contains("Nam") ? "hcm" : "hn")}";
        }

        int userId = Preferences.Get("userID", 0);
        if (userId == 0)
        {
            TripList.ItemsSource = new List<TripDisplayItem>();
            return;
        }

        // Gọi API thật — DataConnect tự động failover sang Replica nếu Primary sập
        var result = await _apiService.GetTripHistoryAsync(userId);

        if (result.IsSuccess && result.Data != null)
        {
            var items = result.Data.Select(t => new TripDisplayItem
            {
                StartAddress  = t.PickupLocation,
                DestAddress   = t.DropoffLocation,
                StatusText    = MapStatus(t.Status),
                StatusColor   = MapStatusColor(t.Status),
                PriceText     = "—",
                DateText      = t.CreatedAt.HasValue
                                    ? t.CreatedAt.Value.ToString("dd/MM/yyyy HH:mm")
                                    : "—",
                DriverName    = t.DriverID.HasValue ? $"Tài xế #{t.DriverID}" : "Chưa phân công",
                DriverInitial = t.DriverID.HasValue ? "T" : "?",
                Rating        = "—"
            }).ToList();

            TripList.ItemsSource = items;
        }
        else if (result.IsReadOnlyMode)
        {
            Preferences.Set("isReadOnly", true);
            ReadOnlyBanner.IsVisible = true;
            await DisplayAlert("Chế độ dự phòng",
                "Đang kết nối server dự phòng (Replica). Chỉ xem được lịch sử cũ.", "OK");
        }
        else
        {
            await DisplayAlert("Không thể tải lịch sử",
                result.ErrorMessage ?? "Kiểm tra lại kết nối mạng.", "OK");
            TripList.ItemsSource = new List<TripDisplayItem>();
        }
    }

    private static string MapStatus(string status) => status?.ToLower() switch
    {
        "completed" or "done" => "Hoàn thành",
        "cancelled"           => "Đã hủy",
        "active"              => "Đang đi",
        "pending"             => "Đang chờ",
        _                     => status ?? "—"
    };

    private static string MapStatusColor(string status) => status?.ToLower() switch
    {
        "completed" or "done" => "#00C853",
        "cancelled"           => "#FF5252",
        "active"              => "#2196F3",
        _                     => "#FFC107"
    };
}

public class TripDisplayItem
{
    public string StartAddress  { get; set; } = "";
    public string DestAddress   { get; set; } = "";
    public string StatusText    { get; set; } = "";
    public string StatusColor   { get; set; } = "#FFC107";
    public string PriceText     { get; set; } = "";
    public string DateText      { get; set; } = "";
    public string DriverName    { get; set; } = "";
    public string DriverInitial { get; set; } = "?";
    public string Rating        { get; set; } = "—";
}
