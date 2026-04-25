using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using MBrush = Mapsui.Styles.Brush;
using MColor = Mapsui.Styles.Color;              // dùng cho Mapsui layer/style
using MauiColor = Microsoft.Maui.Graphics.Color;    // dùng cho MAUI UI

namespace RideHailingApp;

public partial class MainPage : ContentPage
{
    private MemoryLayer _currentLocationLayer;
    private MemoryLayer _driverLayer;
    private CancellationTokenSource _cts;
    private bool _isTracking = false;
    // Khai báo ApiService
    private readonly RideHailingApp.Services.ApiService _apiService;
    private readonly RideHailingApp.Services.TripHubService _hub;
    private string? _activeTripId;
    // ── Mock: danh sách tài xế giả ──
    private readonly List<MockDriver> _mockDrivers = new()
    {
        new MockDriver { Name = "Tài xế Tuấn",  Plate = "59B-12345", Rating = 4.9, Lat = 10.7615, Lon = 106.7040, Price = 25000, Arrival = 3 },
        new MockDriver { Name = "Tài xế Minh",  Plate = "51A-67890", Rating = 4.7, Lat = 10.7590, Lon = 106.7020, Price = 22000, Arrival = 5 },
        new MockDriver { Name = "Tài xế Hùng",  Plate = "59C-11111", Rating = 4.8, Lat = 10.7635, Lon = 106.7055, Price = 28000, Arrival = 2 },
        new MockDriver { Name = "Tài xế Linh",  Plate = "59D-22222", Rating = 5.0, Lat = 10.7575, Lon = 106.7065, Price = 30000, Arrival = 4 },
    };

    private MockDriver _selectedDriver;

    public MainPage()
    {
        InitializeComponent();
        _apiService = MauiProgram.Services.GetRequiredService<RideHailingApp.Services.ApiService>();
        _hub        = MauiProgram.Services.GetRequiredService<RideHailingApp.Services.TripHubService>();

        _hub.LocationUpdated    += OnDriverLocationUpdated;
        _hub.TripStatusChanged  += OnTripStatusChanged;

        InitializeMap();
        UpdateServerStatusUI();
    }

    // Cập nhật UI trạng thái server (Read-Only hay không)
    private void UpdateServerStatusUI()
    {
        bool isReadOnly = Preferences.Get("isReadOnly", false);
        string regionName = Preferences.Get("regionName", "Server Miền Nam (TP.HCM)");

        ServerStatusLabel.Text = isReadOnly
            ? $"⚠ {regionName} — Dự phòng"
            : $"● {regionName}";
        ServerStatusLabel.TextColor = isReadOnly
            ? MauiColor.FromArgb("#FFC107")
            : MauiColor.FromArgb("#00C853");

        ReadOnlyBanner.IsVisible = isReadOnly;

        // Ẩn nút tìm xe khi read-only
        FindDriverButton.IsEnabled = !isReadOnly;
        FindDriverButton.BackgroundColor = isReadOnly
            ? MauiColor.FromArgb("#3A3A3A")
            : MauiColor.FromArgb("#00C853");
        FindDriverButton.Text = isReadOnly
            ? "Không thể đặt xe (chế độ dự phòng)"
            : "Tìm tài xế";

        // Hiện avatar chữ cái đầu tên
        string userName = Preferences.Get("userName", "?");
        AvatarLabel.Text = string.IsNullOrEmpty(userName) ? "?" :
            userName.Trim().Split(' ').Last().ToUpper()[0].ToString();
    }

    private void InitializeMap()
    {
        MyMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer());

        // Tắt widget log/performance
        foreach (var widget in MyMap.Map.Widgets)
        {
            string name = widget.GetType().Name;
            if (name == "LoggingWidget" || name == "PerformanceWidget")
                widget.Enabled = false;
        }

        // Layer vị trí người dùng
        _currentLocationLayer = new MemoryLayer
        {
            Name = "LocationLayer",
            Style = new SymbolStyle
            {
                Fill = new MBrush(MColor.FromArgb(255, 0, 200, 83)),
                SymbolScale = 0.6,
                Outline = new Pen { Color = MColor.White, Width = 2 }
            }
        };
        MyMap.Map.Layers.Add(_currentLocationLayer);

