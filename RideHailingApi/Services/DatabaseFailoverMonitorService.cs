using Microsoft.AspNetCore.SignalR;
using RideHailingApi.Hubs;

namespace RideHailingApi.Services
{
    public class DatabaseFailoverMonitorService : BackgroundService
    {
        private static readonly string[] Regions = { "South", "North" };

        private readonly IConfiguration        _config;
        private readonly DatabaseRuntimeState   _state;
        private readonly IDatabaseProbe         _probe;
        private readonly FailoverSimulator      _simulator;   // Giữ tích hợp với admin manual override
        private readonly ILogger<DatabaseFailoverMonitorService> _logger;
        private readonly IHubContext<TripHub> _hubContext;

        public DatabaseFailoverMonitorService(
            IConfiguration config,
            DatabaseRuntimeState state,
            IDatabaseProbe probe,
            FailoverSimulator simulator,
            ILogger<DatabaseFailoverMonitorService> logger,
            IHubContext<TripHub> hubContext)
        {
            _config    = config;
            _state     = state;
            _probe     = probe;
            _simulator = simulator;
            _logger    = logger;
            _hubContext = hubContext;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int intervalSec       = _config.GetValue("DatabaseFailover:HealthCheckIntervalSeconds", 10);
            int recoveryThreshold = _config.GetValue("DatabaseFailover:RecoverySuccessThreshold", 3);
            // By default do NOT perform automatic failover unless explicitly enabled in configuration.
            bool enableAutoFailoverGlobal = _config.GetValue("DatabaseFailover:EnableAutoFailover", false);
            _logger.LogInformation(
                "DatabaseFailoverMonitor started — interval={Interval}s, recoveryThreshold={Threshold}",
                intervalSec, recoveryThreshold);
            if (!enableAutoFailoverGlobal)
            {
                _logger.LogWarning("Automatic failover is DISABLED by configuration. Only admin manual override will trigger failover.");
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var region in Regions)
                {
                    await CheckRegionAsync(region, recoveryThreshold, stoppingToken, enableAutoFailoverGlobal);
                }
                await Task.Delay(TimeSpan.FromSeconds(intervalSec), stoppingToken);
            }
        }
        private async Task CheckRegionAsync(string region, int recoveryThreshold, CancellationToken ct, bool enableAutoFailover)
        {
            string primaryCs = _config.GetConnectionString($"{region}_Primary") ?? "";
            string backupCs  = _config.GetConnectionString($"{region}_Replica")  ?? "";

            // Admin manual override trumps real check
            bool manualDown    = _simulator.IsPrimaryDown(region);
            bool primaryAlive  = !manualDown && await _probe.CanConnectAsync(primaryCs, ct);
            bool backupAlive   = await _probe.CanConnectAsync(backupCs, ct);

            // Note: enableAutoFailover is passed from ExecuteAsync (global flag)
            bool statusChanged = false;
            bool isDegraded = false;
            string message = "";

            _state.Update(region, s =>
            {
                var oldTarget = s.CurrentTarget;
                var oldDegraded = s.IsDegradedMode;

                s.PrimaryHealthy   = primaryAlive;
                s.BackupHealthy    = backupAlive;
                s.ManualOverrideDown = manualDown;
                s.LastChecked      = DateTime.UtcNow;

                if (primaryAlive)
                    s.PrimaryRecoveryCount++;
                else
                    s.PrimaryRecoveryCount = 0;

                if (manualDown)
                {
                    // Admin explicitly requested failover -> switch to backup
                    s.CurrentTarget = DatabaseTarget.Backup;
                    s.IsDegradedMode = true;
                }
                else if (enableAutoFailover && !manualDown && !primaryAlive && backupAlive)
                {
                    // Automatic failover enabled and primary down -> switch to backup
                    s.CurrentTarget = DatabaseTarget.Backup;
                    s.IsDegradedMode = true;
                }
                else if (primaryAlive && s.PrimaryRecoveryCount >= recoveryThreshold)
                {
                    s.CurrentTarget  = DatabaseTarget.Primary;
                    s.IsDegradedMode = false;
                }
                else if (!primaryAlive && !backupAlive)
                {
                    s.CurrentTarget  = DatabaseTarget.None;
                    s.IsDegradedMode = true;
                }

                if (s.CurrentTarget != oldTarget || s.IsDegradedMode != oldDegraded)
                {
                    statusChanged = true;
                    isDegraded = s.IsDegradedMode;
                    message = isDegraded
                        ? $"Hệ thống vùng {region} đã chuyển sang chế độ dự phòng (Read-only)."
                        : $"Hệ thống vùng {region} đã khôi phục hoạt động bình thường.";

                    _logger.LogWarning("[{Region}] Status changed. Degraded={Degraded}, Target={Target}",
                        region, isDegraded, s.CurrentTarget);
                    _simulator.Append(region, message);
                }
            });

            if (statusChanged)
            {
                await _hubContext.Clients.Group("GlobalUsers")
                    .SendAsync("OnDatabaseStatusChanged", region, isDegraded, message, ct);
            }
        }
    }
}
