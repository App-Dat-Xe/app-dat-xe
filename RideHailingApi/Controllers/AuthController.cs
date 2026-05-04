using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using RideHailingApi.Data;
using RideHailingApi.Middleware;
using RideHailingApi.Models;
using RideHailingApi.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;

namespace RideHailingApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly DataConnect _db;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;
        private readonly RefreshTokenService _refreshTokens;

        public AuthController(DataConnect db, IConfiguration config,
            ILogger<AuthController> logger, RefreshTokenService refreshTokens)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _refreshTokens = refreshTokens;
        }

        // POST /api/auth/login — Public endpoint, returns JWT + refresh token
        [EnableRateLimiting("auth")]
        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            string region = HttpContext.GetRegion();
            if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { error = "Thiếu UserName hoặc Password.", message = "Vui lòng nhập đầy đủ UserName và Password." });

            try
            {
                System.Data.DataTable table;
                if (req.IsDriver)
                {
                    table = _db.ExecuteReader(region,
                        "SELECT TOP 1 DriverID AS UserID, Phone AS UserName, FullName, Phone, @r AS RegisteredRegion, PasswordHash AS PassWord FROM Drivers WHERE Phone = @u OR FullName = @u",
                        cmd =>
                        {
                            cmd.Parameters.AddWithValue("@r", region);
                            cmd.Parameters.AddWithValue("@u", req.UserName);
                        });
                }
                else
                {
                    string pwCol = "PassWord";
                    if (_db.ExecuteScalarInt(region, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='PassWord'") == 0)
                    {
                        if (_db.ExecuteScalarInt(region, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='PasswordHash'") > 0)
                            pwCol = "PasswordHash";
                        else if (_db.ExecuteScalarInt(region, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='Password'") > 0)
                            pwCol = "Password";
                    }

                    table = _db.ExecuteReader(region,
                        $"SELECT TOP 1 UserID, UserName, FullName, Phone, RegisteredRegion, [{pwCol}] AS PassWord FROM Users WHERE UserName = @u OR Phone = @u",
                        cmd =>
                        {
                            cmd.Parameters.AddWithValue("@u", req.UserName);
                        });
                }

                if (table.Rows.Count == 0)
                {
                    _logger.LogInformation("Login failed: user not found for username={UserName} region={Region}", req.UserName, region);
                    return Unauthorized(new { error = "Unauthorized", message = "Tài khoản không tồn tại hoặc thông tin đăng nhập sai." });
                }

                var row = table.Rows[0];
                string storedHash = row["PassWord"].ToString() ?? string.Empty;

                bool ok = false;
                if (req.IsDriver)
                {
                    ok = storedHash == req.Password;
                }
                else
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(storedHash) && storedHash.StartsWith("$2"))
                        {
                            ok = BCrypt.Net.BCrypt.Verify(req.Password, storedHash);
                        }
                        else
                        {
                            ok = storedHash == req.Password;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Password verification failed (possibly invalid hash) for user {UserName}", req.UserName);
                    }
                }

                if (!ok)
                {
                    _logger.LogInformation("Login failed: invalid password for username={UserName}", req.UserName);
                    return Unauthorized(new { error = "Unauthorized", message = "Mật khẩu không đúng." });
                }

                // Check if account is locked (column may not exist on older DBs)
                try
                {
                    if (row.Table.Columns.Contains("IsLocked") && (bool)row["IsLocked"])
                        return Unauthorized(new { error = "Unauthorized", message = "Tài khoản đã bị khóa." });
                }
                catch { /* IsLocked column not yet migrated */ }

                int userId = (int)row["UserID"];
                string userName = row["UserName"].ToString() ?? "";
                string registeredRegion = row["RegisteredRegion"].ToString() ?? region;

                string role = req.IsDriver ? "DRIVER" : "USER";
                string accessToken  = GenerateJwt(userId, userName, registeredRegion, role);
                string refreshToken = _refreshTokens.Generate(userId, userName, registeredRegion);
                int expiryMinutes   = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15");

                return Ok(new
                {
                    accessToken,
                    refreshToken,
                    tokenType = "Bearer",
                    expiresIn = expiryMinutes * 60,
                    user = new
                    {
                        id = userId,
                        userName,
                        fullName = row["FullName"].ToString() ?? "",
                        phone = row["Phone"].ToString() ?? "",
                        registeredRegion,
                        roles = new[] { role }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error for {UserName}", req.UserName);
                return StatusCode(500, new { error = "ServerError", message = $"Lỗi server: {ex.Message}" });
            }
        }

        // POST /api/auth/refresh — Cấp access token mới từ refresh token
        [EnableRateLimiting("auth")]
        [HttpPost("refresh")]
        public IActionResult Refresh([FromBody] RefreshTokenRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.RefreshToken))
                return BadRequest(new { error = "Thiếu refresh token." });

            var entry = _refreshTokens.Validate(req.RefreshToken);
            if (entry == null)
                return Unauthorized(new { error = "Refresh token không hợp lệ hoặc đã hết hạn." });

            _refreshTokens.Revoke(req.RefreshToken);
            // Defaulting to USER for refreshed tokens here, though typically we'd fetch the role from DB or refresh token store.
            string newAccess  = GenerateJwt(entry.UserId, entry.UserName, entry.Region, "USER");
            string newRefresh = _refreshTokens.Generate(entry.UserId, entry.UserName, entry.Region);
            int expiryMinutes = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15");

            return Ok(new { accessToken = newAccess, refreshToken = newRefresh, expiresIn = expiryMinutes * 60 });
        }

        // POST /api/auth/logout — Thu hồi refresh token
        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout([FromBody] RefreshTokenRequest req)
        {
            if (!string.IsNullOrWhiteSpace(req.RefreshToken))
                _refreshTokens.Revoke(req.RefreshToken);
            return Ok(new { message = "Đăng xuất thành công." });
        }

        // POST /api/auth/register — Public endpoint
        [EnableRateLimiting("auth")]
        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest req)
        {
            string region = HttpContext.GetRegion();
            if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Phone))
                return BadRequest(new { error = "Thiếu thông tin bắt buộc.", message = "Vui lòng nhập đầy đủ thông tin." });
            // Diagnostic log: record attempt (do NOT log password)
            _logger.LogInformation("Register attempt: UserName={UserName}, RegisteredRegion={Region}", req.UserName, req.RegisteredRegion ?? region);

            try
            {
                // Check for existing username or phone first to return clearer errors
                int dupCount = _db.ExecuteScalarInt(region,
                    "SELECT COUNT(1) FROM Users WHERE UserName = @u OR Phone = @ph",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@u", req.UserName);
                        cmd.Parameters.AddWithValue("@ph", req.Phone);
                    });
                if (dupCount > 0)
                    return Conflict(new { error = "Duplicate", message = "UserName hoặc Phone đã tồn tại." });

                // Determine which password column exists and insert accordingly
                string pwCol = "PassWord";
                if (_db.ExecuteScalarInt(region, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='PassWord'") == 0)
                {
                    if (_db.ExecuteScalarInt(region, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='PasswordHash'") > 0)
                        pwCol = "PasswordHash";
                    else if (_db.ExecuteScalarInt(region, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='Password'") > 0)
                        pwCol = "Password";
                }

                // Hash password with bcrypt for secure storage if possible
                string hashed = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12);

                var sql = $"INSERT INTO Users (UserName, FullName, Phone, {pwCol}, RegisteredRegion) VALUES (@u, @n, @ph, @pw, @r)";
                _db.ExecuteNonQuery(region,
                    sql,
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@u", req.UserName);
                        cmd.Parameters.AddWithValue("@n", req.FullName);
                        cmd.Parameters.AddWithValue("@ph", req.Phone);
                        cmd.Parameters.AddWithValue("@pw", hashed);
                        cmd.Parameters.AddWithValue("@r", req.RegisteredRegion);
                    });
                return Ok(new { message = "Đăng ký thành công.", region });
            }
            catch (InvalidOperationException ex)
            {
                // Log details so we can see underlying cause (e.g. DataConnect reported SqlException)
                _logger.LogWarning(ex, "Register failed (InvalidOperation) for UserName={UserName}, Region={Region}: {Message}", req.UserName, region, ex.Message);
                return StatusCode(503, new { error = "DbUnavailable", message = ex.Message });
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return Conflict(new { error = "Duplicate", message = "UserName hoặc Phone đã tồn tại." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Register error for {UserName}", req.UserName);
                return StatusCode(500, new { error = "ServerError", message = ex.Message });
            }
        }

        private string GenerateJwt(int userId, string userName, string region, string role = "USER")
        {
            var jwt = _config.GetSection("JwtSettings");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            int expiryMinutes = int.Parse(jwt["ExpiryMinutes"] ?? "15");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("region", region),
                new Claim(ClaimTypes.Role, role)
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
