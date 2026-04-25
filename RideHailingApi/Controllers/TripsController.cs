using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RideHailingApi.Data;
using RideHailingApi.Hubs;
using RideHailingApi.Middleware;
using RideHailingApi.Models;

namespace RideHailingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TripsController : ControllerBase
    {
        private readonly DataConnect _db;
        private readonly IHubContext<TripHub> _hub;

        public TripsController(DataConnect db, IHubContext<TripHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        // POST /api/trips/book-trip — Protected: yêu cầu JWT hợp lệ
        [Authorize]
        [HttpPost("book-trip")]
        public async Task<IActionResult> BookTrip([FromBody] TripRequest request)
        {
            string region = HttpContext.GetRegion();
            if (!string.IsNullOrWhiteSpace(request.Region))
                region = request.Region;

            try
            {
                var newId = _db.ExecuteScalarWrite(region,
                    "INSERT INTO Trips (UserID, PickupLocation, DropoffLocation, Region, Status) " +
                    "VALUES (@Uid, @Pick, @Drop, @Reg, 'Pending'); SELECT SCOPE_IDENTITY();",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@Uid", request.UserID);
                        cmd.Parameters.AddWithValue("@Pick", request.PickupLocation);
                        cmd.Parameters.AddWithValue("@Drop", request.DropoffLocation);
                        cmd.Parameters.AddWithValue("@Reg", region);
                    });

                int tripId = Convert.ToInt32(newId);

                // Đẩy trạng thái "Pending" ngay lập tức tới group chuyến đi
                await _hub.Clients.Group($"Trip_{tripId}")
                    .SendAsync("OnTripStatusChanged", "Pending", "Đang tìm tài xế cho bạn...");

                // Thông báo tới pool tài xế trong cùng khu vực
                await _hub.Clients.Group($"DriverPool_{region}")
                    .SendAsync("OnNewTripRequest", tripId, request.PickupLocation, request.DropoffLocation);

                return Ok(new { tripId, message = $"Đặt xe thành công tại Server Chính ({region})" });
            }
            catch (InvalidOperationException)
            {
                // Primary sập — DataConnect không cho ghi vào Replica
                return StatusCode(503, new
                {
                    error   = "Server Chính đang bảo trì.",
                    message = "Hệ thống đang ở chế độ Read-Only. Bạn chỉ có thể xem lịch sử, không thể đặt xe mới lúc này."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST /api/trips/{tripId}/notify-status — Tài xế báo trạng thái chuyến (Accepted/Arrived/Completed)
        [Authorize]
        [HttpPost("{tripId:int}/notify-status")]
        public async Task<IActionResult> NotifyTripStatus(int tripId, [FromBody] TripStatusRequest req)
        {
            await _hub.Clients.Group($"Trip_{tripId}")
                .SendAsync("OnTripStatusChanged", req.Status, req.Message);
            return Ok(new { sent = true });
        }

        // GET /api/trips/history/{userId} — lịch sử chuyến đi (có failover sang Replica)
        [HttpGet("history/{userId:int}")]
        public IActionResult GetHistory(int userId)
        {
            string region = HttpContext.GetRegion();
            try
            {
                var table = _db.ExecuteReader(region,
                    "SELECT TripID, UserID, DriverID, PickupLocation, DropoffLocation, Region, Status, CreatedAt " +
                    "FROM Trips WHERE UserID = @id ORDER BY CreatedAt DESC",
                    cmd => cmd.Parameters.AddWithValue("@id", userId));

                var trips = new List<TripHistoryItem>();
                foreach (System.Data.DataRow row in table.Rows)
                {
                    trips.Add(new TripHistoryItem
                    {
                        TripID          = (int)row["TripID"],
                        UserID          = (int)row["UserID"],
                        DriverID        = row["DriverID"] is DBNull ? null : (int?)row["DriverID"],
                        PickupLocation  = row["PickupLocation"].ToString() ?? "",
                        DropoffLocation = row["DropoffLocation"].ToString() ?? "",
                        Region          = row["Region"].ToString() ?? "",
                        Status          = row["Status"].ToString() ?? "",
                        CreatedAt       = row["CreatedAt"] is DBNull ? null : (DateTime?)row["CreatedAt"]
                    });
                }
                return Ok(trips);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/trips/test-connection/{region} — kiểm tra kết nối DB của 1 region
        [HttpGet("test-connection/{region}")]
        public IActionResult TestDBconnection(string region)
        {
            try
            {
                var serverName = _db.ExecuteScalar(region, "SELECT @@SERVERNAME")?.ToString();
                return Ok(new
                {
                    TrangThai  = "Kết nối thành công",
                    KhuVuc     = region,
                    ServerName = serverName,
                    LoiNhan    = "API của bạn đã đâm xuyên qua SQL Server rồi đó!"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    TrangThai = "Kết nối thất bại",
                    KhuVuc    = region,
                    LoiNhan   = $"Không thể kết nối đến SQL Server: {ex.Message}"
                });
            }
        }

        // GET /api/trips/pending/{region} — tài xế lấy danh sách cuốc đang chờ
        [Authorize]
        [HttpGet("pending/{region}")]
        public IActionResult GetPendingTrips(string region)
        {
            try
            {
                var table = _db.ExecuteReader(region,
                    "SELECT TripID, UserID, PickupLocation, DropoffLocation, Region, CreatedAt " +
                    "FROM Trips WHERE Status='Pending' AND Region=@region ORDER BY CreatedAt DESC",
                    cmd => cmd.Parameters.AddWithValue("@region", region));

                var trips = new List<PendingTripItem>();
                foreach (System.Data.DataRow row in table.Rows)
                {
                    trips.Add(new PendingTripItem
                    {
                        TripID          = (int)row["TripID"],
                        UserID          = (int)row["UserID"],
                        PickupLocation  = row["PickupLocation"].ToString() ?? "",
                        DropoffLocation = row["DropoffLocation"].ToString() ?? "",
                        Region          = row["Region"].ToString() ?? "",
                        CreatedAt       = row["CreatedAt"] is DBNull ? null : (DateTime?)row["CreatedAt"]
                    });
                }
                return Ok(trips);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST /api/trips/{tripId}/accept — tài xế nhận cuốc
        [Authorize]
        [HttpPost("{tripId:int}/accept")]
        public async Task<IActionResult> AcceptTrip(int tripId)
        {
            string region = HttpContext.GetRegion();
            var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value;
            int driverId = int.Parse(subClaim ?? "0");
            string driverName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                             ?? User.FindFirst("unique_name")?.Value ?? "Tài xế";

            try
            {
                int rows = _db.ExecuteNonQuery(region,
                    "UPDATE Trips SET Status='Accepted', DriverID=@driverId " +
                    "WHERE TripID=@tripId AND Status='Pending'",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@driverId", driverId);
                        cmd.Parameters.AddWithValue("@tripId", tripId);
                    });

                if (rows == 0)
                    return Conflict(new { error = "Chuyến đi đã được nhận bởi tài xế khác." });

                await _hub.Clients.Group($"Trip_{tripId}")
                    .SendAsync("OnTripStatusChanged", "Accepted",
                        $"Tài xế {driverName} đã nhận chuyến của bạn!");

                return Ok(new { accepted = true, driverId, driverName });
            }
            catch (InvalidOperationException)
            {
                return StatusCode(503, new { error = "Server Chính đang bảo trì." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST /api/trips/{tripId}/arrive — tài xế đến điểm đón
        [Authorize]
        [HttpPost("{tripId:int}/arrive")]
        public async Task<IActionResult> ArriveAtPickup(int tripId)
        {
            string region = HttpContext.GetRegion();
            try
            {
                _db.ExecuteNonQuery(region,
                    "UPDATE Trips SET Status='Arrived' WHERE TripID=@tripId",
                    cmd => cmd.Parameters.AddWithValue("@tripId", tripId));

                await _hub.Clients.Group($"Trip_{tripId}")
                    .SendAsync("OnTripStatusChanged", "Arrived", "Tài xế đã đến điểm đón!");

                return Ok(new { arrived = true });
            }
            catch (InvalidOperationException)
            {
                return StatusCode(503, new { error = "Server Chính đang bảo trì." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST /api/trips/{tripId}/complete — hoàn thành chuyến
        [Authorize]
        [HttpPost("{tripId:int}/complete")]
        public async Task<IActionResult> CompleteTrip(int tripId)
        {
            string region = HttpContext.GetRegion();
            try
            {
                _db.ExecuteNonQuery(region,
                    "UPDATE Trips SET Status='Completed' WHERE TripID=@tripId",
                    cmd => cmd.Parameters.AddWithValue("@tripId", tripId));

                await _hub.Clients.Group($"Trip_{tripId}")
                    .SendAsync("OnTripStatusChanged", "Completed", "Chuyến đi hoàn thành. Cảm ơn bạn!");

                return Ok(new { completed = true });
            }
            catch (InvalidOperationException)
            {
                return StatusCode(503, new { error = "Server Chính đang bảo trì." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/trips/health/{region}
        // Trả về trạng thái Primary và Replica — client dùng để tự phát hiện failover khi khởi động.
        [HttpGet("health/{region}")]
        public IActionResult Health(string region)
        {
            bool primaryOk = _db.IsPrimaryAlive(region);
            bool replicaOk = _db.IsReplicaAlive(region);
            return Ok(new
            {
                Region    = region,
                PrimaryOk = primaryOk,
                ReplicaOk = replicaOk,
                IsFailover = !primaryOk && replicaOk,
                Message   = primaryOk
                    ? $"Server chính ({region}) hoạt động bình thường."
                    : replicaOk
                        ? $"Server chính ({region}) KHÔNG khả dụng — đang dùng Replica."
                        : $"Cả Primary lẫn Replica ({region}) đều không phản hồi!"
            });
        }
    }
}
