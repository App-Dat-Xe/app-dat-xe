using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using MBrush = Mapsui.Styles.Brush;
using MColor = Mapsui.Styles.Color;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace RideHailingApp;

public partial class MainPage : ContentPage
{
    private readonly RideHailingApp.Services.ApiService _apiService;
    private readonly RideHailingApp.Services.TripHubService _hub;

    // Map layers
    private MemoryLayer? _currentLocationLayer;
    private MemoryLayer? _pickupLayer;
    private MemoryLayer? _dropoffLayer;
    private MemoryLayer? _routeDotsLayer;
    private MemoryLayer? _driverPinLayer;

    private CancellationTokenSource? _gpsCts;
    private CancellationTokenSource? _searchTimeoutCts;
    private bool _isTracking = false;

    // Booking state
    private string? _activeTripId;
    private string _selectedVehicleType = "Xe máy";
    private decimal _selectedFare;

    // Pickup coords (set khi mở vehicle selection để vẽ route)
    private double _pickupLat = 10.7605;
    private double _pickupLon = 106.7035;

    // Giá cơ bản theo loại xe
    private static readonly (string Type, string Emoji, decimal BaseRate, int EtaMin) Moto
        = ("Xe máy",    "🛵", 3_000m, 3);
    private static readonly (string Type, string Emoji, decimal BaseRate, int EtaMin) Car4
        = ("Ô tô 4 chỗ", "🚗", 5_000m, 5);
    private static readonly (string Type, string Emoji, decimal BaseRate, int EtaMin) Car7
        = ("Ô tô 7 chỗ", "🚐", 7_000m, 8);

    private double _estimatedDistanceKm = 5.0;

    public MainPage()
    {
        InitializeComponent();
        _apiService = MauiProgram.Services.GetRequiredService<RideHailingApp.Services.ApiService>();
        _hub        = MauiProgram.Services.GetRequiredService<RideHailingApp.Services.TripHubService>();

        _hub.LocationUpdated   += OnDriverLocationUpdated;
        _hub.TripStatusChanged += OnTripStatusChanged;

        Connectivity.ConnectivityChanged += OnConnectivityChanged;

        InitializeMap();
        UpdateServerStatusUI();
    }

    // ───────────────── Server status UI ─────────────────

    private void UpdateServerStatusUI()
    {
        bool isReadOnly   = Preferences.Get("isReadOnly", false);
        string regionName = Preferences.Get("regionName", "Server Miền Nam (TP.HCM)");

        ServerStatusLabel.Text = isReadOnly
            ? $"⚠ {regionName} — Dự phòng"
            : $"● {regionName}";
        ServerStatusLabel.TextColor = isReadOnly
            ? MauiColor.FromArgb("#FFC107")
            : MauiColor.FromArgb("#00C853");

        ReadOnlyBanner.IsVisible = isReadOnly;

        FindDriverButton.IsEnabled = !isReadOnly;
        FindDriverButton.BackgroundColor = isReadOnly
            ? MauiColor.FromArgb("#3A3A3A")
            : MauiColor.FromArgb("#00C853");
        FindDriverButton.Text = isReadOnly
            ? "Không thể đặt xe (chế độ dự phòng)"
            : "Tìm tài xế";

        string userName = Preferences.Get("userName", "?");
        AvatarLabel.Text = string.IsNullOrEmpty(userName) ? "?" :
            userName.Trim().Split(' ').Last().ToUpper()[0].ToString();
    }

    // ───────────────── Map Initialization ─────────────────

