using System.Collections.ObjectModel;
using RideHailingApp.Services;

namespace RideHailingApp;

public partial class DriverHomePage : ContentPage
{
    private readonly ApiService _apiService;
    private readonly TripHubService _hub;
    private readonly GeoLocatorService _geo;

    private bool _isOnline = false;
    private int _activeTripId = 0;
    private CancellationTokenSource? _gpsLoopCts;

    public ObservableCollection<PendingTripItem> TripRequests { get; } = new();

    public DriverHomePage()
    {
        InitializeComponent();
        BindingContext = this;

        _apiService = MauiProgram.Services.GetRequiredService<ApiService>();
        _hub        = MauiProgram.Services.GetRequiredService<TripHubService>();
        _geo        = MauiProgram.Services.GetRequiredService<GeoLocatorService>();

        // Set driver name from preferences
        string name = Preferences.Get("userName", "Tài xế");
        DriverNameLabel.Text = name;
        AvatarLabel.Text = name.Length > 0 ? name[0].ToString().ToUpper() : "T";

        // Subscribe to hub events
        _hub.NewTripRequest += OnNewTripRequest;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _hub.NewTripRequest -= OnNewTripRequest;
    }

    // ───────────────── Online / Offline Toggle ─────────────────

    private async void OnGoOnlineClicked(object sender, EventArgs e)
    {
        if (!_isOnline)
            await GoOnlineAsync();
        else
            await GoOfflineAsync();
    }

    private async Task GoOnlineAsync()
    {
        try
        {
            HideError();
            OnlineToggleButton.IsEnabled = false;
            OnlineToggleButton.Text = "Đang kết nối...";

            await _hub.StartAsync();

            string region = _geo.GetCachedRegion();
            await _hub.JoinDriverPoolAsync(region);

            _isOnline = true;
            UpdateOnlineUI(true);

            // Load pending trips immediately
            await RefreshPendingTripsAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Không thể kết nối: {ex.Message}");
            OnlineToggleButton.IsEnabled = true;
            OnlineToggleButton.Text = "🚦 Bắt đầu nhận cuốc xe";
        }
    }

    private async Task GoOfflineAsync()
    {
        try
        {
            string region = _geo.GetCachedRegion();
            await _hub.LeaveDriverPoolAsync(region);
            await _hub.StopAsync();

            _isOnline = false;
            TripRequests.Clear();
            UpdateOnlineUI(false);
        }
        catch (Exception ex)
        {
            ShowError($"Lỗi khi ngắt kết nối: {ex.Message}");
        }
    }

    private void UpdateOnlineUI(bool online)
    {
        OnlineToggleButton.IsEnabled = true;
        if (online)
        {
            OnlineToggleButton.Text = "⛔ Dừng nhận cuốc xe";
            OnlineToggleButton.BackgroundColor = Color.FromArgb("#C62828");
            StatusLabel.Text = "● Online";
            StatusLabel.TextColor = Color.FromArgb("#00C853");
            StatusBadge.BackgroundColor = Color.FromArgb("#1A2A1A");
            StatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#00C853"));
            StatusInfoLabel.Text = "Online";
            StatusInfoLabel.TextColor = Color.FromArgb("#00C853");
        }
        else
        {
            OnlineToggleButton.Text = "🚦 Bắt đầu nhận cuốc xe";
            OnlineToggleButton.BackgroundColor = Color.FromArgb("#00C853");
            StatusLabel.Text = "● Offline";
            StatusLabel.TextColor = Color.FromArgb("#4A5068");
            StatusBadge.BackgroundColor = Color.FromArgb("#13161F");
            StatusBadge.Stroke = new SolidColorBrush(Color.FromArgb("#2E3348"));
            StatusInfoLabel.Text = "Offline";
            StatusInfoLabel.TextColor = Color.FromArgb("#8A8FA3");
        }
    }

    // ───────────────── Pending Trips ─────────────────

    private async Task RefreshPendingTripsAsync()
    {
        string region = _geo.GetCachedRegion();
        var result = await _apiService.GetPendingTripsAsync(region);
        if (result.IsSuccess && result.Data != null)
        {
            TripRequests.Clear();
            foreach (var trip in result.Data)
                TripRequests.Add(trip);

            PendingCountLabel.Text = TripRequests.Count.ToString();
        }
    }

