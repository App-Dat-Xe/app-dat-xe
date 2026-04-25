using Microsoft.AspNetCore.SignalR.Client;

namespace RideHailingApp.Services
{
    public class TripHubService : IAsyncDisposable
    {
        private readonly ApiService _apiService;
        private HubConnection? _connection;

        public event Action<double, double>? LocationUpdated;
        public event Action<string, string>? TripStatusChanged;
        public event Action<int, string, string>? NewTripRequest;  // tripId, pickup, dropoff

        public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

        public TripHubService(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task StartAsync()
        {
            if (_connection?.State == HubConnectionState.Connected) return;

            string baseUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://192.168.1.45:5108"
                : "https://localhost:7285";

            _connection = new HubConnectionBuilder()
                .WithUrl($"{baseUrl}/hubs/trip", options =>
                {
                    // MAUI gửi token qua query string để hỗ trợ WebSocket transport
                    options.AccessTokenProvider = () =>
                        Task.FromResult(_apiService.GetToken());
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<double, double>("OnLocationUpdated", (lat, lng) =>
            {
                MainThread.BeginInvokeOnMainThread(() => LocationUpdated?.Invoke(lat, lng));
            });

            _connection.On<string, string>("OnTripStatusChanged", (status, message) =>
            {
                MainThread.BeginInvokeOnMainThread(() => TripStatusChanged?.Invoke(status, message));
            });

            _connection.On<int, string, string>("OnNewTripRequest", (id, pickup, dropoff) =>
            {
                MainThread.BeginInvokeOnMainThread(() => NewTripRequest?.Invoke(id, pickup, dropoff));
            });

            await _connection.StartAsync();
        }

        public async Task JoinTripGroupAsync(string tripId)
        {
            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync("JoinTripGroup", tripId);
        }

        public async Task UpdateDriverLocationAsync(string tripId, double lat, double lng)
        {
            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync("UpdateDriverLocation", tripId, lat, lng);
        }

        public async Task LeaveTripGroupAsync(string tripId)
        {
            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync("LeaveTripGroup", tripId);
        }

        public async Task JoinDriverPoolAsync(string region)
        {
            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync("JoinDriverPool", region);
        }

        public async Task LeaveDriverPoolAsync(string region)
        {
            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync("LeaveDriverPool", region);
        }

        public async Task StopAsync()
        {
            if (_connection != null)
                await _connection.StopAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
                await _connection.DisposeAsync();
        }
    }
}
