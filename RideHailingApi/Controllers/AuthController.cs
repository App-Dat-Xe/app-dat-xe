using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using RideHailingApi.Data;
using RideHailingApi.Middleware;
using RideHailingApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RideHailingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly DataConnect _db;
        private readonly IConfiguration _config;

        public AuthController(DataConnect db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        // POST /api/auth/login — Public endpoint, trả JWT khi đăng nhập thành công
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
                    return Unauthorized(new { error = "Unauthorized", message = "Invalid credentials" });

                var row = table.Rows[0];
                int userId = (int)row["UserID"];
                string userName = row["UserName"].ToString() ?? "";
                string registeredRegion = row["RegisteredRegion"].ToString() ?? region;

                string token = GenerateJwt(userId, userName, registeredRegion);
                int expiryMinutes = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15");

                return Ok(new
                {
                    accessToken = token,
                    tokenType = "Bearer",
                    expiresIn = expiryMinutes * 60,
                    user = new
                    {
                        id = userId,
                        userName,
                        fullName = row["FullName"].ToString() ?? "",
                        phone = row["Phone"].ToString() ?? "",
                        registeredRegion,
                        roles = new[] { "USER" }
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi server: {ex.Message}" });
            }
        }

        // POST /api/auth/register — Public endpoint
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

        private string GenerateJwt(int userId, string userName, string region)
        {
            var jwt = _config.GetSection("JwtSettings");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            int expiryMinutes = int.Parse(jwt["ExpiryMinutes"] ?? "15");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("region", region),
                new Claim(ClaimTypes.Role, "USER")
            };

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
