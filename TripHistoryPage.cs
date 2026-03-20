using System.Collections.ObjectModel;

namespace RideHailingApp;

public partial class TripHistoryPage : ContentPage
{
    // ── Mock: lịch sử chuyến đi giả ──
    private readonly List<MockTrip> _mockTrips = new()
    {
        new MockTrip
        {
            StartAddress = "123 Nguyễn Huệ, Quận 1",
            DestAddress  = "Sân bay Tân Sơn Nhất",
            Status       = "done",
            Price        = 85000,
            CreatedAt    = DateTime.Now.AddDays(-1),
            DriverName   = "Nguyễn Văn Tuấn",
            Rating       = 5.0
        },
        new MockTrip
        {
            StartAddress = "Vincom Center, Quận 1",
            DestAddress  = "Đại học Bách Khoa TP.HCM",
            Status       = "done",
            Price        = 42000,
            CreatedAt    = DateTime.Now.AddDays(-2),
            DriverName   = "Trần Văn Minh",
            Rating       = 4.8
        },
        new MockTrip
        {
            StartAddress = "Bến xe Miền Đông",
            DestAddress  = "75 Hai Bà Trưng, Quận 3",
            Status       = "done",
            Price        = 55000,
            CreatedAt    = DateTime.Now.AddDays(-3),
            DriverName   = "Lê Văn Hùng",
            Rating       = 4.7
        },
        new MockTrip
        {
            StartAddress = "Chợ Bến Thành",
            DestAddress  = "Khu đô thị Phú Mỹ Hưng, Quận 7",
            Status       = "cancelled",
            Price        = 0,
            CreatedAt    = DateTime.Now.AddDays(-5),
            DriverName   = "Phạm Thị Linh",
            Rating       = 0
        },
        new MockTrip
        {
            StartAddress = "Nhà hát TP.HCM",
            DestAddress  = "Công viên Gia Định, Gò Vấp",
            Status       = "done",
            Price        = 68000,
            CreatedAt    = DateTime.Now.AddDays(-7),
            DriverName   = "Hoàng Văn Nam",
            Rating       = 4.9
        },
    };

    public TripHistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadMockData();
    }

    private void LoadMockData()
    {
        bool isReadOnly = Preferences.Get("isReadOnly", false);

        // Hiện banner read-only nếu đang ở chế độ dự phòng
        ReadOnlyBanner.IsVisible = isReadOnly;
        if (isReadOnly)
        {
            string regionName = Preferences.Get("regionName", "HCM");
            ReplicaServerLabel.Text = $"replica-{(regionName.Contains("Nam") ? "hcm" : "hn")}:5433";
        }

        // Bind dữ liệu mock vào CollectionView
        TripList.ItemsSource = _mockTrips.Select(t => new TripDisplayItem
        {
            StartAddress = t.StartAddress,
            DestAddress  = t.DestAddress,
            StatusText   = t.StatusText,
            StatusColor  = t.StatusColor,
            PriceText    = t.PriceText,
            DateText     = t.DateText,
            DriverName   = t.DriverName,
            DriverInitial = t.DriverInitial,
            Rating       = t.Rating > 0 ? t.Rating.ToString("F1") : "-"
        }).ToList();
    }
}

// ── Models cho hiển thị ──
public class MockTrip
{
    public string   StartAddress { get; set; } = "";
    public string   DestAddress  { get; set; } = "";
    public string   Status       { get; set; } = "done";
    public decimal  Price        { get; set; }
    public DateTime CreatedAt    { get; set; }
    public string   DriverName   { get; set; } = "";
    public double   Rating       { get; set; }

    public string StatusText => Status switch
    {
        "done"      => "Hoàn thành",
        "cancelled" => "Đã hủy",
        "active"    => "Đang đi",
        _           => "Đang chờ"
    };

    public string StatusColor => Status switch
    {
        "done"      => "#00C853",
        "cancelled" => "#FF5252",
        "active"    => "#2196F3",
        _           => "#FFC107"
    };

    public string PriceText    => Price > 0 ? $"{Price:#,##0}đ" : "Đã hủy";
    public string DateText     => CreatedAt.ToString("dd/MM/yyyy HH:mm");
    public string DriverInitial => string.IsNullOrEmpty(DriverName) ? "?" :
        DriverName.Trim().Split(' ').Last().ToUpper()[0].ToString();
}

public class TripDisplayItem
{
    public string StartAddress  { get; set; }
    public string DestAddress   { get; set; }
    public string StatusText    { get; set; }
    public string StatusColor   { get; set; }
    public string PriceText     { get; set; }
    public string DateText      { get; set; }
    public string DriverName    { get; set; }
    public string DriverInitial { get; set; }
    public string Rating        { get; set; }
}
