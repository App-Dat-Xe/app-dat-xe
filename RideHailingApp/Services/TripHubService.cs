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
        public event Action<string, string>? PoolingNotification;   // poolingType, message
        public event Action<int, string, string, string>? DriverAssigned; // tripId, name, plate, vehicle
        public event Action<int, string>? TripCancelled; // tripId, reason
        public event Action<bool, string, DateTime?>? MaintenanceModeChanged; // isActive, message, estimatedEndTime
        public event Action<string, bool, string>? DatabaseStatusChanged; // region, isDegraded, message

        public event Action<Exception?>? Reconnecting;
        public event Action<string?>? Reconnected;
        public event Action<Exception?>? Closed;

        public HubConnectionState State => _connection?.State ?? HubConnectionState.Disconnected;

        public TripHubService(ApiService apiService)
        {
            _apiService = apiService;
        }

        public async Task StartAsync()
        {
            if (_connection?.State == HubConnectionState.Connected) return;

            string baseUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://192.168.1.121:5108"
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
                MainThread.BeginInvokeOnMainThread(() => LocationUpdated?.Invoke(lat, lng)));

            _connection.On<string, string>("OnTripStatusChanged", (status, message) =>
                MainThread.BeginInvokeOnMainThread(() => TripStatusChanged?.Invoke(status, message)));

            _connection.On<int, string, string>("OnNewTripRequest", (id, pickup, dropoff) =>
                MainThread.BeginInvokeOnMainThread(() => NewTripRequest?.Invoke(id, pickup, dropoff)));

            _connection.On<int, string, string, string>("OnDriverAssigned", (tripId, name, plate, vehicle) =>
                MainThread.BeginInvokeOnMainThread(() => DriverAssigned?.Invoke(tripId, name, plate, vehicle)));

            _connection.On<int, string>("OnTripCancelled", (tripId, reason) =>
                MainThread.BeginInvokeOnMainThread(() => TripCancelled?.Invoke(tripId, reason)));

            _connection.Reconnecting += ex =>
            {
                MainThread.BeginInvokeOnMainThread(() => Reconnecting?.Invoke(ex));
                return Task.CompletedTask;
            };
            _connection.Reconnected += connId =>
            {
                MainThread.BeginInvokeOnMainThread(() => Reconnected?.Invoke(connId));
                return Task.CompletedTask;
            };
            _connection.Closed += ex =>
            {
                MainThread.BeginInvokeOnMainThread(() => Closed?.Invoke(ex));
                return Task.CompletedTask;
            };

            _connection.On<string, string>("OnPoolingNotification", (poolingType, message) =>
                MainThread.BeginInvokeOnMainThread(() => PoolingNotification?.Invoke(poolingType, message)));

            _connection.On<bool, string, DateTime?>("OnMaintenanceModeChanged", (isActive, message, estimatedEndTime) =>
                MainThread.BeginInvokeOnMainThread(() => MaintenanceModeChanged?.Invoke(isActive, message, estimatedEndTime)));

            _connection.On<string, bool, string>("OnDatabaseStatusChanged", (region, isDegraded, message) =>
                MainThread.BeginInvokeOnMainThread(() => DatabaseStatusChanged?.Invoke(region, isDegraded, message)));

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

        public async Task JoinUserGroupAsync(int userId)
        {
            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync("JoinUserGroup", userId.ToString());
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
