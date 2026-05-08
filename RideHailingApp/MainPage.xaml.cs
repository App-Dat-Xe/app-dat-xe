using System.Globalization;
using RideHailingApp.Services;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace RideHailingApp;

public partial class MainPage : ContentPage
{
    private readonly ApiService _apiService;
    private readonly TripHubService _hub;
    private readonly GoogleMapsService _mapsService;

    private CancellationTokenSource? _gpsCts;
    private CancellationTokenSource? _searchTimeoutCts;
    private bool _isTracking = false;

    // Booking state
    private string? _activeTripId;
    private string _selectedVehicleType = "Xe máy";
    private decimal _selectedFare;
    private string _selectedServiceType = "Xe máy"; // từ nút home
    private bool _isPassengerInVehicle = false; // true khi đang di chuyển đến điểm đến

    // Tọa độ
    private double _pickupLat = 10.7605;
    private double _pickupLon = 106.7035;
    private double _dropoffLat;
    private double _dropoffLon;
    private string _encodedPolyline = "";

    // Địa chỉ
    private string _pickupAddress = "Vị trí hiện tại";
    private string _destinationAddress = "";

    private CancellationTokenSource? _pickupAutocompleteCts;
    private CancellationTokenSource? _destAutocompleteCts;

    private static readonly (string Type, string Emoji, decimal BaseRate, int EtaMin) Moto
        = ("Xe máy",     "🛵", 3_000m, 3);
    private static readonly (string Type, string Emoji, decimal BaseRate, int EtaMin) Car4
        = ("Ô tô 4 chỗ", "🚗", 5_000m, 5);
    private static readonly (string Type, string Emoji, decimal BaseRate, int EtaMin) Car7
        = ("Ô tô 7 chỗ", "🚐", 7_000m, 8);

    private double _estimatedDistanceKm = 5.0;

    public MainPage()
    {
        InitializeComponent();
        _apiService  = MauiProgram.Services.GetRequiredService<ApiService>();
        _hub         = MauiProgram.Services.GetRequiredService<TripHubService>();
        _mapsService = MauiProgram.Services.GetRequiredService<GoogleMapsService>();

        _hub.LocationUpdated       += OnDriverLocationUpdated;
        _hub.TripStatusChanged     += OnTripStatusChanged;
        _hub.MaintenanceModeChanged += OnMaintenanceModeChanged;
        _hub.DatabaseStatusChanged  += OnDatabaseStatusChanged;
        ApiService.OnDatabaseStatusChanged += OnApiDatabaseStatusChanged;

        Connectivity.ConnectivityChanged += OnConnectivityChanged;

        UpdateServerStatusUI();
        StartTrackingLocation();
    }

    // ─────────────────────────────────────────────
    //  Server status
    // ─────────────────────────────────────────────

    private void UpdateServerStatusUI()
    {
        bool isReadOnly   = Preferences.Get("isReadOnly", false);
        bool isMaintenance = Preferences.Get("isMaintenanceMode", false);
        string regionName = Preferences.Get("regionName", "Server Miền Nam");

        if (isMaintenance)
        {
            ServerStatusLabel.Text      = "⛔ Hệ thống đang bảo trì";
            ServerStatusLabel.TextColor = MauiColor.FromArgb("#FF5252");
        }
        else
        {
            ServerStatusLabel.Text = isReadOnly
                ? $"⚠ {regionName} — Dự phòng"
                : $"● {regionName}";
            ServerStatusLabel.TextColor = isReadOnly
                ? MauiColor.FromArgb("#FFC107")
                : MauiColor.FromArgb("#CCFFDD");
        }

        ReadOnlyBanner.IsVisible   = isReadOnly && !isMaintenance;
        MaintenanceBanner.IsVisible = isMaintenance;

        string userName = Preferences.Get("userName", "?");
        AvatarLabel.Text = string.IsNullOrEmpty(userName) ? "?" :
            userName.Trim().Split(' ').Last().ToUpper()[0].ToString();
    }

