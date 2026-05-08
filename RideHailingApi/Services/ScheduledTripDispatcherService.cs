using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RideHailingApi.Data;
using RideHailingApi.Hubs;

namespace RideHailingApi.Services
{
    public class ScheduledTripDispatcherService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<TripHub> _hub;
        private readonly ILogger<ScheduledTripDispatcherService> _logger;

        public ScheduledTripDispatcherService(
            IServiceScopeFactory scopeFactory,
            IHubContext<TripHub> hub,
            ILogger<ScheduledTripDispatcherService> logger)
        {
            _scopeFactory = scopeFactory;
            _hub = hub;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ScheduledTripDispatcher started.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await DispatchDueTripsAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "ScheduledTripDispatcher error"); }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        private async Task DispatchDueTripsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<DataContext>();
            var db  = scope.ServiceProvider.GetRequiredService<DataConnect>();
            var fcm = scope.ServiceProvider.GetRequiredService<IFcmNotificationService>();

            // Trips due within the next 2 minutes that haven't been dispatched yet
            var dueTrips = await ctx.ScheduledTrips
                .Where(s => s.Status == "Scheduled"
                         && s.ScheduledPickupTime <= DateTime.UtcNow.AddMinutes(2))
                .ToListAsync();

            foreach (var scheduled in dueTrips)
            {
                try
                {
                    // Insert into Trips table in the correct region
                    var newId = db.ExecuteScalarWrite(scheduled.Region,
                        "INSERT INTO Trips (UserID, PickupLocation, DropoffLocation, Region, DistanceKm, Price, Status) " +
                        "VALUES (@uid, @pickup, @dropoff, @region, @dist, @price, 'Pending'); SELECT SCOPE_IDENTITY();",
                        cmd =>
                        {
                            cmd.Parameters.AddWithValue("@uid",    scheduled.UserId);
                            cmd.Parameters.AddWithValue("@pickup",  scheduled.PickupAddress);
                            cmd.Parameters.AddWithValue("@dropoff", scheduled.DropoffAddress);
                            cmd.Parameters.AddWithValue("@region",  scheduled.Region);
                            cmd.Parameters.AddWithValue("@dist",    (object?)scheduled.DistanceKm  ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@price",   (object?)scheduled.EstimatedFare ?? DBNull.Value);
                        });

                    int tripId = Convert.ToInt32(newId);

                    scheduled.Status    = "Dispatched";
                    scheduled.TripId    = tripId;
                    scheduled.UpdatedAt = DateTime.UtcNow;
                    await ctx.SaveChangesAsync();

                    // Notify driver pool
                    await _hub.Clients.Group($"DriverPool_{scheduled.Region}")
                        .SendAsync("OnNewTripRequest", tripId,
                            scheduled.PickupAddress, scheduled.DropoffAddress);

                    // Push notification to user
                    await fcm.SendToUserAsync(
                        scheduled.UserId,
                        "Chuyến xe đã được kích hoạt",
                        $"Chuyến lên lịch {scheduled.ScheduledPickupTime:HH:mm dd/MM} của bạn đang được đặt!");

                    _logger.LogInformation(
                        "Dispatched scheduled trip {ScheduledTripId} → Trip {TripId} (region={Region})",
                        scheduled.ScheduledTripId, tripId, scheduled.Region);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch ScheduledTripId={Id}", scheduled.ScheduledTripId);
                    // Mark as failed so the next run skips it
                    scheduled.Status    = "DispatchFailed";
                    scheduled.UpdatedAt = DateTime.UtcNow;
                    try { await ctx.SaveChangesAsync(); } catch { /* best-effort */ }
                }
            }
        }
    }
}
