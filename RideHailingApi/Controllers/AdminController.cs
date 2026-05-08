using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RideHailingApi.Data;
using RideHailingApi.Hubs;
using RideHailingApi.Models;
using RideHailingApi.Services;

namespace RideHailingApi.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/admin")]
    [Route("api/admin")]
    [Authorize(Roles = "ADMIN")]
    public class AdminController : ControllerBase
    {
        private readonly DataConnect              _db;
        private readonly FailoverSimulator        _failover;
        private readonly DatabaseRuntimeState     _runtimeState;
        private readonly MaintenanceModeService   _maintenance;
        private readonly IHubContext<TripHub>     _hub;
        private static readonly string[]         _regions = { "North", "South" };

        public AdminController(
            DataConnect db,
            FailoverSimulator failover,
            DatabaseRuntimeState runtimeState,
            MaintenanceModeService maintenance,
            IHubContext<TripHub> hub)
        {
            _db           = db;
            _failover     = failover;
            _runtimeState = runtimeState;
            _maintenance  = maintenance;
            _hub          = hub;
        }

        // ── Maintenance Mode ───────────────────────────────────────────────────────

        // POST /api/admin/maintenance/on
        [HttpPost("maintenance/on")]
        public async Task<IActionResult> MaintenanceOn([FromBody] MaintenanceRequest? req)
        {
            string msg = req?.Message ?? "Hệ thống đang bảo trì. Vui lòng thử lại sau.";
            _maintenance.Activate(msg, req?.EstimatedEndTime);
            await _hub.Clients.Group("GlobalUsers")
                .SendAsync("OnMaintenanceModeChanged", true, msg, req?.EstimatedEndTime);
            return Ok(new
            {
                isActive = true,
                message = msg,
                estimatedEndTime = _maintenance.EstimatedEndTime
            });
        }

        // POST /api/admin/maintenance/off
        [HttpPost("maintenance/off")]
        public async Task<IActionResult> MaintenanceOff()
        {
            _maintenance.Deactivate();
            await _hub.Clients.Group("GlobalUsers")
                .SendAsync("OnMaintenanceModeChanged", false, "", null);
            return Ok(new { isActive = false });
        }

        // GET /api/admin/maintenance/status
        [HttpGet("maintenance/status")]
        public IActionResult MaintenanceStatus()
            => Ok(new
            {
                isActive = _maintenance.IsActive,
                message = _maintenance.Message,
                estimatedEndTime = _maintenance.EstimatedEndTime
            });

        // ── Analytics ─────────────────────────────────────────────────────────────

        // GET /api/admin/dashboard/kpis
        [HttpGet("dashboard/kpis")]
        public IActionResult GetKpis()
        {
            long    totalTrips = 0, totalUsers = 0, totalDrivers = 0, cancelledTrips = 0;
            decimal totalRevenue = 0;

            foreach (var region in _regions)
            {
                // Dùng DataConnect (tự động chuyển sang Replica nếu Primary sập)
                totalTrips    += Convert.ToInt64(_db.ExecuteScalar(region, "SELECT COUNT(*) FROM Trips")  ?? 0L);
                totalUsers    += Convert.ToInt64(_db.ExecuteScalar(region, "SELECT COUNT(*) FROM Users")  ?? 0L);
                totalDrivers  += Convert.ToInt64(_db.ExecuteScalar(region, "SELECT COUNT(*) FROM Drivers") ?? 0L);
                cancelledTrips += Convert.ToInt64(_db.ExecuteScalar(region,
                    "SELECT COUNT(*) FROM Trips WHERE Status='Cancelled'") ?? 0L);
                var rev = _db.ExecuteScalar(region, "SELECT ISNULL(SUM(Price),0) FROM Trips WHERE Status='Completed'");
                if (rev != null && rev != DBNull.Value) totalRevenue += Convert.ToDecimal(rev);
            }

            double cancelRate = totalTrips > 0 ? Math.Round((double)cancelledTrips / totalTrips * 100, 1) : 0;
            return Ok(new { totalTrips, totalUsers, totalDrivers, totalRevenue, cancelledTrips, cancelRate });
        }

        // GET /api/admin/dashboard/revenue?days=30
        [HttpGet("dashboard/revenue")]
        public IActionResult GetRevenue([FromQuery] int days = 30)
        {
            var rows = new List<object>();
            foreach (var region in _regions)
            {
                var table = _db.ExecuteReader(region,
                    "SELECT CONVERT(DATE, UpdatedAt) AS Day, COUNT(*) AS Trips, ISNULL(SUM(Price),0) AS Revenue " +
                    "FROM Trips WHERE Status='Completed' AND UpdatedAt >= DATEADD(DAY, -@d, GETDATE()) " +
                    "GROUP BY CONVERT(DATE, UpdatedAt) ORDER BY Day DESC",
                    cmd => cmd.Parameters.AddWithValue("@d", days));

                foreach (System.Data.DataRow row in table.Rows)
                {
                    rows.Add(new
                    {
                        region,
                        day     = ((DateTime)row["Day"]).ToString("yyyy-MM-dd"),
                        trips   = Convert.ToInt32(row["Trips"]),
                        revenue = Convert.ToDecimal(row["Revenue"])
                    });
                }
            }
            return Ok(rows);
        }

        // ── User Management ───────────────────────────────────────────────────────

        // GET /api/admin/users?search=&region=South&page=1&pageSize=20
        [HttpGet("users")]
        public IActionResult GetUsers(
            [FromQuery] string? search,
            [FromQuery] string region   = "South",
            [FromQuery] int    page     = 1,
            [FromQuery] int    pageSize = 20)
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            int offset = (page - 1) * pageSize;

            try
            {
                string whereClause = "(@s='' OR UserName LIKE '%'+@s+'%' OR FullName LIKE '%'+@s+'%')";

                long total = Convert.ToInt64(_db.ExecuteScalar(region,
                    $"SELECT COUNT(*) FROM Users WHERE {whereClause}",
                    cmd => cmd.Parameters.AddWithValue("@s", search ?? "")) ?? 0L);

                var table = _db.ExecuteReader(region,
                    $"SELECT UserID, UserName, FullName, Phone, RegisteredRegion, ISNULL(IsLocked,0) AS IsLocked " +
                    $"FROM Users WHERE {whereClause} " +
                    $"ORDER BY UserID DESC " +
                    $"OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@s",        search ?? "");
                        cmd.Parameters.AddWithValue("@offset",   offset);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);
                    });

                var users = table.Rows.Cast<System.Data.DataRow>().Select(r => new
                {
                    userId   = (int)r["UserID"],
                    userName = r["UserName"].ToString(),
                    fullName = r["FullName"].ToString(),
                    phone    = r["Phone"].ToString(),
                    region   = r["RegisteredRegion"].ToString(),
                    isLocked = Convert.ToBoolean(r["IsLocked"])
                }).ToList();

                return Ok(new { data = users, total, page, pageSize,
                    totalPages = (int)Math.Ceiling((double)total / pageSize) });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // PUT /api/admin/users/{id}/lock?region=South
        [HttpPut("users/{id:int}/lock")]
        public IActionResult LockUser(int id, [FromQuery] string region, [FromBody] LockRequest req)
        {
            try
            {
                _db.ExecuteNonQuery(region,
                    "UPDATE Users SET IsLocked=@locked WHERE UserID=@id",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@locked", req.IsLocked ? 1 : 0);
                        cmd.Parameters.AddWithValue("@id", id);
                    });
                return Ok(new { userId = id, isLocked = req.IsLocked });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // ── Driver Management ─────────────────────────────────────────────────────

        // GET /api/admin/drivers?search=&region=South&page=1&pageSize=20
        [HttpGet("drivers")]
        public IActionResult GetDrivers(
            [FromQuery] string? search,
            [FromQuery] string region   = "South",
            [FromQuery] int    page     = 1,
            [FromQuery] int    pageSize = 20)
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            int offset = (page - 1) * pageSize;

            try
            {
                string whereClause = "@s='' OR d.FullName LIKE '%'+@s+'%' OR d.Phone LIKE '%'+@s+'%'";

                long total = Convert.ToInt64(_db.ExecuteScalar(region,
                    $"SELECT COUNT(*) FROM Drivers d WHERE {whereClause}",
                    cmd => cmd.Parameters.AddWithValue("@s", search ?? "")) ?? 0L);

                var table = _db.ExecuteReader(region,
                    $"SELECT d.DriverID, d.FullName, d.Phone, ISNULL(d.IsLocked,0) AS IsLocked, " +
                    $"COUNT(t.TripID) AS TotalTrips, ISNULL(SUM(t.Price),0) AS TotalEarnings " +
                    $"FROM Drivers d LEFT JOIN Trips t ON d.DriverID = t.DriverID AND t.Status='Completed' " +
                    $"WHERE {whereClause} " +
                    $"GROUP BY d.DriverID, d.FullName, d.Phone, d.IsLocked " +
                    $"ORDER BY d.DriverID DESC " +
                    $"OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@s",        search ?? "");
                        cmd.Parameters.AddWithValue("@offset",   offset);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);
                    });

                var drivers = table.Rows.Cast<System.Data.DataRow>().Select(r => new
                {
                    driverId      = (int)r["DriverID"],
                    fullName      = r["FullName"].ToString(),
                    phone         = r["Phone"].ToString(),
                    isLocked      = Convert.ToBoolean(r["IsLocked"]),
                    totalTrips    = Convert.ToInt32(r["TotalTrips"]),
                    totalEarnings = Convert.ToDecimal(r["TotalEarnings"])
                }).ToList();

                return Ok(new { data = drivers, total, page, pageSize,
                    totalPages = (int)Math.Ceiling((double)total / pageSize) });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // PUT /api/admin/drivers/{id}/lock?region=South
        [HttpPut("drivers/{id:int}/lock")]
        public IActionResult LockDriver(int id, [FromQuery] string region, [FromBody] LockRequest req)
        {
            try
            {
                _db.ExecuteNonQuery(region,
                    "UPDATE Drivers SET IsLocked=@locked WHERE DriverID=@id",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@locked", req.IsLocked ? 1 : 0);
                        cmd.Parameters.AddWithValue("@id", id);
                    });
                return Ok(new { driverId = id, isLocked = req.IsLocked });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // GET /api/admin/trips?region=South&status=Completed&page=1&pageSize=20
        [HttpGet("trips")]
        public IActionResult GetTrips(
            [FromQuery] string  region   = "South",
            [FromQuery] string? status   = null,
            [FromQuery] int     page     = 1,
            [FromQuery] int     pageSize = 20)
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            int offset = (page - 1) * pageSize;

            try
            {
                string filter = string.IsNullOrEmpty(status) ? "" : "AND t.Status=@status";

                long total = Convert.ToInt64(_db.ExecuteScalar(region,
                    $"SELECT COUNT(*) FROM Trips t WHERE 1=1 {filter}",
                    cmd => { if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@status", status); }) ?? 0L);

                var table = _db.ExecuteReader(region,
                    $"SELECT t.TripID, t.UserID, t.DriverID, " +
                    $"COALESCE(t.PickupLocation, pl.LocationName, '') AS PickupLocation, " +
                    $"COALESCE(t.DropoffLocation, dl.LocationName, '') AS DropoffLocation, " +
                    $"ISNULL(t.VehicleType,'') AS VehicleType, ISNULL(t.Price,0) AS Fare, t.Status, t.UpdatedAt " +
                    $"FROM Trips t " +
                    $"LEFT JOIN Locations pl ON t.PickupLocationID = pl.LocationID " +
                    $"LEFT JOIN Locations dl ON t.DropoffLocationID = dl.LocationID " +
                    $"WHERE 1=1 {filter} " +
                    $"ORDER BY t.UpdatedAt DESC " +
                    $"OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY",
                    cmd =>
                    {
                        if (!string.IsNullOrEmpty(status)) cmd.Parameters.AddWithValue("@status", status);
                        cmd.Parameters.AddWithValue("@offset",   offset);
                        cmd.Parameters.AddWithValue("@pageSize", pageSize);
                    });

                var trips = table.Rows.Cast<System.Data.DataRow>().Select(r => new
                {
                    tripId      = (int)r["TripID"],
                    userId      = (int)r["UserID"],
                    driverId    = r["DriverID"] is DBNull ? null : (int?)Convert.ToInt32(r["DriverID"]),
                    pickup      = r["PickupLocation"].ToString(),
                    dropoff     = r["DropoffLocation"].ToString(),
                    vehicleType = r["VehicleType"].ToString(),
                    fare        = r["Fare"] is DBNull ? 0m : Convert.ToDecimal(r["Fare"]),
                    status      = r["Status"].ToString(),
                    createdAt   = r["UpdatedAt"] is DBNull ? null : ((DateTime?)r["UpdatedAt"])?.ToString("dd/MM/yyyy HH:mm")
                }).ToList();

                return Ok(new { data = trips, total, page, pageSize,
                    totalPages = (int)Math.Ceiling((double)total / pageSize) });
            }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        // GET /api/admin/status
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var allState = _runtimeState.GetAll();

            var servers = allState.Select(r =>
            {
                var s = r.State;
                return new
                {
                    region           = r.Region,
                    primaryReal      = s.PrimaryHealthy,
                    primarySimulated = s.ManualOverrideDown,
                    replicaReal      = s.BackupHealthy,
                    currentTarget    = s.CurrentTarget.ToString(),
                    isDegraded       = s.IsDegradedMode,
                    lastChecked      = s.LastChecked
                };
            }).ToList();

            var logs = _failover.GetLogs()
                .Select(l => new { time = l.Time, region = l.Region, message = l.Message })
                .ToList();

            return Ok(new { servers, logs });
        }

        // POST /api/admin/simulate-down/{region}
        [HttpPost("simulate-down/{region}")]
        public async Task<IActionResult> SimulateDown(string region)
        {
            _failover.SetPrimaryDown(region);

            // Broadcast tới tất cả app đang kết nối
            string message = $"Máy chủ {region} gặp sự cố";
            await _hub.Clients.Group("GlobalUsers")
                .SendAsync("OnDatabaseStatusChanged", region, true, message);

            return Ok(new { message = $"Primary [{region}] đã được giả lập SẬP. Tất cả app đã nhận thông báo chuyển sang Replica." });
        }

        // POST /api/admin/simulate-up/{region}
        [HttpPost("simulate-up/{region}")]
        public async Task<IActionResult> SimulateUp(string region)
        {
            _failover.SetPrimaryUp(region);

            // Broadcast tới tất cả app đang kết nối
            string message = $"Máy chủ {region} đã được khôi phục";
            await _hub.Clients.Group("GlobalUsers")
                .SendAsync("OnDatabaseStatusChanged", region, false, message);

            return Ok(new { message = $"Primary [{region}] đã được khôi phục. Tất cả app đã nhận thông báo trở lại bình thường." });
        }

        // POST /api/admin/reset-manual-overrides
        [HttpPost("reset-manual-overrides")]
        public IActionResult ResetManualOverrides()
        {
            var regions = new[] { "South", "North" };
            foreach (var r in regions)
            {
                _failover.SetPrimaryUp(r);
            }
            return Ok(new { message = "Đã khôi phục trạng thái hoạt động bình thường cho tất cả các vùng." });
        }

        // POST /api/admin/test-write/{region}
        [HttpPost("test-write/{region}")]
        public IActionResult TestWrite(string region)
        {
            try
            {
                _db.ExecuteNonQuery(region,
                    "INSERT INTO Trips (UserID, PickupLocation, DropoffLocation, Region, Status) " +
                    "VALUES (1, N'[Admin Test] Điểm đón', N'[Admin Test] Điểm đến', @r, 'Test')",
                    cmd => cmd.Parameters.AddWithValue("@r", region));

                _failover.Append(region, $"✅ GHI thành công vào Primary [{region}]");
                return Ok(new { success = true, source = "Primary",
                    message = $"Ghi thành công vào Primary [{region}]." });
            }
            catch (InvalidOperationException ex)
            {
                _failover.Append(region, $"❌ GHI THẤT BẠI [{region}] — Primary không khả dụng");
                return StatusCode(503, new { success = false, source = "—", message = ex.Message });
            }
            catch (Exception ex)
            {
                _failover.Append(region, $"❌ GHI LỖI [{region}]: {ex.Message}");
                return StatusCode(500, new { success = false, source = "—", message = ex.Message });
            }
        }

        // GET /api/admin/test-read/{region}
        [HttpGet("test-read/{region}")]
        public IActionResult TestRead(string region)
        {
            try
            {
                var table = _db.ExecuteReader(region,
                    "SELECT TOP 5 TripID, PickupLocation, DropoffLocation, Status, UpdatedAt " +
                    "FROM Trips ORDER BY UpdatedAt DESC");

                var target = _runtimeState.GetTarget(region);
                string source = target == DatabaseTarget.Primary ? "Primary" : "Replica";

                var rows = table.Rows.Cast<System.Data.DataRow>().Select(r => new
                {
                    TripID          = (int)r["TripID"],
                    PickupLocation  = r["PickupLocation"].ToString(),
                    DropoffLocation = r["DropoffLocation"].ToString(),
                    Status          = r["Status"].ToString(),
                    CreatedAt       = r["UpdatedAt"] is DBNull ? "—" : ((DateTime)r["UpdatedAt"]).ToString("dd/MM HH:mm")
                }).ToList();

                _failover.Append(region,
                    $"✅ ĐỌC thành công từ {source} [{region}] — {rows.Count} chuyến đi");

                return Ok(new { success = true, source, rowCount = rows.Count, data = rows });
            }
            catch (Exception ex)
            {
                _failover.Append(region, $"❌ ĐỌC THẤT BẠI [{region}]: {ex.Message}");
                return StatusCode(500, new { success = false, source = "—", message = ex.Message });
            }
        }
    }
}