    private void InitializeMap()
    {
        MyMap.Map.Layers.Add(OpenStreetMap.CreateTileLayer());

        foreach (var widget in MyMap.Map.Widgets)
        {
            string wName = widget.GetType().Name;
            if (wName == "LoggingWidget" || wName == "PerformanceWidget")
                widget.Enabled = false;
        }

        // Layer 1: Vị trí người dùng (xanh dương)
        _currentLocationLayer = new MemoryLayer
        {
            Name  = "UserLocation",
            Style = new SymbolStyle
            {
                Fill        = new MBrush(MColor.FromArgb(255, 33, 150, 243)),
                SymbolScale = 0.8,
                Outline     = new Pen { Color = MColor.White, Width = 2 }
            }
        };
        MyMap.Map.Layers.Add(_currentLocationLayer);

        // Layer 2: Đường route (chấm xanh lá - AC-01)
        _routeDotsLayer = new MemoryLayer { Name = "RouteDots" };
        MyMap.Map.Layers.Add(_routeDotsLayer);

        // Layer 3: Điểm đón (xanh lá, lớn hơn)
        _pickupLayer = new MemoryLayer { Name = "Pickup" };
        MyMap.Map.Layers.Add(_pickupLayer);

        // Layer 4: Điểm đến (đỏ)
        _dropoffLayer = new MemoryLayer { Name = "Dropoff" };
        MyMap.Map.Layers.Add(_dropoffLayer);

        // Layer 5: Vị trí tài xế (vàng - AC-06)
        _driverPinLayer = new MemoryLayer { Name = "Driver" };
        MyMap.Map.Layers.Add(_driverPinLayer);

        // Căn giữa bản đồ về TP.HCM
        Dispatcher.Dispatch(async () =>
        {
            await Task.Delay(300);
            var center = SphericalMercator.FromLonLat(106.7035, 10.7605);
            MyMap.Map.Navigator.CenterOn(new MPoint(center.x, center.y));
            MyMap.Map.Navigator.ZoomTo(2);
        });

        StartTrackingLocation();
    }

    // ───────────────── GPS Tracking ─────────────────

    private async void StartTrackingLocation()
    {
        try
        {
            _isTracking = true;
            while (_isTracking)
            {
                _gpsCts = new CancellationTokenSource();
                var req      = new GeolocationRequest(GeolocationAccuracy.High, TimeSpan.FromSeconds(5));
                var location = await Geolocation.Default.GetLocationAsync(req, _gpsCts.Token);
                if (location != null)
                {
                    _pickupLat = location.Latitude;
                    _pickupLon = location.Longitude;
                    UpdateUserPinOnMap(location.Latitude, location.Longitude);
                }
                await Task.Delay(5000);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPS Error: {ex.Message}");
        }
    }

    private void UpdateUserPinOnMap(double lat, double lon)
    {
        if (_currentLocationLayer == null) return;
        var coords  = SphericalMercator.FromLonLat(lon, lat);
        var feature = new PointFeature(new MPoint(coords.x, coords.y));
        _currentLocationLayer.Features = new List<IFeature> { feature };
        _currentLocationLayer.DataHasChanged();
    }

    // ───────────────── Route & Marker Drawing (AC-01, FR-02) ─────────────────

    private void DrawRouteOnMap(double pickupLat, double pickupLon, double dropoffLat, double dropoffLon)
    {
        if (_pickupLayer == null || _dropoffLayer == null || _routeDotsLayer == null) return;

        var pPick = SphericalMercator.FromLonLat(pickupLon, pickupLat);
        var pDrop = SphericalMercator.FromLonLat(dropoffLon, dropoffLat);

        // Điểm đón — xanh lá
        var pickupFeature = new PointFeature(new MPoint(pPick.x, pPick.y));
        pickupFeature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill       = new MBrush(MColor.FromArgb(255, 0, 200, 83)),
            SymbolScale = 0.9,
            Outline    = new Pen { Color = MColor.White, Width = 3 }
        });
        _pickupLayer.Features = new List<IFeature> { pickupFeature };
        _pickupLayer.DataHasChanged();