        // Layer tài xế mock
        _driverLayer = new MemoryLayer { Name = "DriverLayer" };
        var features = new List<IFeature>();

        foreach (var driver in _mockDrivers)
        {
            var coords = SphericalMercator.FromLonLat(driver.Lon, driver.Lat);
            var feature = new PointFeature(new MPoint(coords.x, coords.y));
            feature["Name"] = driver.Name;
            feature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill = new MBrush(MColor.FromArgb(255, 255, 193, 7)),
                SymbolScale = 0.5,
                Outline = new Pen { Color = MColor.White, Width = 1 }
            });
            features.Add(feature);
        }

        _driverLayer.Features = features;
        _driverLayer.DataHasChanged();
        MyMap.Map.Layers.Add(_driverLayer);

        // Bắt sự kiện chạm vào tài xế
        MyMap.Info += OnMapInfo;

        // Căn giữa bản đồ về khu vực TP.HCM
        Dispatcher.Dispatch(async () =>
        {
            await Task.Delay(300);
            var center = SphericalMercator.FromLonLat(106.7035, 10.7605);
            MyMap.Map.Navigator.CenterOn(new MPoint(center.x, center.y));
            MyMap.Map.Navigator.ZoomTo(2);
        });
        _currentLocationLayer = new MemoryLayer
        {
            Name = "LocationLayer",
            Style = new SymbolStyle
            {
                Fill = new MBrush(MColor.FromArgb(255, 33, 150, 243)), // Màu xanh dương cho GPS
                SymbolScale = 0.8,
                Outline = new Pen { Color = MColor.White, Width = 2 }
            }
        };
        MyMap.Map.Layers.Add(_currentLocationLayer);

        // Bắt đầu track GPS khi mở app
        StartTrackingLocation();
    }
    private async void StartTrackingLocation()
    {
        try
        {
            _isTracking = true;
            while (_isTracking)
            {
                var request = new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(5));
                _cts = new CancellationTokenSource();
                var location = await Geolocation.Default.GetLocationAsync(request, _cts.Token);

                if (location != null)
                {
                    UpdateUserLocationOnMap(location.Latitude, location.Longitude);
                }

                await Task.Delay(5000); // Cập nhật mỗi 5 giây
            }
        }
        catch (Exception ex)
        {
            // Xử lý lỗi (người dùng tắt GPS, không cấp quyền...)
            System.Diagnostics.Debug.WriteLine($"GPS Error: {ex.Message}");
        }
    }

    private void UpdateUserLocationOnMap(double lat, double lon)
    {
        var coords = SphericalMercator.FromLonLat(lon, lat);
        var feature = new PointFeature(new MPoint(coords.x, coords.y));

        // Cập nhật dữ liệu cho layer
        _currentLocationLayer.Features = new List<IFeature> { feature };
        _currentLocationLayer.DataHasChanged();
    }
    // ── SignalR handlers ──

    private void OnDriverLocationUpdated(double lat, double lng)
    {
        UpdateUserLocationOnMap(lat, lng);
    }

    private async void OnTripStatusChanged(string status, string message)
    {
        await DisplayAlert("Cập nhật chuyến đi", message, "OK");

        if (status == "Completed" && _activeTripId != null)
        {
            await _hub.LeaveTripGroupAsync(_activeTripId);
            _activeTripId = null;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isTracking = false;
        _cts?.Cancel();
    }

    // Chạm vào tài xế trên bản đồ → hiện popup
    private void OnMapInfo(object sender, MapInfoEventArgs e)
    {
        var mapInfo = e.GetMapInfo(new List<ILayer> { _driverLayer });
        if (mapInfo?.Feature == null) return;

        var name = mapInfo.Feature["Name"]?.ToString();
        _selectedDriver = _mockDrivers.FirstOrDefault(d => d.Name == name);

        if (_selectedDriver != null)
            ShowDriverPopup(_selectedDriver);
    }

    private void ShowDriverPopup(MockDriver driver)
    {
        DriverInitial.Text = driver.Name.Split(' ').Last().ToUpper()[0].ToString();
        PopupTitle.Text = driver.Name;
        PopupDescription.Text = $"{driver.Plate}";
        DriverDistance.Text = $"{new Random().Next(100, 800)}m";
        TripPrice.Text = $"{driver.Price:#,##0}đ";
        ArrivalTime.Text = $"{driver.Arrival} phút";

        // Chặn đặt xe khi read-only
        bool isReadOnly = Preferences.Get("isReadOnly", false);
        BookButton.IsEnabled = !isReadOnly;
        BookButton.Text = isReadOnly ? "Không thể đặt (chế độ dự phòng)" : "Đặt xe ngay";
        BookButton.BackgroundColor = isReadOnly
            ? MauiColor.FromArgb("#3A3A3A")
            : MauiColor.FromArgb("#00C853");

        PopupOverlay.IsVisible = true;
    }

    private void OnClosePopup(object sender, EventArgs e)
    {
        PopupOverlay.IsVisible = false;
    }

    private async void OnBookRideClicked(object sender, EventArgs e)
    {
        if (Preferences.Get("isReadOnly", false))
        {
            await DisplayAlert("Không thể đặt xe",
                "Hệ thống đang ở chế độ dự phòng (Replica).\nChỉ có thể xem lịch sử chuyến đi.",
                "OK");
            return;
        }

        if (_selectedDriver == null) return;

        PopupOverlay.IsVisible = false;
        await DisplayAlert("Đặt xe thành công!",
            $"Tài xế {_selectedDriver.Name} đang trên đường đến.\nDự kiến: {_selectedDriver.Arrival} phút.",
            "OK");
    }

    private async void OnFindDriverClicked(object sender, EventArgs e)
    {
        if (Preferences.Get("isReadOnly", false))
        {
            await DisplayAlert("Không thể đặt xe",
                "Hệ thống đang ở chế độ dự phòng.\nVui lòng thử lại sau.",
                "OK");
            return;
        }

        string dest = DestinationEntry.Text?.Trim();
        if (string.IsNullOrEmpty(dest))
        {
            await DisplayAlert("Thiếu thông tin", "Vui lòng nhập điểm đến.", "OK");
            return;
        }

        // Lấy userId thật từ session sau khi đăng nhập
        int userId = Preferences.Get("userID", 0);
        if (userId == 0)
        {
            await DisplayAlert("Chưa đăng nhập", "Vui lòng đăng nhập trước.", "OK");
            return;
        }

        string pickup = string.IsNullOrEmpty(PickupEntry.Text) ? "Vị trí hiện tại" : PickupEntry.Text.Trim();
        string region = Preferences.Get("currentRegion", "South");

        // Gọi API đặt xe thật
        var result = await _apiService.BookTripAsync(userId, pickup, dest);

        if (result.IsSuccess && result.Data != null)
        {
            _activeTripId = result.Data.TripId.ToString();

            // Kết nối SignalR và tham gia group chuyến đi để nhận GPS + thông báo realtime
            try
            {
                await _hub.StartAsync();
                await _hub.JoinTripGroupAsync(_activeTripId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR connect error: {ex.Message}");
            }

            await DisplayAlert("Đặt xe thành công!",
                $"Chuyến đi #{_activeTripId} đã được ghi vào Server {region}.\nĐang tìm tài xế...", "OK");
        }
        else if (result.IsReadOnlyMode)
        {
            // Primary sập → cập nhật flag và refresh UI ngay
            Preferences.Set("isReadOnly", true);
            UpdateServerStatusUI();
            await DisplayAlert("Không thể đặt xe",
                "Server chính đang bảo trì.\nHệ thống chuyển sang chế độ Read-Only — bạn chỉ xem được lịch sử.", "OK");
        }
        else
        {
            await DisplayAlert("Lỗi", result.ErrorMessage ?? "Không thể kết nối server.", "OK");
        }
    }

    private async void OnMyLocationClicked(object sender, EventArgs e)
    {
        var location = await Geolocation.Default.GetLastKnownLocationAsync();
        if (location == null)
        {
            location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium));
        }

        if (location != null)
        {
            var center = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
            MyMap.Map.Navigator.CenterOn(new MPoint(center.x, center.y));
            MyMap.Map.Navigator.ZoomTo(2);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateServerStatusUI(); // refresh mỗi khi quay lại trang
    }
}

// Model tài xế mock
public class MockDriver
{
    public string Name { get; set; }
    public string Plate { get; set; }
    public double Rating { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public decimal Price { get; set; }
    public int Arrival { get; set; }
}