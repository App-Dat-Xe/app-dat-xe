using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RideHailingApi.Models;
using RideHailingApi.Services;

namespace RideHailingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TripsController : ControllerBase
    {
        private readonly DbConnectionFactory _dbFactory;

        public TripsController(DbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory;
        }

        [HttpPost("book-trip")]
        public IActionResult BookTrip([FromBody] TripRequest request)
        {
            string primaryConn = _dbFactory.GetConnectionString(request.Region, isFailover: false);

            try
            {
                // Thử ghi vào Primary
                using (var conn = new SqlConnection(primaryConn))
                {
                    conn.Open();
                    string sql = "INSERT INTO Trips (UserID, PickupLocation, DropoffLocation, Region, Status) VALUES (@Uid, @Pick, @Drop, @Reg, 'Pending')";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@Uid", request.UserID);
                        cmd.Parameters.AddWithValue("@Pick", request.PickupLocation);
                        cmd.Parameters.AddWithValue("@Drop", request.DropoffLocation);
                        cmd.Parameters.AddWithValue("@Reg", request.Region);
                        cmd.ExecuteNonQuery();
                    }
                    return Ok(new { message = $"Đặt xe thành công tại Server Chính ({request.Region})" });
                }
            }
            catch (SqlException)
            {
                // PRIMARY SẬP -> BẺ LÁI SANG REPLICA
                string replicaConn = _dbFactory.GetConnectionString(request.Region, isFailover: true);
                try
                {
                    using (var fbConn = new SqlConnection(replicaConn))
                    {
                        fbConn.Open();
                        // Trả về báo lỗi 503 nhưng kèm thông báo đã chuyển sang Read-Only
                        return StatusCode(503, new
                        {
                            error = "Server Chính đang bảo trì.",
                            message = "Đã bẻ lái sang Server Dự phòng (Read-Only). Bạn chỉ có thể xem lịch sử, không thể đặt xe mới lúc này."
                        });
                    }
                }
                catch (Exception)
                {
                    return StatusCode(500, "Cả hai hệ thống đều sập!");
                }
            }
        }
        [HttpGet("test-connection/{region}")]
        public IActionResult TestDBconnection(string region)
        {
            try
            {
                string primaryConn = _dbFactory.GetConnectionString(region, isFailover: false);
                using (var conn = new SqlConnection(primaryConn))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("SELECT @@SERVERNAME", conn))
                    {
                        var serverName = cmd.ExecuteScalar()?.ToString();
                        return Ok(new
                        {
                            TrangThai = "Kết nối thành công",
                            KhuVuc = region,
                            ServerName = serverName,
                            LoiNhan = "API của bạn đã đâm xuyên qua SQL Server rồi đó!"
                        });
                    }

                }
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