        // Điểm đến — đỏ
        var dropoffFeature = new PointFeature(new MPoint(pDrop.x, pDrop.y));
        dropoffFeature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Fill       = new MBrush(MColor.FromArgb(255, 220, 50, 50)),
            SymbolScale = 0.9,
            Outline    = new Pen { Color = MColor.White, Width = 3 }
        });
        _dropoffLayer.Features = new List<IFeature> { dropoffFeature };
        _dropoffLayer.DataHasChanged();

        // Đường nối (30 chấm xanh giữa 2 điểm)
        const int segments = 30;
        var dots = new List<IFeature>();
        for (int i = 1; i < segments; i++)
        {
            double t = (double)i / segments;
            var pt = new MPoint(
                pPick.x + (pDrop.x - pPick.x) * t,
                pPick.y + (pDrop.y - pPick.y) * t);
            var dot = new PointFeature(pt);
            dot.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                Fill       = new MBrush(MColor.FromArgb(160, 0, 200, 83)),
                SymbolScale = 0.25,
            });
            dots.Add(dot);
        }
        _routeDotsLayer.Features = dots;
        _routeDotsLayer.DataHasChanged();

        // Fit map để thấy cả 2 điểm
        Dispatcher.Dispatch(() =>
        {
            var midX = (pPick.x + pDrop.x) / 2;
            var midY = (pPick.y + pDrop.y) / 2;
            MyMap.Map.Navigator.CenterOn(new MPoint(midX, midY));
            MyMap.Map.Navigator.ZoomTo(3);
        });
    }

    private void ClearRouteFromMap()
    {
        if (_pickupLayer != null)  { _pickupLayer.Features  = new List<IFeature>(); _pickupLayer.DataHasChanged(); }
        if (_dropoffLayer != null) { _dropoffLayer.Features = new List<IFeature>(); _dropoffLayer.DataHasChanged(); }
        if (_routeDotsLayer != null) { _routeDotsLayer.Features = new List<IFeature>(); _routeDotsLayer.DataHasChanged(); }
        if (_driverPinLayer != null) { _driverPinLayer.Features = new List<IFeature>(); _driverPinLayer.DataHasChanged(); }
    }

    // ───────────────── Panel State Management ─────────────────

    private void ShowPanel(string panelName)
    {
        SearchFormPanel.IsVisible       = panelName == "search";
        VehicleSelectionPanel.IsVisible = panelName == "vehicle";
        SearchingPanel.IsVisible        = panelName == "searching";
        ActiveTripPanel.IsVisible       = panelName == "active";

        SearchingIndicator.IsRunning = panelName == "searching";

        if (panelName == "search")
            ClearRouteFromMap();
    }

    // ───────────────── Giai đoạn 1: Tìm tài xế — PreviewingTrip ─────────────────

    private async void OnFindDriverClicked(object sender, EventArgs e)
    {
        if (Preferences.Get("isReadOnly", false))
        {
            await DisplayAlert("Không thể đặt xe",
                "Hệ thống đang ở chế độ dự phòng (Replica).\nChỉ có thể xem lịch sử chuyến đi.", "OK");
            return;
        }

        string dest = DestinationEntry.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(dest))
        {
            await DisplayAlert("Thiếu thông tin", "Vui lòng nhập điểm đến.", "OK");
            return;
        }

        // Tính khoảng cách ước tính (seed từ text để nhất quán)
        int seed = Math.Abs((PickupEntry.Text ?? "" + dest).GetHashCode());
        _estimatedDistanceKm = 3.0 + (seed % 120) / 10.0;

        // Ước tính tọa độ dropoff từ GPS pickup + bearing ngẫu nhiên (FR-02)
        double bearingRad = (seed % 360) * Math.PI / 180.0;
        double dropoffLat = _pickupLat + (_estimatedDistanceKm / 111.0) * Math.Cos(bearingRad);
        double dropoffLon = _pickupLon + (_estimatedDistanceKm / (111.0 * Math.Cos(_pickupLat * Math.PI / 180.0))) * Math.Sin(bearingRad);

        // Vẽ route lên bản đồ (AC-01)
        DrawRouteOnMap(_pickupLat, _pickupLon, dropoffLat, dropoffLon);

        // Tính ETA di chuyển: quãng đường / tốc độ trung bình 30km/h
        int travelMinutes = Math.Max(5, (int)(_estimatedDistanceKm / 30.0 * 60));

        RouteInfoLabel.Text = $"📏 {_estimatedDistanceKm:F1} km • ⏱ ~{travelMinutes} phút di chuyển";

        // Cập nhật giá từng loại xe (Giai đoạn 2)
        UpdateVehicleOption(MotoOptionBorder, MotoPriceLabel, MotoEtaLabel, Moto);
        UpdateVehicleOption(Car4OptionBorder, Car4PriceLabel, Car4EtaLabel, Car4);
        UpdateVehicleOption(Car7OptionBorder, Car7PriceLabel, Car7EtaLabel, Car7);

        SelectVehicle("Xe máy");
        ShowPanel("vehicle");
    }

    private void UpdateVehicleOption(Border border, Label priceLabel, Label etaLabel,
        (string Type, string Emoji, decimal BaseRate, int EtaMin) v)
    {
        decimal fare = Math.Round((10_000m + v.BaseRate * (decimal)_estimatedDistanceKm) / 1000m) * 1000m;
        int etaMin   = v.EtaMin + (int)(_estimatedDistanceKm / 3);

        priceLabel.Text = $"{fare:#,##0}đ";
        etaLabel.Text   = $"~{etaMin} phút đến đón";
    }

    // ───────────────── Giai đoạn 2: Chọn loại xe — VehicleSelection ─────────────────

    private void SelectVehicle(string vehicleType)
    {
        _selectedVehicleType = vehicleType;

        var selectedStroke   = new SolidColorBrush(MauiColor.FromArgb("#00C853"));
        var unselectedStroke = new SolidColorBrush(MauiColor.FromArgb("#2E3348"));

        MotoOptionBorder.Stroke = vehicleType == "Xe máy"     ? selectedStroke : unselectedStroke;
        Car4OptionBorder.Stroke = vehicleType == "Ô tô 4 chỗ" ? selectedStroke : unselectedStroke;
        Car7OptionBorder.Stroke = vehicleType == "Ô tô 7 chỗ" ? selectedStroke : unselectedStroke;

        // Giá & ETA cho xe đã chọn
        var v = vehicleType switch
        {
            "Ô tô 4 chỗ" => Car4,
            "Ô tô 7 chỗ" => Car7,
            _             => Moto
        };
        _selectedFare = Math.Round((10_000m + v.BaseRate * (decimal)_estimatedDistanceKm) / 1000m) * 1000m;
        int etaMin    = v.EtaMin + (int)(_estimatedDistanceKm / 3);

        // Cập nhật summary bar (AC-03, FR-04)
        SelectedVehicleLabel.Text = $"{v.Emoji} {v.Type}";
        SelectedFareLabel.Text    = $"{_selectedFare:#,##0}đ";
        SelectedEtaLabel.Text     = $"~{etaMin} phút";
    }

    private void OnMotoSelected(object sender, TappedEventArgs e) => SelectVehicle("Xe máy");
    private void OnCar4Selected(object sender, TappedEventArgs e) => SelectVehicle("Ô tô 4 chỗ");
    private void OnCar7Selected(object sender, TappedEventArgs e) => SelectVehicle("Ô tô 7 chỗ");

    private void OnCancelVehicleSelection(object sender, EventArgs e)
    {
        ClearRouteFromMap();
        ShowPanel("search");
    }

    // ───────────────── Giai đoạn 3: Xác nhận đặt cuốc — WaitingForConfirmation ─────────────────

    private async void OnConfirmBookingClicked(object sender, EventArgs e)
    {
        int userId = Preferences.Get("userID", 0);
        if (userId == 0)
        {
            await DisplayAlert("Chưa đăng nhập", "Vui lòng đăng nhập trước.", "OK");
            return;
        }

        string pickup  = string.IsNullOrWhiteSpace(PickupEntry.Text) ? "Vị trí hiện tại" : PickupEntry.Text.Trim();
        string dropoff = DestinationEntry.Text.Trim();

        ConfirmBookingButton.IsEnabled = false;

        var result = await _apiService.BookTripAsync(userId, pickup, dropoff, _selectedVehicleType);

        if (result.IsSuccess && result.Data != null)
        {
            _activeTripId = result.Data.TripId.ToString();

            // Chuẩn bị ActiveTripPanel
            TripPickupLabel.Text      = pickup;
            TripDropoffLabel.Text     = dropoff;
            TripStatusLabel.Text      = "● Đang tìm tài xế";
            TripStatusLabel.TextColor = MauiColor.FromArgb("#FFC107");
            DriverInfoCard.IsVisible  = false;
            DriverEtaBanner.IsVisible = false;

            // Kết nối SignalR (Giai đoạn 4 — FR-07)
            try
            {
                await _hub.StartAsync();
                await _hub.JoinTripGroupAsync(_activeTripId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SignalR error: {ex.Message}");
            }

            ShowPanel("searching");
            SearchingStatusLabel.Text = $"Chuyến #{_activeTripId} • {_selectedVehicleType} • {_selectedFare:#,##0}đ";

            // Bắt đầu đếm timeout tìm tài xế (Exception 8.3)
            StartDriverSearchTimeout();
        }
        else if (result.IsReadOnlyMode)
        {
            Preferences.Set("isReadOnly", true);
            UpdateServerStatusUI();
            ShowPanel("search");
            ConfirmBookingButton.IsEnabled = true;
            await DisplayAlert("Không thể đặt xe",
                "Server chính đang bảo trì.\nHệ thống chuyển sang chế độ Read-Only — bạn chỉ xem được lịch sử.", "OK");
        }
        else
        {
            ShowPanel("vehicle");
            ConfirmBookingButton.IsEnabled = true;
            await DisplayAlert("Lỗi", result.ErrorMessage ?? "Không thể kết nối server.", "OK");
        }
    }

    // ───────────────── Exception 8.3: Timeout tìm tài xế ─────────────────

    private void StartDriverSearchTimeout()
    {
        _searchTimeoutCts?.Cancel();
        _searchTimeoutCts = new CancellationTokenSource();
        var token = _searchTimeoutCts.Token;

        _ = Task.Delay(90_000, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.Dispatch(async () =>
            {
                if (!SearchingPanel.IsVisible) return;

                bool keepWaiting = await DisplayAlert(
                    "Chưa tìm thấy tài xế",
                    "Hệ thống chưa tìm được tài xế phù hợp trong 90 giây.\nBạn muốn tiếp tục chờ hay hủy chuyến?",
                    "Tiếp tục chờ", "Hủy chuyến");

                if (keepWaiting)
                    StartDriverSearchTimeout();     // Restart timer
                else
                    await CancelTripAsync();
            });
        });
    }

    // ───────────────── Exception 8.3/6.3: Hủy cuốc ─────────────────

    private async void OnCancelTripClicked(object? sender, EventArgs e)
    {
        bool confirmed = await DisplayAlert("Hủy cuốc",
            "Bạn có chắc muốn hủy yêu cầu đặt xe không?",
            "Xác nhận hủy", "Tiếp tục chờ");
        if (!confirmed) return;
        await CancelTripAsync();
    }

    private async Task CancelTripAsync()
    {
        _searchTimeoutCts?.Cancel();

        if (_activeTripId != null)
        {
            await _apiService.CancelTripAsync(int.Parse(_activeTripId));
            try { await _hub.LeaveTripGroupAsync(_activeTripId); } catch { }
        }

        _activeTripId = null;
        ClearRouteFromMap();
        ShowPanel("search");
        ConfirmBookingButton.IsEnabled = true;
    }

    // ───────────────── SignalR: Driver location update (AC-06, FR-07) ─────────────────

    private void OnDriverLocationUpdated(double lat, double lon)
    {
        Dispatcher.Dispatch(() =>
        {
            // Cập nhật pin tài xế trên bản đồ
            if (_driverPinLayer != null)
            {
                var coords  = SphericalMercator.FromLonLat(lon, lat);
                var feature = new PointFeature(new MPoint(coords.x, coords.y));
                feature.Styles.Add(new SymbolStyle
                {
                    SymbolType = SymbolType.Ellipse,
                    Fill       = new MBrush(MColor.FromArgb(255, 255, 193, 7)),
                    SymbolScale = 0.85,
                    Outline    = new Pen { Color = MColor.White, Width = 2 }
                });
                _driverPinLayer.Features = new List<IFeature> { feature };
                _driverPinLayer.DataHasChanged();
            }

            // Tính ETA tài xế đến điểm đón (Haversine)
            double dlat = (_pickupLat - lat) * Math.PI / 180.0;
            double dlon = (_pickupLon - lon) * Math.PI / 180.0;
            double a = Math.Sin(dlat / 2) * Math.Sin(dlat / 2)
                     + Math.Cos(lat * Math.PI / 180) * Math.Cos(_pickupLat * Math.PI / 180)
                       * Math.Sin(dlon / 2) * Math.Sin(dlon / 2);
            double distKm    = 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            int etaMinutes   = Math.Max(1, (int)(distKm / 30.0 * 60));

            DriverEtaLabel.Text     = $"Cách bạn ~{distKm:F1} km • đến trong ~{etaMinutes} phút";
            DriverEtaBanner.IsVisible = ActiveTripPanel.IsVisible;
        });
    }

    // ───────────────── SignalR: Trip status (FR-05, FR-06, FR-07) ─────────────────

    private async void OnTripStatusChanged(string status, string message)
    {
        await Dispatcher.DispatchAsync(() =>
        {
            switch (status)
            {
                // FR-06: Tài xế nhận cuốc — DriverAccepted
                case "DriverAccepted":
                    _searchTimeoutCts?.Cancel();     // Đã tìm được tài xế
                    TripStatusLabel.Text      = "● Tài xế đã nhận cuốc";
                    TripStatusLabel.TextColor = MauiColor.FromArgb("#00C853");
                    ShowDriverInfoFromMessage(message);
                    ShowPanel("active");
                    break;

                // FR-07: Tài xế đang đến đón — DriverOnTheWay
                case "DriverOnTheWay":
                    TripStatusLabel.Text      = "● Tài xế đang trên đường đến đón bạn";
                    TripStatusLabel.TextColor = MauiColor.FromArgb("#2196F3");
                    DriverEtaBanner.IsVisible = true;
                    ShowPanel("active");
                    break;

                // InProgress: Đã đón khách
                case "InProgress":
                    TripStatusLabel.Text      = "● Đang di chuyển đến điểm đến";
                    TripStatusLabel.TextColor = MauiColor.FromArgb("#00C853");
                    DriverEtaBanner.IsVisible = false;
                    ShowPanel("active");
                    break;

                // Hoàn thành
                case "Completed":
                    _searchTimeoutCts?.Cancel();
                    if (_activeTripId != null)
                    {
                        try { _hub.LeaveTripGroupAsync(_activeTripId).GetAwaiter().GetResult(); } catch { }
                    }
                    _activeTripId = null;
                    ClearRouteFromMap();
                    ShowPanel("search");
                    ConfirmBookingButton.IsEnabled = true;
                    DisplayAlert("Hoàn thành chuyến đi", message, "OK");
                    break;

                // Exception 8.4: Tài xế hủy → quay về trạng thái SearchingDriver
                case "CancelledByDriver":
                    DriverInfoCard.IsVisible  = false;
                    DriverEtaBanner.IsVisible = false;
                    ShowPanel("searching");
                    SearchingStatusLabel.Text = "Tài xế vừa hủy cuốc. Đang tìm tài xế khác...";
                    StartDriverSearchTimeout();   // Restart timeout
                    DisplayAlert("Tài xế đã hủy", "Hệ thống đang tự động tìm tài xế khác cho bạn.", "OK");
                    break;
            }
        });
    }

    private void ShowDriverInfoFromMessage(string message)
    {
        // Hiện thẻ tài xế (tên/biển số nếu server gửi kèm trong message)
        DriverInfoCard.IsVisible = true;
        TripDriverName.Text    = "Tài xế";
        TripDriverPlate.Text   = _selectedVehicleType;
        TripDriverInitial.Text = "T";
    }

    // ───────────────── Exception 8.5: Mất kết nối mạng ─────────────────

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        bool hasInternet = e.NetworkAccess == NetworkAccess.Internet;
        Dispatcher.Dispatch(() =>
        {
            SearchNetworkWarning.IsVisible = !hasInternet && SearchingPanel.IsVisible;
            ActiveNetworkWarning.IsVisible = !hasInternet && ActiveTripPanel.IsVisible;
        });
    }

    // ───────────────── Lifecycle ─────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateServerStatusUI();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isTracking = false;
        _gpsCts?.Cancel();
        _searchTimeoutCts?.Cancel();
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;
    }

    private async void OnMyLocationClicked(object sender, EventArgs e)
    {
        var location = await Geolocation.Default.GetLastKnownLocationAsync()
                       ?? await Geolocation.Default.GetLocationAsync(
                              new GeolocationRequest(GeolocationAccuracy.Medium));

        if (location != null)
        {
            _pickupLat = location.Latitude;
            _pickupLon = location.Longitude;
            var center = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
            MyMap.Map.Navigator.CenterOn(new MPoint(center.x, center.y));
            MyMap.Map.Navigator.ZoomTo(2);
        }
    }
}