    private async void OnMaintenanceModeChanged(bool isActive, string message, DateTime? estimatedEndTime)
    {
        Preferences.Set("isMaintenanceMode", isActive);
        if (estimatedEndTime.HasValue)
            Preferences.Set("maintenanceEndTime", estimatedEndTime.Value.ToString("O"));
        UpdateServerStatusUI();

        if (isActive)
        {
            string display = string.IsNullOrWhiteSpace(message)
                ? "Hệ thống đang bảo trì. Không thể đặt xe mới."
                : message;
            if (estimatedEndTime.HasValue)
                display += $"\n\nDự kiến kết thúc: {estimatedEndTime:HH:mm dd/MM/yyyy}";
            await DisplayAlert("Thông báo hệ thống", display, "Đã hiểu");
        }
        else
        {
            await DisplayAlert("Hệ thống hoạt động trở lại", "Bạn có thể đặt xe bình thường.", "OK");
        }
    }

    private async void OnDatabaseStatusChanged(string region, bool isDegraded, string message)
    {
        // Cập nhật trạng thái hệ thống ngay lập tức bất kể vùng nào
        // (Trong thực tế 1 vùng sập thì vùng kia vẫn dùng được, nhưng ở đây ta ưu tiên hiển thị real-time)
        Preferences.Set("isReadOnly", isDegraded);

        MainThread.BeginInvokeOnMainThread(() => {
            UpdateServerStatusUI();

            if (isDegraded)
            {
                // Hiệu ứng chuyển sang chế độ failover
                ShowFailoverAnimation();
            }
            else
            {
                // Hiệu ứng phục hồi hệ thống
                ShowRecoveryAnimation();
            }
        });

        if (isDegraded)
        {
            await DisplayAlert("Sự cố máy chủ",
                $"{message}\n\nHệ thống đang chạy trên Database dự phòng (Chế độ chỉ đọc).", "Đã hiểu");
        }
        else
        {
            await DisplayAlert("Máy chủ đã khôi phục",
                "Hệ thống đã quay trở lại hoạt động bình thường trên Database chính.", "Tuyệt vời");
        }
    }

