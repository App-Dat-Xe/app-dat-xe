using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RideHailingApi.Data;
using RideHailingApi.Hubs;
using RideHailingApi.Middleware;
using RideHailingApi.Models;
using RideHailingApi.Services;

namespace RideHailingApi.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Route("api/[controller]")]
    public class TripsController : ControllerBase
    {
        private readonly DataConnect _db;
        private readonly IHubContext<TripHub> _hub;
        private readonly FareService _fareService;
        private readonly IFcmNotificationService _fcm;

        private int ResolveLocationId(string region, string locationName)
        {
            if (string.IsNullOrWhiteSpace(locationName)) return 1;
            try
            {
                var idObj = _db.ExecuteScalar(region, 
                    "SELECT LocationID FROM Locations WHERE LocationName = @name", 
                    cmd => cmd.Parameters.AddWithValue("@name", locationName));
                if (idObj != null && idObj != DBNull.Value) return Convert.ToInt32(idObj);

                var newId = _db.ExecuteScalarWrite(region,
                    "INSERT INTO Locations (LocationName, Address, Latitude, Longitude) VALUES (@name, @name, 0, 0); SELECT SCOPE_IDENTITY();",
                    cmd => cmd.Parameters.AddWithValue("@name", locationName));
                return Convert.ToInt32(newId);
            }
            catch { return 1; }
        }
        public TripsController(DataConnect db, IHubContext<TripHub> hub, FareService fareService,
            IFcmNotificationService fcm)
        {
            _db = db;
            _hub = hub;
            _fareService = fareService;
            _fcm = fcm;
        }

        // GET /api/trips/estimate-fare?vehicleType=Xe+máy&distanceKm=5.5
        [HttpGet("estimate-fare")]
        public IActionResult EstimateFare([FromQuery] string vehicleType = "Xe máy", [FromQuery] double distanceKm = 0)
        {
            decimal fare = _fareService.Calculate(vehicleType, distanceKm);
            return Ok(new { vehicleType, distanceKm, fare });
        }

        // POST /api/trips/book-trip — Protected: yêu cầu JWT hợp lệ
        [Authorize]
        [HttpPost("book-trip")]
        public async Task<IActionResult> BookTrip([FromBody] TripRequest request)
        {
            string region = HttpContext.GetRegion();
            if (!string.IsNullOrWhiteSpace(request.Region))
                region = request.Region;

            decimal fare = _fareService.Calculate(request.VehicleType, request.DistanceKm);
            int pickupId = request.PickupLocationID > 0 ? request.PickupLocationID : ResolveLocationId(region, request.PickupLocation);
            int dropoffId = request.DropoffLocationID > 0 ? request.DropoffLocationID : ResolveLocationId(region, request.DropoffLocation);

            try
            {
                var newId = _db.ExecuteScalarWrite(region,
                    "INSERT INTO Trips (UserID, PickupLocationID, DropoffLocationID, Region, DistanceKm, Price, Status) " +
                    "VALUES (@Uid, @PickId, @DropId, @Reg, @DistanceKm, @Price, 'Pending'); SELECT SCOPE_IDENTITY();",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@Uid", request.UserID);
                        cmd.Parameters.AddWithValue("@PickId", pickupId);
                        cmd.Parameters.AddWithValue("@DropId", dropoffId);
                        cmd.Parameters.AddWithValue("@Reg", region);
                        cmd.Parameters.AddWithValue("@DistanceKm", request.DistanceKm);
                        cmd.Parameters.AddWithValue("@Price", fare);
                    });

                int tripId = Convert.ToInt32(newId);
                await _hub.Clients.Group($"Trip_{tripId}")
                    .SendAsync("OnTripStatusChanged", "Pending", "Đang tìm tài xế cho bạn...");
                await _hub.Clients.Group($"DriverPool_{region}")
                    .SendAsync("OnNewTripRequest", tripId, request.PickupLocationID, request.DropoffLocationID);

                return Ok(new { tripId, message = $"Đặt xe thành công tại Server Chính ({region})", fare });
            }
            catch (InvalidOperationException)
            {
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
                    "SELECT t.TripID, t.UserID, t.DriverID, p.LocationName AS PickupLocation, d.LocationName AS DropoffLocation, t.Region, t.Status, " +
                    "'Xe máy' AS VehicleType, t.Price AS Fare, t.UpdatedAt " +
                    "FROM Trips t " +
                    "LEFT JOIN Locations p ON t.PickupLocationID = p.LocationID " +
                    "LEFT JOIN Locations d ON t.DropoffLocationID = d.LocationID " +
                    "WHERE t.UserID = @id ORDER BY t.UpdatedAt DESC",
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
                        VehicleType     = row["VehicleType"].ToString() ?? "",
                        Fare            = row["Fare"] is DBNull ? null : Convert.ToDecimal(row["Fare"]),
                        CreatedAt       = row["UpdatedAt"] is DBNull ? null : (DateTime?)row["UpdatedAt"]
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
                    "SELECT t.TripID, t.UserID, p.LocationName AS PickupLocation, d.LocationName AS DropoffLocation, t.Region, " +
                    "'Xe máy' AS VehicleType, t.Price AS Fare, t.CreatedAt " +
                    "FROM Trips t " +
                    "LEFT JOIN Locations p ON t.PickupLocationID = p.LocationID " +
                    "LEFT JOIN Locations d ON t.DropoffLocationID = d.LocationID " +
                    "WHERE t.Status='Pending' AND t.Region=@region ORDER BY t.CreatedAt DESC",
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
                        VehicleType     = row["VehicleType"].ToString() ?? "",
                        EstimatedFare   = row["Fare"] is DBNull ? null : Convert.ToDecimal(row["Fare"]),
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

                // Push notification to passenger
                var userIdObj = _db.ExecuteScalar(region,
                    "SELECT UserID FROM Trips WHERE TripID=@id",
                    cmd => cmd.Parameters.AddWithValue("@id", tripId));
                if (userIdObj != null && userIdObj != DBNull.Value)
                    await _fcm.SendToUserAsync(Convert.ToInt32(userIdObj),
                        "Tài xế đã nhận chuyến",
                        $"Tài xế {driverName} đang trên đường đến đón bạn!");

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

                // Push notification to passenger
                var userIdObj = _db.ExecuteScalar(region,
                    "SELECT UserID FROM Trips WHERE TripID=@id",
                    cmd => cmd.Parameters.AddWithValue("@id", tripId));
                if (userIdObj != null && userIdObj != DBNull.Value)
                    await _fcm.SendToUserAsync(Convert.ToInt32(userIdObj),
                        "Tài xế đã đến nơi",
                        "Tài xế đang chờ bạn tại điểm đón. Hãy ra ngay!");

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

        // POST /api/trips/{tripId}/pickup — tài xế đón khách, bắt đầu di chuyển
        [Authorize]
        [HttpPost("{tripId:int}/pickup")]
        public async Task<IActionResult> PickupPassenger(int tripId)
        {
            string region = HttpContext.GetRegion();
            try
            {
                _db.ExecuteNonQuery(region,
                    "UPDATE Trips SET Status='InProgress' WHERE TripID=@tripId",
                    cmd => cmd.Parameters.AddWithValue("@tripId", tripId));

                await _hub.Clients.Group($"Trip_{tripId}")
                    .SendAsync("OnTripStatusChanged", "InProgress", "Tài xế đã đón khách. Đang di chuyển đến điểm đến.");

                return Ok(new { inProgress = true });
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
                // Read fare before marking completed
                decimal? fare = null;
                try
                {
                    var fareObj = _db.ExecuteScalar(region,
                        "SELECT Price FROM Trips WHERE TripID=@tripId",
                        cmd => cmd.Parameters.AddWithValue("@tripId", tripId));
                    if (fareObj is not null and not DBNull)
                        fare = Convert.ToDecimal(fareObj);
                }
                catch { /* Fare column may not exist on older DB */ }

                _db.ExecuteNonQuery(region,
                    "UPDATE Trips SET Status='Completed' WHERE TripID=@tripId",
                    cmd => cmd.Parameters.AddWithValue("@tripId", tripId));

                string fareMsg = fare.HasValue ? $" Tổng tiền: {fare.Value:#,##0}đ." : "";
                await _hub.Clients.Group($"Trip_{tripId}")
                    .SendAsync("OnTripStatusChanged", "Completed", $"Chuyến đi hoàn thành.{fareMsg} Cảm ơn bạn!");

                return Ok(new { completed = true, fare });
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

        // POST /api/trips/{tripId}/cancel — hủy chuyến (user hoặc driver)
        [Authorize]
        [HttpPost("{tripId:int}/cancel")]
        public async Task<IActionResult> CancelTrip(int tripId)
        {
            string region = HttpContext.GetRegion();
            try
            {
                _db.ExecuteNonQuery(region,
                    "UPDATE Trips SET Status='Cancelled' WHERE TripID=@tripId AND Status IN ('Pending','Accepted','Arrived')",
                    cmd => cmd.Parameters.AddWithValue("@tripId", tripId));

                await _hub.Clients.Group($"Trip_{tripId}")
                    .SendAsync("OnTripStatusChanged", "CancelledByDriver", "Chuyến đi đã bị hủy.");

                return Ok(new { cancelled = true });
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

        // POST /api/trips/{tripId}/rating — khách hàng đánh giá tài xế
        [Authorize]
        [HttpPost("{tripId:int}/rating")]
        public IActionResult SubmitRating(int tripId, [FromBody] RatingRequest req)
        {
            string region = HttpContext.GetRegion();
            var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value;
            int userId = int.Parse(subClaim ?? "0");

            if (req.Score < 1 || req.Score > 5)
                return BadRequest(new { error = "Score phải từ 1 đến 5." });

            try
            {
                _db.ExecuteNonQuery(region,
                    "INSERT INTO Ratings (TripID, UserID, Score, Comment, CreatedAt) " +
                    "VALUES (@tripId, @userId, @score, @comment, GETDATE())",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@tripId",  tripId);
                        cmd.Parameters.AddWithValue("@userId",  userId);
                        cmd.Parameters.AddWithValue("@score",   req.Score);
                        cmd.Parameters.AddWithValue("@comment", (object?)req.Comment ?? DBNull.Value);
                    });
                return Ok(new { rated = true, score = req.Score });
            }
            catch (InvalidOperationException)
            {
                return StatusCode(503, new { error = "Server Chính đang bảo trì." });
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return Conflict(new { error = "Chuyến này đã được đánh giá." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/trips/{tripId}/invoice — chi tiết hoá đơn chuyến
        [HttpGet("{tripId:int}/invoice")]
        public IActionResult GetInvoice(int tripId)
        {
            string region = HttpContext.GetRegion();
            try
            {
                var table = _db.ExecuteReader(region,
                    "SELECT t.TripID, t.UserID, t.DriverID, p.LocationName AS PickupLocation, d.LocationName AS DropoffLocation, " +
                    "t.Region, t.Status, 'Xe máy' AS VehicleType, " +
                    "ISNULL(t.DistanceKm,0) AS DistanceKm, t.Price AS Fare, t.CreatedAt, " +
                    "ISNULL(r.Score,0) AS RatingScore, ISNULL(r.Comment,'') AS RatingComment " +
                    "FROM Trips t " +
                    "LEFT JOIN Ratings r ON t.TripID = r.TripID " +
                    "LEFT JOIN Locations p ON t.PickupLocationID = p.LocationID " +
                    "LEFT JOIN Locations d ON t.DropoffLocationID = d.LocationID " +
                    "WHERE t.TripID = @tripId",
                    cmd => cmd.Parameters.AddWithValue("@tripId", tripId));

                if (table.Rows.Count == 0)
                    return NotFound(new { error = "Không tìm thấy chuyến đi." });

                var row = table.Rows[0];
                decimal fare = row["Fare"] is DBNull ? 0m : Convert.ToDecimal(row["Fare"]);
                double distKm = Convert.ToDouble(row["DistanceKm"]);

                return Ok(new
                {
                    tripId        = (int)row["TripID"],
                    userId        = (int)row["UserID"],
                    driverId      = row["DriverID"] is DBNull ? null : (int?)Convert.ToInt32(row["DriverID"]),
                    pickup        = row["PickupLocation"].ToString(),
                    dropoff       = row["DropoffLocation"].ToString(),
                    vehicleType   = row["VehicleType"].ToString(),
                    distanceKm    = distKm,
                    baseFare      = 10_000m,
                    distanceFare  = Math.Max(0m, fare - 10_000m),
                    totalFare     = fare,
                    status        = row["Status"].ToString(),
                    createdAt     = row["CreatedAt"] is DBNull ? null : (DateTime?)row["CreatedAt"],
                    ratingScore   = Convert.ToInt32(row["RatingScore"]),
                    ratingComment = row["RatingComment"].ToString()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/trips/driver/history — lịch sử cuốc của tài xế đang đăng nhập
        [Authorize]
        [HttpGet("driver/history")]
        public IActionResult GetDriverHistory()
        {
            string region = HttpContext.GetRegion();
            var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst("sub")?.Value;
            int driverId = int.Parse(subClaim ?? "0");

            try
            {
                var table = _db.ExecuteReader(region,
                    "SELECT t.TripID, t.UserID, t.DriverID, p.LocationName AS PickupLocation, d.LocationName AS DropoffLocation, t.Region, t.Status, " +
                    "'Xe máy' AS VehicleType, t.Price AS Fare, t.CreatedAt " +
                    "FROM Trips t " +
                    "LEFT JOIN Locations p ON t.PickupLocationID = p.LocationID " +
                    "LEFT JOIN Locations d ON t.DropoffLocationID = d.LocationID " +
                    "WHERE t.DriverID=@driverId AND t.Status IN ('Completed','Cancelled') " +
                    "ORDER BY t.CreatedAt DESC",
                    cmd => cmd.Parameters.AddWithValue("@driverId", driverId));

                var trips = new List<TripHistoryItem>();
                foreach (System.Data.DataRow row in table.Rows)
                {
                    trips.Add(new TripHistoryItem
                    {
                        TripID          = (int)row["TripID"],
                        UserID          = (int)row["UserID"],
                        DriverID        = driverId,
                        PickupLocation  = row["PickupLocation"].ToString() ?? "",
                        DropoffLocation = row["DropoffLocation"].ToString() ?? "",
                        Region          = row["Region"].ToString() ?? "",
                        Status          = row["Status"].ToString() ?? "",
                        VehicleType     = row["VehicleType"].ToString() ?? "",
                        Fare            = row["Fare"] is DBNull ? null : Convert.ToDecimal(row["Fare"]),
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

        // ===== POOLING ENDPOINTS =====

        // GET /api/trips/pool-candidates/{tripId}?mainPickupLat=10.7605&mainPickupLon=106.7035&mainDropoffLat=10.8&mainDropoffLon=106.8
        // Tìm danh sách cuốc có thể ghép với cuốc chính (theo tiêu chí khoảng cách, thời gian, loại xe)
        [Authorize]
        [HttpGet("pool-candidates/{tripId:int}")]
        public IActionResult GetPoolCandidates(
            int tripId,
            [FromQuery] double mainPickupLat,
            [FromQuery] double mainPickupLon,
            [FromQuery] double mainDropoffLat,
            [FromQuery] double mainDropoffLon)
        {
            string region = HttpContext.GetRegion();

            try
            {
                // Lấy thông tin trip chính
                var mainTripTable = _db.ExecuteReader(region,
                    "SELECT TripID, PickupLocation, DropoffLocation, CreatedAt FROM Trips WHERE TripID=@tripId",
                    cmd => cmd.Parameters.AddWithValue("@tripId", tripId));

                if (mainTripTable.Rows.Count == 0)
                    return NotFound(new { error = "Trip không tồn tại." });

                var mainTripRow = mainTripTable.Rows[0];
                DateTime mainCreatedAt = mainTripRow["CreatedAt"] is DBNull ? DateTime.Now : (DateTime)mainTripRow["CreatedAt"];

                // Lấy danh sách các trip Pending khác (cùng region, khác userID, không phải trip chính)
                var candidatesTable = _db.ExecuteReader(region,
                    "SELECT TOP 50 TripID, UserID, PickupLocation, DropoffLocation, CreatedAt " +
                    "FROM Trips " +
                    "WHERE Status='Pending' AND Region=@region AND TripID!=@tripId " +
                    "ORDER BY CreatedAt DESC",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@region", region);
                        cmd.Parameters.AddWithValue("@tripId", tripId);
                    });

                var candidates = new List<PoolingCandidateItem>();

                const double MaxPickupDistanceKm = 1.0;
                const double MaxDropoffDistanceKm = 1.0;
                const int MaxMinutesOld = 5;

                foreach (System.Data.DataRow row in candidatesTable.Rows)
                {
                    int candidateTripId = (int)row["TripID"];
                    DateTime candidateCreatedAt = row["CreatedAt"] is DBNull ? DateTime.Now : (DateTime)row["CreatedAt"];

                    // Kiểm tra thời gian (cuốc phải tạo trong 5 phút gần nhất)
                    int minutesOld = (int)(mainCreatedAt - candidateCreatedAt).TotalMinutes;
                    if (minutesOld < 0 || minutesOld > MaxMinutesOld)
                        continue;

                    // Trong thực tế, cần parse GPS từ PickupLocation/DropoffLocation
                    // Hiện tại giả sử format là: "10.7605,106.7035" hoặc tương tự
                    string pickupStr = row["PickupLocation"].ToString() ?? "";
                    string dropoffStr = row["DropoffLocation"].ToString() ?? "";

                    if (!TryParseCoordinates(pickupStr, out double candPickupLat, out double candPickupLon))
                        continue;
                    if (!TryParseCoordinates(dropoffStr, out double candDropoffLat, out double candDropoffLon))
                        continue;

                    // Tính khoảng cách
                    double pickupDist = GeoDistanceHelper.CalculateDistance(
                        mainPickupLat, mainPickupLon, candPickupLat, candPickupLon);
                    double dropoffDist = GeoDistanceHelper.CalculateDistance(
                        mainDropoffLat, mainDropoffLon, candDropoffLat, candDropoffLon);

                    // Kiểm tra tiêu chí khoảng cách
                    if (pickupDist <= MaxPickupDistanceKm && dropoffDist <= MaxDropoffDistanceKm)
                    {
                        candidates.Add(new PoolingCandidateItem
                        {
                            TripID = candidateTripId,
                            UserID = (int)row["UserID"],
                            PickupLocation = pickupStr,
                            DropoffLocation = dropoffStr,
                            PickupDistance = pickupDist,
                            DropoffDistance = dropoffDist,
                            MinutesOld = minutesOld,
                            CreatedAt = candidateCreatedAt
                        });
                    }
                }

                return Ok(candidates);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST /api/trips/pool — ghép 2 cuốc lại
        [Authorize]
        [HttpPost("pool")]
        public async Task<IActionResult> PoolTrips([FromBody] PoolTripsRequest req)
        {
            string region = HttpContext.GetRegion();

            if (req.MainTripID == req.SecondaryTripID)
                return BadRequest(new { error = "Không thể ghép 1 cuốc với chính nó." });

            try
            {
                // Cập nhật PooledWithTripID cho cả 2 cuốc
                int rows = _db.ExecuteNonQuery(region,
                    "UPDATE Trips SET PooledWithTripID=@secondary WHERE TripID=@main AND Status='Pending'; " +
                    "UPDATE Trips SET PooledWithTripID=@main WHERE TripID=@secondary AND Status='Pending';",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@main", req.MainTripID);
                        cmd.Parameters.AddWithValue("@secondary", req.SecondaryTripID);
                    });

                if (rows < 2)
                    return Conflict(new { error = "Một hoặc cả 2 cuốc đã không còn có sẵn để ghép." });

                // Thông báo cho cả 2 hành khách
                await _hub.Clients.Group($"Trip_{req.MainTripID}")
                    .SendAsync("OnPoolingNotification", "pooled",
                        $"Chuyến của bạn đã được ghép với một cuốc khác để tiết kiệm chi phí!");

                await _hub.Clients.Group($"Trip_{req.SecondaryTripID}")
                    .SendAsync("OnPoolingNotification", "pooled",
                        $"Chuyến của bạn đã được ghép với một cuốc khác để tiết kiệm chi phí!");

                return Ok(new
                {
                    success = true,
                    mainTripId = req.MainTripID,
                    secondaryTripId = req.SecondaryTripID,
                    message = "Ghép cuốc thành công!"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/trips/pooled/{tripId} — lấy thông tin cuốc ghép
        [Authorize]
        [HttpGet("pooled/{tripId:int}")]
        public IActionResult GetPooledTripInfo(int tripId)
        {
            string region = HttpContext.GetRegion();

            try
            {
                // Lấy trip chính
                var mainTable = _db.ExecuteReader(region,
                    "SELECT TripID, UserID, PickupLocation, DropoffLocation, PooledWithTripID FROM Trips WHERE TripID=@tripId",
                    cmd => cmd.Parameters.AddWithValue("@tripId", tripId));

                if (mainTable.Rows.Count == 0)
                    return NotFound(new { error = "Trip không tồn tại." });

                var mainRow = mainTable.Rows[0];
                int? pooledTripId = mainRow["PooledWithTripID"] is DBNull ? null : (int?)mainRow["PooledWithTripID"];

                if (!pooledTripId.HasValue)
                    return Ok(new { hasPooling = false });

                // Lấy trip ghép
                var pooledTable = _db.ExecuteReader(region,
                    "SELECT TripID, UserID, PickupLocation, DropoffLocation FROM Trips WHERE TripID=@tripId",
                    cmd => cmd.Parameters.AddWithValue("@tripId", pooledTripId.Value));

                if (pooledTable.Rows.Count == 0)
                    return Ok(new { hasPooling = false });

                var pooledRow = pooledTable.Rows[0];

                return Ok(new PooledTripInfo
                {
                    MainTripID = (int)mainRow["TripID"],
                    SecondaryTripID = (int)pooledRow["TripID"],
                    MainUserID = (int)mainRow["UserID"],
                    SecondaryUserID = (int)pooledRow["UserID"],
                    MainPickup = mainRow["PickupLocation"].ToString() ?? "",
                    MainDropoff = mainRow["DropoffLocation"].ToString() ?? "",
                    SecondaryPickup = pooledRow["PickupLocation"].ToString() ?? "",
                    SecondaryDropoff = pooledRow["DropoffLocation"].ToString() ?? "",
                    CurrentPassengers = 2,
                    PooledAt = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // Helper: Parse "lat,lon" string thành double
        private static bool TryParseCoordinates(string coordString, out double lat, out double lon)
        {
            lat = 0;
            lon = 0;

            if (string.IsNullOrEmpty(coordString))
                return false;

            var parts = coordString.Split(',');
            if (parts.Length != 2)
                return false;

            return double.TryParse(parts[0].Trim(), out lat) &&
                   double.TryParse(parts[1].Trim(), out lon);
        }
    }
}
