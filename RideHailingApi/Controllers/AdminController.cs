using Microsoft.AspNetCore.Mvc;
using RideHailingApi.Data;
using RideHailingApi.Services;

namespace RideHailingApi.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly DataConnect       _db;
        private readonly FailoverSimulator _failover;

        public AdminController(DataConnect db, FailoverSimulator failover)
        {
            _db       = db;
            _failover = failover;
        }

        // GET /api/admin/status — trạng thái tất cả server + event log
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var regions = new[] { "South", "North" };
            var servers = regions.Select(r => new
            {
                Region          = r,
                PrimarySimulated = _failover.IsPrimaryDown(r),
                PrimaryReal     = _db.IsPrimaryAlive(r),
                ReplicaReal     = _db.IsReplicaAlive(r)
            });
            return Ok(new { servers, logs = _failover.GetLogs() });
        }

        // POST /api/admin/simulate-down/{region} — giả lập Primary sập
        [HttpPost("simulate-down/{region}")]
        public IActionResult SimulateDown(string region)
        {
            _failover.SetPrimaryDown(region);
            return Ok(new { message = $"Primary [{region}] đã được giả lập SẬP. App chuyển sang Replica." });
        }

        // POST /api/admin/simulate-up/{region} — khôi phục Primary
        [HttpPost("simulate-up/{region}")]
        public IActionResult SimulateUp(string region)
        {
            _failover.SetPrimaryUp(region);
            return Ok(new { message = $"Primary [{region}] đã được khôi phục. App trở lại bình thường." });
        }

        // POST /api/admin/test-write/{region} — thử ghi vào Primary
        // Sẽ thất bại (503) khi Primary đang giả lập sập
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

        // GET /api/admin/test-read/{region} — thử đọc (tự fallover sang Replica nếu Primary sập)
        [HttpGet("test-read/{region}")]
        public IActionResult TestRead(string region)
        {
            try
            {
                var table = _db.ExecuteReader(region,
                    "SELECT TOP 5 TripID, PickupLocation, DropoffLocation, Status, CreatedAt " +
                    "FROM Trips ORDER BY CreatedAt DESC");

                bool fromReplica = _failover.IsPrimaryDown(region);
                string source    = fromReplica ? "Replica" : "Primary";

                var rows = table.Rows.Cast<System.Data.DataRow>().Select(r => new
                {
                    TripID          = (int)r["TripID"],
                    PickupLocation  = r["PickupLocation"].ToString(),
                    DropoffLocation = r["DropoffLocation"].ToString(),
                    Status          = r["Status"].ToString(),
                    CreatedAt       = r["CreatedAt"] is DBNull ? "—" : ((DateTime)r["CreatedAt"]).ToString("dd/MM HH:mm")
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