    // ─────────────────────────────────────────────
    //  GPS Tracking
    // ─────────────────────────────────────────────

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
                    Dispatcher.Dispatch(() =>
                    {
                        MapStatusLabel.Text = $"📍 {_pickupLat:F4}°, {_pickupLon:F4}°";
                        CurrentPickupSubLabel.Text = $"{_pickupLat:F4}°, {_pickupLon:F4}°";

                        // Đồng bộ vị trí lên bản đồ pickup nếu đang mở
                        if (MapPickupPanel.IsVisible)
                        {
                            string latStr = _pickupLat.ToString(CultureInfo.InvariantCulture);
                            string lonStr = _pickupLon.ToString(CultureInfo.InvariantCulture);
                            _ = PickupMapView.EvaluateJavaScriptAsync($"setCenter({latStr}, {lonStr})");
                        }
                    });
                }
                await Task.Delay(5000);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GPS Error: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────
    //  Panel Navigation
    // ─────────────────────────────────────────────

    private void ShowPanel(string panelName)
    {
        HomePanel.IsVisible            = panelName == "home";
        SearchPanel.IsVisible          = panelName == "search";
        MapPickupPanel.IsVisible       = panelName == "mapPickup";
        RouteVehiclePanel.IsVisible    = panelName == "vehicle";
        SearchingDriverPanel.IsVisible = panelName == "searching";
        ActiveTripPanel.IsVisible      = panelName == "active";

        SearchingIndicator.IsRunning = panelName == "searching";

        if (panelName == "search")
        {
            SearchResultsSection.IsVisible = false;
            PopularSection.IsVisible       = true;
            OnSearchPanelAppeared();
            DestinationEntry.Focus();
        }

        if (panelName == "mapPickup")
        {
            PickupMapView.Source = new HtmlWebViewSource
            {
                Html = MapHtmlService.GetPickupMapHtml(_pickupLat, _pickupLon)
            };
        }
        else if (panelName == "vehicle" && _dropoffLat != 0)
        {
            RouteMapView.Source = new HtmlWebViewSource
            {
                Html = MapHtmlService.GetRouteMapHtml(
                    _pickupLat, _pickupLon, _dropoffLat, _dropoffLon, _encodedPolyline)
            };
        }
        else if (panelName == "active" && _dropoffLat != 0)
        {
            ActiveTripMapView.Source = new HtmlWebViewSource
            {
                Html = MapHtmlService.GetActiveTripMapHtml(
                    _pickupLat, _pickupLon, _dropoffLat, _dropoffLon, _encodedPolyline)
            };
        }
    }

    // ─────────────────────────────────────────────
    //  HOME PANEL handlers
    // ─────────────────────────────────────────────

    private void OnSearchBarTapped(object sender, TappedEventArgs e) => ShowPanel("search");

    private void OnXeMayClicked(object sender, TappedEventArgs e)
    {
        if (Preferences.Get("isMaintenanceMode", false))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await DisplayAlert("Không thể đặt xe",
                    "Hệ thống đang bảo trì.\nBạn chỉ có thể xem lịch sử chuyến đi và thông tin cá nhân.", "OK"));
            return;
        }
        if (Preferences.Get("isReadOnly", false))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await DisplayAlert("Không thể đặt xe",
                    "Hệ thống đang ở chế độ dự phòng.\nChỉ có thể xem lịch sử chuyến đi.", "OK"));
            return;
        }
        _selectedServiceType = "Xe máy";
        ShowPanel("search");
    }

    private void OnOtoClicked(object sender, TappedEventArgs e)
    {
        if (Preferences.Get("isMaintenanceMode", false))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await DisplayAlert("Không thể đặt xe",
                    "Hệ thống đang bảo trì.\nBạn chỉ có thể xem lịch sử chuyến đi và thông tin cá nhân.", "OK"));
            return;
        }
        if (Preferences.Get("isReadOnly", false))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await DisplayAlert("Không thể đặt xe",
                    "Hệ thống đang ở chế độ dự phòng.\nChỉ có thể xem lịch sử chuyến đi.", "OK"));
            return;
        }
        _selectedServiceType = "Ô tô 4 chỗ";
        ShowPanel("search");
    }

    private async void OnDatXeTruocClicked(object sender, TappedEventArgs e)
    {
        await DisplayAlert("Thông báo", "Tính năng đặt xe trước đang được phát triển.", "OK");
    }

    // ─────────────────────────────────────────────
    //  SEARCH PANEL handlers
    // ─────────────────────────────────────────────

    private void OnBackFromSearch(object sender, EventArgs e)
    {
        DestinationEntry.Text = "";
        ShowPanel("home");
    }

    private async void OnSearchPanelAppeared()
    {
        // Load địa điểm phổ biến khi mở trang tìm kiếm
        var popular = await _apiService.GetPopularLocationsAsync();
        var suggestions = popular.Select(SearchSuggestion.FromDb).ToList();

        PopularLocationsList.ItemsSource = suggestions;
        PopularSection.IsVisible = suggestions.Count > 0;
    }

    private async void OnDestinationTextChanged(object sender, TextChangedEventArgs e)
    {
        string query = e.NewTextValue?.Trim() ?? "";

        if (string.IsNullOrEmpty(query))
        {
            // Quay lại hiển thị địa điểm phổ biến
            SearchResultsSection.IsVisible = false;
            PopularSection.IsVisible       = PopularLocationsList.ItemsSource != null;
            return;
        }

        PopularSection.IsVisible       = false;
        SearchResultsSection.IsVisible = true;

        _destAutocompleteCts?.Cancel();
        _destAutocompleteCts = new CancellationTokenSource();
        var token = _destAutocompleteCts.Token;

        try
        {
            await Task.Delay(350, token);
            if (token.IsCancellationRequested) return;

            // Tìm kiếm song song: DB và Google
            var dbTask     = _apiService.SearchLocationsAsync(query);
            var googleTask = _mapsService.GetAutocompleteAsync(query, _pickupLat, _pickupLon);
            await Task.WhenAll(dbTask, googleTask);

            if (token.IsCancellationRequested) return;

            var dbResults     = dbTask.Result.Select(SearchSuggestion.FromDb).ToList();
            var googleResults = googleTask.Result
                                    .Select(p => SearchSuggestion.FromGoogle(p.DisplayText))
                                    .ToList();

            // Hiển thị DB results
            DbResultsHeader.IsVisible      = dbResults.Count > 0;
            DbLocationsList.ItemsSource    = dbResults;

            // Hiển thị Google results
            GoogleResultsHeader.IsVisible          = googleResults.Count > 0;
            DestinationAutocompleteList.ItemsSource = googleResults;
        }
        catch (TaskCanceledException) { }
    }

    private void OnDestinationSuggestionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchSuggestion suggestion) return;

        _destinationAddress = suggestion.DisplayText;
        DestinationEntry.Text = suggestion.DisplayText;

        // Reset selection
        if (sender is CollectionView cv) cv.SelectedItem = null;
        PopularLocationsList.SelectedItem = null;
        DbLocationsList.SelectedItem = null;
        DestinationAutocompleteList.SelectedItem = null;

        // Nếu có tọa độ từ DB → dùng luôn
        if (suggestion.Latitude.HasValue)
        {
            _dropoffLat = suggestion.Latitude.Value;
            _dropoffLon = suggestion.Longitude!.Value;
        }

        // Chuyển sang màn hình chọn điểm đón
        _pickupAddress = string.IsNullOrWhiteSpace(PickupEntry.Text)
            ? "Vị trí hiện tại"
            : PickupEntry.Text.Trim();
        CurrentPickupLabel.Text    = _pickupAddress;
        CurrentPickupSubLabel.Text = $"{_pickupLat:F4}°, {_pickupLon:F4}°";

        ShowPanel("mapPickup");
    }

    // ─────────────────────────────────────────────
    //  MAP PICKUP PANEL handlers
    // ─────────────────────────────────────────────

    private void OnBackFromMapPickup(object sender, EventArgs e) => ShowPanel("search");

    private async void OnPickupTextChanged(object sender, TextChangedEventArgs e)
    {
        _pickupAutocompleteCts?.Cancel();
        _pickupAutocompleteCts = new CancellationTokenSource();
        var token = _pickupAutocompleteCts.Token;

        try
        {
            await Task.Delay(400, token);
            var suggestions = await _mapsService.GetAutocompleteAsync(e.NewTextValue ?? "", _pickupLat, _pickupLon);
            if (token.IsCancellationRequested) return;

            PickupAutocompleteList.ItemsSource = suggestions;
            PickupAutocompleteContainer.IsVisible = suggestions.Count > 0;
        }
        catch (TaskCanceledException) { }
    }

    private void OnPickupSuggestionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PlaceSuggestion suggestion) return;
        PickupEntry.Text = suggestion.DisplayText;
        _pickupAddress = suggestion.DisplayText;
        CurrentPickupLabel.Text = _pickupAddress;
        PickupAutocompleteContainer.IsVisible = false;
        PickupAutocompleteList.SelectedItem   = null;
    }

    private async void OnConfirmPickupClicked(object sender, EventArgs e)
    {
        _pickupAddress = string.IsNullOrWhiteSpace(PickupEntry.Text)
            ? "Vị trí hiện tại"
            : PickupEntry.Text.Trim();

        // Đọc tọa độ tâm bản đồ từ WebView
        try
        {
            string? centerStr = await PickupMapView.EvaluateJavaScriptAsync("getCenter()");
            if (!string.IsNullOrEmpty(centerStr))
            {
                var parts = centerStr.Trim('"').Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double mapLat)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double mapLon))
                {
                    _pickupLat = mapLat;
                    _pickupLon = mapLon;
                }
            }
        }
        catch { }

        // Tính tuyến đường
        try
        {
            var routeResult = await _mapsService.GetRouteAsync(_pickupAddress, _destinationAddress);

            if (routeResult.IsSuccess)
            {
                _estimatedDistanceKm = routeResult.DistanceKm;
                _encodedPolyline = routeResult.EncodedPolyline;
                RouteInfoLabel.Text = $"📏 {_estimatedDistanceKm:F1} km  •  ⏱ ~{routeResult.DurationMinutes} phút";

                if (routeResult.DecodedPoints.Count > 0)
                {
                    _dropoffLat = routeResult.DecodedPoints[^1].Lat;
                    _dropoffLon = routeResult.DecodedPoints[^1].Lon;
                }
            }
            else
            {
                int seed = Math.Abs((_pickupAddress + _destinationAddress).GetHashCode());
                _estimatedDistanceKm = 3.0 + (seed % 120) / 10.0;
                int travelMin = Math.Max(5, (int)(_estimatedDistanceKm / 30.0 * 60));
                RouteInfoLabel.Text = $"📏 ~{_estimatedDistanceKm:F1} km  •  ⏱ ~{travelMin} phút";
            }
        }
        catch
        {
            _estimatedDistanceKm = 5.0;
            RouteInfoLabel.Text = "📏 ~5 km  •  ⏱ ~15 phút";
        }

        // Đảm bảo có tọa độ đích (fallback) để có thể hiển thị Map
        if (_dropoffLat == 0)
        {
            _dropoffLat = _pickupLat + 0.02;
            _dropoffLon = _pickupLon + 0.02;
        }

        // Cập nhật nhãn tuyến đường
        RoutePickupLabel.Text  = _pickupAddress;
        RouteDropoffLabel.Text = _destinationAddress;

        // Cập nhật giá từng loại xe
        UpdateVehicleOption(MotoPriceLabel, MotoEtaLabel, Moto);
        UpdateVehicleOption(Car4PriceLabel, Car4EtaLabel, Car4);
        UpdateVehicleOption(Car7PriceLabel, Car7EtaLabel, Car7);

        // Pre-select xe theo loại đã chọn từ home
        SelectVehicle(_selectedServiceType.StartsWith("Ô tô 7") ? "Ô tô 7 chỗ"
                    : _selectedServiceType.StartsWith("Ô tô")   ? "Ô tô 4 chỗ"
                    : "Xe máy");

        ShowPanel("vehicle");
    }

    private void UpdateVehicleOption(Label priceLabel, Label etaLabel,
        (string Type, string Emoji, decimal BaseRate, int EtaMin) v)
    {
        decimal fare = Math.Round((10_000m + v.BaseRate * (decimal)_estimatedDistanceKm) / 1000m) * 1000m;
        int etaMin   = v.EtaMin + (int)(_estimatedDistanceKm / 3);
        priceLabel.Text = $"{fare:#,##0}đ";
        etaLabel.Text   = $"Đón trong {etaMin} phút";
    }

    // ─────────────────────────────────────────────
    //  VEHICLE PANEL handlers
    // ─────────────────────────────────────────────

    private void OnBackFromVehicle(object sender, EventArgs e) => ShowPanel("mapPickup");

    private void SelectVehicle(string vehicleType)
    {
        _selectedVehicleType = vehicleType;

        var selStroke = new SolidColorBrush(MauiColor.FromArgb("#00B14F"));
        var selBg     = MauiColor.FromArgb("#F0FFF5");
        var defStroke = new SolidColorBrush(MauiColor.FromArgb("#E8E8E8"));
        var defBg     = MauiColor.FromArgb("#FFFFFF");

        MotoVehicleBorder.Stroke          = vehicleType == "Xe máy"     ? selStroke : defStroke;
        MotoVehicleBorder.BackgroundColor = vehicleType == "Xe máy"     ? selBg     : defBg;
        Car4VehicleBorder.Stroke          = vehicleType == "Ô tô 4 chỗ" ? selStroke : defStroke;
        Car4VehicleBorder.BackgroundColor = vehicleType == "Ô tô 4 chỗ" ? selBg     : defBg;
        Car7VehicleBorder.Stroke          = vehicleType == "Ô tô 7 chỗ" ? selStroke : defStroke;
        Car7VehicleBorder.BackgroundColor = vehicleType == "Ô tô 7 chỗ" ? selBg     : defBg;

        var v = vehicleType switch
        {
            "Ô tô 4 chỗ" => Car4,
            "Ô tô 7 chỗ" => Car7,
            _             => Moto
        };
        _selectedFare = Math.Round((10_000m + v.BaseRate * (decimal)_estimatedDistanceKm) / 1000m) * 1000m;
    }

    private void OnMotoSelected(object sender, TappedEventArgs e) => SelectVehicle("Xe máy");
    private void OnCar4Selected(object sender, TappedEventArgs e) => SelectVehicle("Ô tô 4 chỗ");
    private void OnCar7Selected(object sender, TappedEventArgs e) => SelectVehicle("Ô tô 7 chỗ");

    private void OnCancelVehicleSelection(object sender, EventArgs e) => ShowPanel("mapPickup");

    private async void OnConfirmBookingClicked(object sender, EventArgs e)
    {
        if (Preferences.Get("isMaintenanceMode", false))
        {
            await DisplayAlert("Không thể đặt xe",
                "Hệ thống đang bảo trì. Vui lòng thử lại sau.", "OK");
            return;
        }

        if (Preferences.Get("isReadOnly", false))
        {
            await DisplayAlert("Không thể đặt xe",
                "Hệ thống đang ở chế độ dự phòng. Chỉ có thể xem thông tin, không thể đặt xe mới.", "OK");
            return;
        }

        int userId = Preferences.Get("userID", 0);
        string? token = _apiService.GetToken();

        if (userId == 0 || string.IsNullOrEmpty(token))
        {
            // Token mất (ví dụ sau rebuild app) → redirect đăng nhập lại
            bool goLogin = await DisplayAlert("Phiên đăng nhập hết hạn",
                "Vui lòng đăng nhập lại để tiếp tục.", "Đăng nhập", "Hủy");
            if (goLogin)
                Application.Current!.MainPage = new NavigationPage(new LoginPage());
            return;
        }

        ConfirmBookingButton.IsEnabled = false;

        var result = await _apiService.BookTripAsync(userId, _pickupAddress, _destinationAddress, _selectedVehicleType, _estimatedDistanceKm);

        if (result.IsSuccess && result.Data != null)
        {
            _activeTripId = result.Data.TripId.ToString();

            TripPickupLabel.Text      = _pickupAddress;
            TripDropoffLabel.Text     = _destinationAddress;
            TripStatusLabel.Text      = "● Đang tìm tài xế";
            TripStatusLabel.TextColor = MauiColor.FromArgb("#FFC107");
            DriverInfoCard.IsVisible  = false;
            DriverEtaBanner.IsVisible = false;

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
            SearchingStatusLabel.Text =
                $"Chuyến #{_activeTripId}  •  {_selectedVehicleType}  •  {_selectedFare:#,##0}đ";

            StartDriverSearchTimeout();
        }
        else if (result.IsReadOnlyMode)
        {
            Preferences.Set("isReadOnly", true);
            UpdateServerStatusUI();
            ShowPanel("home");
            ConfirmBookingButton.IsEnabled = true;
            await DisplayAlert("Không thể đặt xe",
                "Server chính đang bảo trì.\nHệ thống chuyển sang chế độ Read-Only.", "OK");
        }
        else
        {
            ShowPanel("vehicle");
            ConfirmBookingButton.IsEnabled = true;

            // 401 = token hết hạn → yêu cầu đăng nhập lại
            if (result.ErrorMessage?.Contains("401") == true)
            {
                bool goLogin = await DisplayAlert("Phiên đăng nhập hết hạn",
                    "Token đã hết hạn. Vui lòng đăng nhập lại.", "Đăng nhập", "Hủy");
                if (goLogin)
                    Application.Current!.MainPage = new NavigationPage(new LoginPage());
            }
            else
            {
                await DisplayAlert("Lỗi", result.ErrorMessage ?? "Không thể kết nối server.", "OK");
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Timeout tìm tài xế
    // ─────────────────────────────────────────────

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
                if (!SearchingDriverPanel.IsVisible) return;

                bool keepWaiting = await DisplayAlert(
                    "Chưa tìm thấy tài xế",
                    "Hệ thống chưa tìm được tài xế trong 90 giây.\nBạn muốn tiếp tục chờ hay hủy chuyến?",
                    "Tiếp tục chờ", "Hủy chuyến");

                if (keepWaiting)
                    StartDriverSearchTimeout();
                else
                    await CancelTripAsync();
            });
        });
    }

    // ─────────────────────────────────────────────
    //  Hủy cuốc
    // ─────────────────────────────────────────────

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

        _activeTripId         = null;
        _encodedPolyline      = "";
        _isPassengerInVehicle = false;
        ShowPanel("home");
        ConfirmBookingButton.IsEnabled = true;
    }

    // ─────────────────────────────────────────────
    //  SignalR: Cập nhật vị trí tài xế
    // ─────────────────────────────────────────────

    private void OnDriverLocationUpdated(double lat, double lon)
    {
        Dispatcher.Dispatch(() =>
        {
            double targetLat = _isPassengerInVehicle ? _dropoffLat : _pickupLat;
            double targetLon = _isPassengerInVehicle ? _dropoffLon : _pickupLon;

            double dlat = (targetLat - lat) * Math.PI / 180.0;
            double dlon = (targetLon - lon) * Math.PI / 180.0;
            double a = Math.Sin(dlat / 2) * Math.Sin(dlat / 2)
                     + Math.Cos(lat * Math.PI / 180) * Math.Cos(targetLat * Math.PI / 180)
                       * Math.Sin(dlon / 2) * Math.Sin(dlon / 2);
            double distKm  = 6371 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            int etaMinutes = Math.Max(1, (int)(distKm / 30.0 * 60));

            DriverEtaLabel.Text = _isPassengerInVehicle
                ? $"Còn ~{distKm:F1} km  •  ~{etaMinutes} phút đến điểm đến"
                : $"Cách bạn ~{distKm:F1} km  •  đến trong ~{etaMinutes} phút";
            DriverEtaBanner.IsVisible = ActiveTripPanel.IsVisible;

            if (ActiveTripPanel.IsVisible)
            {
                string latStr = lat.ToString(CultureInfo.InvariantCulture);
                string lonStr = lon.ToString(CultureInfo.InvariantCulture);
                _ = ActiveTripMapView.EvaluateJavaScriptAsync($"updateDriver({latStr}, {lonStr})");
            }
        });
    }

    // ─────────────────────────────────────────────
    //  SignalR: Trạng thái chuyến đi
    // ─────────────────────────────────────────────

    private async void OnTripStatusChanged(string status, string message)
    {
        await Dispatcher.DispatchAsync(() =>
        {
            switch (status)
            {
                case "DriverAccepted":
                    _searchTimeoutCts?.Cancel();
                    TripStatusLabel.Text      = "● Tài xế đã nhận cuốc";
                    TripStatusLabel.TextColor = MauiColor.FromArgb("#00B14F");
                    DriverInfoCard.IsVisible  = true;
                    TripDriverName.Text    = "Tài xế";
                    TripDriverPlate.Text   = _selectedVehicleType;
                    TripDriverInitial.Text = "T";
                    ShowPanel("active");
                    break;

                case "DriverOnTheWay":
                    TripStatusLabel.Text      = "● Tài xế đang trên đường đến";
                    TripStatusLabel.TextColor = MauiColor.FromArgb("#2196F3");
                    DriverEtaBanner.IsVisible = true;
                    ShowPanel("active");
                    break;

                case "InProgress":
                    _isPassengerInVehicle     = true;
                    TripStatusLabel.Text      = "● Đang di chuyển đến điểm đến";
                    TripStatusLabel.TextColor = MauiColor.FromArgb("#00B14F");
                    DriverEtaBanner.IsVisible = true;
                    ShowPanel("active");
                    break;

                case "Completed":
                    _isPassengerInVehicle = false;
                    _searchTimeoutCts?.Cancel();
                    if (_activeTripId != null)
                        try { _hub.LeaveTripGroupAsync(_activeTripId).GetAwaiter().GetResult(); } catch { }
                    _activeTripId = null;
                    ShowPanel("home");
                    ConfirmBookingButton.IsEnabled = true;
                    DisplayAlert("Hoàn thành chuyến đi",
                        $"{message}\n\nTổng tiền: {_selectedFare:#,##0}đ", "OK");
                    break;

                case "CancelledByDriver":
                    DriverInfoCard.IsVisible  = false;
                    DriverEtaBanner.IsVisible = false;
                    ShowPanel("searching");
                    SearchingStatusLabel.Text = "Tài xế vừa hủy cuốc. Đang tìm tài xế khác...";
                    StartDriverSearchTimeout();
                    DisplayAlert("Tài xế đã hủy", "Hệ thống đang tìm tài xế khác cho bạn.", "OK");
                    break;
            }
        });
    }

    // ─────────────────────────────────────────────
    //  Mất kết nối mạng
    // ─────────────────────────────────────────────

    private void OnApiDatabaseStatusChanged(bool isDegraded)
    {
        MainThread.BeginInvokeOnMainThread(() => {
            UpdateServerStatusUI();

            if (isDegraded)
            {
                ShowFailoverAnimation();
            }
            else
            {
                ShowRecoveryAnimation();
            }
        });
    }

    // ─────────────────────────────────────────────
    //  Hiệu ứng chuyển trạng thái
    // ─────────────────────────────────────────────

    private async void ShowFailoverAnimation()
    {
        // Hiển thị banner failover với hiệu ứng fade in + pulse
        ReadOnlyBanner.Opacity = 0;
        ReadOnlyBanner.IsVisible = true;
        RecoveryBanner.IsVisible = false;

        // Fade in
        await ReadOnlyBanner.FadeTo(1, 300);

        // Pulse effect (3 lần)
        for (int i = 0; i < 3; i++)
        {
            await Task.WhenAll(
                ReadOnlyBanner.ScaleTo(1.05, 150),
                ReadOnlyBannerIcon.ScaleTo(1.3, 150)
            );
            await Task.WhenAll(
                ReadOnlyBanner.ScaleTo(1.0, 150),
                ReadOnlyBannerIcon.ScaleTo(1.0, 150)
            );
            await Task.Delay(100);
        }
    }

    private async void ShowRecoveryAnimation()
    {
        // Ẩn banner failover
        ReadOnlyBanner.IsVisible = false;

        // Hiển thị banner phục hồi với hiệu ứng slide down + fade in
        RecoveryBanner.Opacity = 0;
        RecoveryBanner.TranslationY = -30;
        RecoveryBanner.IsVisible = true;

        // Slide down + fade in
        await Task.WhenAll(
            RecoveryBanner.FadeTo(1, 400),
            RecoveryBanner.TranslateTo(0, 0, 400, Easing.CubicOut)
        );

        // Giữ banner 5 giây rồi tự động ẩn
        await Task.Delay(5000);

        // Fade out
        await RecoveryBanner.FadeTo(0, 300);
        RecoveryBanner.IsVisible = false;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        bool hasInternet = e.NetworkAccess == NetworkAccess.Internet;
        Dispatcher.Dispatch(() =>
        {
            SearchNetworkWarning.IsVisible = !hasInternet && SearchingDriverPanel.IsVisible;
            ActiveNetworkWarning.IsVisible = !hasInternet && ActiveTripPanel.IsVisible;
        });
    }

    // ─────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateServerStatusUI();
        _ = ConnectHubAsync();
    }

    private async Task ConnectHubAsync()
    {
        try { await _hub.StartAsync(); }
        catch { /* silently ignore — hub will retry on reconnect */ }

        // Sync trạng thái thực từ server mỗi lần vào app
        // (tránh hiển thị banner stale từ Preferences cũ)
        await _apiService.CheckAndSetMaintenanceAsync();
        await _apiService.CheckAndSetReadOnlyAsync();
        UpdateServerStatusUI();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _isTracking = false;
        _gpsCts?.Cancel();
        _searchTimeoutCts?.Cancel();
        Connectivity.ConnectivityChanged     -= OnConnectivityChanged;
        _hub.MaintenanceModeChanged          -= OnMaintenanceModeChanged;
        _hub.DatabaseStatusChanged           -= OnDatabaseStatusChanged;
    }
}
