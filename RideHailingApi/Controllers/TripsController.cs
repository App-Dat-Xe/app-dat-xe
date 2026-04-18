using Microsoft.AspNetCore.Mvc;
using RideHailingApi.Data;
using RideHailingApi.Middleware;
using RideHailingApi.Models;

namespace RideHailingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TripsController : ControllerBase
    {
        private readonly DataConnect _db;

        public TripsController(DataConnect db)
        {
            _db = db;
        }

        // POST /api/trips/book-trip — Region từ header X-Region (fallback Region trong body để tương thích)
        [HttpPost("book-trip")]
        public IActionResult BookTrip([FromBody] TripRequest request)
        {
            string region = HttpContext.GetRegion();
            if (!string.IsNullOrWhiteSpace(request.Region))
                region = request.Region;

            try
            {
                _db.ExecuteNonQuery(region,
                    "INSERT INTO Trips (UserID, PickupLocation, DropoffLocation, Region, Status) " +
                    "VALUES (@Uid, @Pick, @Drop, @Reg, 'Pending')",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@Uid", request.UserID);
                        cmd.Parameters.AddWithValue("@Pick", request.PickupLocation);
                        cmd.Parameters.AddWithValue("@Drop", request.DropoffLocation);
                        cmd.Parameters.AddWithValue("@Reg", region);
                    });
                return Ok(new { message = $"Đặt xe thành công tại Server Chính ({region})" });
            }
            catch (InvalidOperationException)
            {
                // Primary sập — DataConnect không cho ghi vào Replica
                return StatusCode(503, new
                {
                    error = "Server Chính đang bảo trì.",
                    message = "Hệ thống đang ở chế độ Read-Only. Bạn chỉ có thể xem lịch sử, không thể đặt xe mới lúc này."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
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
                        TripID = (int)row["TripID"],
                        UserID = (int)row["UserID"],
                        DriverID = row["DriverID"] is DBNull ? null : (int?)row["DriverID"],
                        PickupLocation = row["PickupLocation"].ToString() ?? "",
                        DropoffLocation = row["DropoffLocation"].ToString() ?? "",
                        Region = row["Region"].ToString() ?? "",
                        Status = row["Status"].ToString() ?? "",
                        CreatedAt = row["CreatedAt"] is DBNull ? null : (DateTime?)row["CreatedAt"]
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
                    TrangThai = "Kết nối thành công",
                    KhuVuc = region,
                    ServerName = serverName,
                    LoiNhan = "API của bạn đã đâm xuyên qua SQL Server rồi đó!"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    TrangThai = "Kết nối thất bại",
                    KhuVuc = region,
                    LoiNhan = $"Không thể kết nối đến SQL Server: {ex.Message}"
                });
            }
        }
    }
}