    // SignalR push: new trip arrived in pool
    private void OnNewTripRequest(int tripId, string pickup, string dropoff)
    {
        // Avoid duplicates
        if (TripRequests.Any(t => t.TripID == tripId)) return;

        TripRequests.Insert(0, new PendingTripItem
        {
            TripID          = tripId,
            PickupLocation  = pickup,
            DropoffLocation = dropoff,
            Region          = _geo.GetCachedRegion()
        });
        PendingCountLabel.Text = TripRequests.Count.ToString();
    }

    // ───────────────── Accept Trip ─────────────────

    private async void OnAcceptTripClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.CommandParameter is not int tripId) return;

        btn.IsEnabled = false;
        btn.Text = "...";

        var result = await _apiService.AcceptTripAsync(tripId);

        if (result.IsSuccess)
        {
            _activeTripId = tripId;

            // Remove from list
            var item = TripRequests.FirstOrDefault(t => t.TripID == tripId);
            string pickup  = item?.PickupLocation  ?? "";
            string dropoff = item?.DropoffLocation ?? "";
            if (item != null) TripRequests.Remove(item);
            PendingCountLabel.Text = TripRequests.Count.ToString();

            // Join the trip SignalR group for GPS push
            await _hub.JoinTripGroupAsync(tripId.ToString());

            // Show active trip card
            ActiveTripInfoLabel.Text = $"#{tripId}  {pickup}  →  {dropoff}";
            ActiveTripCard.IsVisible = true;

            // Start GPS loop
            _gpsLoopCts = new CancellationTokenSource();
            _ = StartGpsLoopAsync(tripId.ToString(), _gpsLoopCts.Token);

            await DisplayAlert("Đã nhận cuốc", $"Chuyến #{tripId} đã được nhận!", "OK");
        }
        else if (result.ErrorMessage?.Contains("409") == true || result.ErrorMessage?.Contains("Conflict") == true)
        {
            await DisplayAlert("Thông báo", "Cuốc này đã được tài xế khác nhận.", "OK");
            var item = TripRequests.FirstOrDefault(t => t.TripID == tripId);
            if (item != null) TripRequests.Remove(item);
            PendingCountLabel.Text = TripRequests.Count.ToString();
        }
        else
        {
            await DisplayAlert("Lỗi", result.ErrorMessage ?? "Không thể nhận cuốc.", "OK");
            btn.IsEnabled = true;
            btn.Text = "Nhận";
        }
    }

    // ───────────────── GPS Loop ─────────────────

    private async Task StartGpsLoopAsync(string tripId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _activeTripId != 0)
        {
            try
            {
                var location = await Geolocation.GetLastKnownLocationAsync();
                if (location != null)
                    await _hub.UpdateDriverLocationAsync(tripId, location.Latitude, location.Longitude);
            }
            catch { /* GPS unavailable — skip silently */ }

            await Task.Delay(3000, ct).ContinueWith(_ => { }, TaskContinuationOptions.None);
        }
    }

    // ───────────────── Arrive ─────────────────

    private async void OnArriveClicked(object sender, EventArgs e)
    {
        if (_activeTripId == 0) return;

        var result = await _apiService.ArriveTripAsync(_activeTripId);
        if (result.IsSuccess)
            await DisplayAlert("Đã đến nơi", "Khách hàng đã được thông báo!", "OK");
        else
            await DisplayAlert("Lỗi", result.ErrorMessage ?? "Không thể cập nhật.", "OK");
    }

    // ───────────────── Complete ─────────────────

    private async void OnCompleteClicked(object sender, EventArgs e)
    {
        if (_activeTripId == 0) return;

        var result = await _apiService.CompleteTripAsync(_activeTripId);
        if (result.IsSuccess)
        {
            // Stop GPS loop
            _gpsLoopCts?.Cancel();
            _gpsLoopCts = null;

            await _hub.LeaveTripGroupAsync(_activeTripId.ToString());

            _activeTripId = 0;
            ActiveTripCard.IsVisible = false;
            ActiveTripInfoLabel.Text = "";

            await DisplayAlert("Hoàn thành", "Chuyến đi đã kết thúc. Cảm ơn bạn!", "OK");
        }
        else
        {
            await DisplayAlert("Lỗi", result.ErrorMessage ?? "Không thể hoàn thành chuyến.", "OK");
        }
    }

    // ───────────────── Helpers ─────────────────

    private void ShowError(string msg)
    {
        ErrorLabel.Text = msg;
        ErrorBanner.IsVisible = true;
    }

    private void HideError() => ErrorBanner.IsVisible = false;
}
