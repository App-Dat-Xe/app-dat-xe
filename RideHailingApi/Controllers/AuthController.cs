using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RideHailingApi.Data;
using RideHailingApi.Middleware;
using RideHailingApi.Models;

namespace RideHailingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly DataConnect _db;

        public AuthController(DataConnect db)
        {
            _db = db;
        }

        // POST /api/auth/login — header X-Region xác định cụm DB
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            string region = HttpContext.GetRegion();
            if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { error = "Thiếu UserName hoặc Password." });

            try
            {
                var table = _db.ExecuteReader(region,
                    "SELECT TOP 1 UserID, UserName, FullName, Phone, RegisteredRegion FROM Users " +
                    "WHERE UserName = @u AND PassWord = @p",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@u", req.UserName);
                        cmd.Parameters.AddWithValue("@p", req.Password);
                    });

                if (table.Rows.Count == 0)
                    return Unauthorized(new { error = "Sai UserName hoặc Password." });

                var row = table.Rows[0];
                var user = new UserDto
                {
                    UserID = (int)row["UserID"],
                    UserName = row["UserName"].ToString() ?? "",
                    FullName = row["FullName"].ToString() ?? "",
                    Phone = row["Phone"].ToString() ?? "",
                    RegisteredRegion = row["RegisteredRegion"].ToString() ?? ""
                };
                return Ok(new { region, user });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi server: {ex.Message}" });
            }
        }

        // POST /api/auth/register — Region được lưu vào RegisteredRegion (cố định cho user)
        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest req)
        {
            string region = HttpContext.GetRegion();
            if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { error = "Thiếu thông tin bắt buộc." });

            try
            {
                _db.ExecuteNonQuery(region,
                    "INSERT INTO Users (UserName, FullName, Phone, PassWord, RegisteredRegion) " +
                    "VALUES (@u, @n, @ph, @pw, @r)",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@u", req.UserName);
                        cmd.Parameters.AddWithValue("@n", req.FullName);
                        cmd.Parameters.AddWithValue("@ph", req.Phone);
                        cmd.Parameters.AddWithValue("@pw", req.Password);
                        cmd.Parameters.AddWithValue("@r", req.RegisteredRegion);
                    });
                return Ok(new { message = "Đăng ký thành công.", region });
            }
            catch (InvalidOperationException ex)
            {
                // Primary sập — không cho register
                return StatusCode(503, new { error = ex.Message });
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return Conflict(new { error = "UserName hoặc Phone đã tồn tại." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
