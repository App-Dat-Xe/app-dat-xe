using Asp.Versioning;
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
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly DataConnect _db;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthController> _logger;
        private readonly RefreshTokenService _refreshTokens;
        private readonly IEmailService _email;
        private readonly EmailTokenService _emailTokens;

        public AuthController(
            DataConnect db,
            IConfiguration config,
            ILogger<AuthController> logger,
            RefreshTokenService refreshTokens,
            IEmailService email,
            EmailTokenService emailTokens)
        {
            _db = db;
            _config = config;
            _logger = logger;
            _refreshTokens = refreshTokens;
            _email = email;
            _emailTokens = emailTokens;
        }

        // POST /api/auth/login
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
                    string pwCol = DetectPasswordColumn(region);
                    table = _db.ExecuteReader(region,
                        $"SELECT TOP 1 UserID, UserName, FullName, Phone, RegisteredRegion, [{pwCol}] AS PassWord FROM Users WHERE UserName = @u OR Phone = @u",
                        cmd => cmd.Parameters.AddWithValue("@u", req.UserName));
                }

                if (table.Rows.Count == 0)
                {
                    _logger.LogInformation("Login failed: user not found for username={UserName}", req.UserName);
                    return Unauthorized(new { error = "Unauthorized", message = "Tài khoản không tồn tại hoặc thông tin đăng nhập sai." });
                }

                var row = table.Rows[0];
                string storedHash = row["PassWord"].ToString() ?? string.Empty;

                bool ok = VerifyPassword(req.Password, storedHash, req.IsDriver);
                if (!ok)
                {
                    _logger.LogInformation("Login failed: invalid password for username={UserName}", req.UserName);
                    return Unauthorized(new { error = "Unauthorized", message = "Mật khẩu không đúng." });
                }

                try
                {
                    if (row.Table.Columns.Contains("IsLocked") && Convert.ToBoolean(row["IsLocked"]))
                        return Unauthorized(new { error = "Unauthorized", message = "Tài khoản đã bị khóa." });
                }
                catch { }

                int    userId           = (int)row["UserID"];
                string userName         = row["UserName"].ToString() ?? "";
                string registeredRegion = row["RegisteredRegion"].ToString() ?? region;
                string role             = req.IsDriver ? "DRIVER" : "USER";

                string accessToken  = GenerateJwt(userId, userName, registeredRegion, role);
                string refreshToken = _refreshTokens.Generate(userId, userName, registeredRegion, role);
                int    expiryMinutes = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15");

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
                        phone    = row["Phone"].ToString() ?? "",
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

        // POST /api/auth/admin/login
        [EnableRateLimiting("auth")]
        [HttpPost("admin/login")]
        public IActionResult AdminLogin([FromBody] AdminLoginRequest req)
        {
            var adminCfg = _config.GetSection("AdminSettings");
            string? cfgUser = adminCfg["UserName"];
            string? cfgPass = adminCfg["Password"];

            if (string.IsNullOrEmpty(cfgUser) || string.IsNullOrEmpty(cfgPass))
                return StatusCode(503, new { error = "Admin credentials not configured on server." });

            if (req.UserName != cfgUser || req.Password != cfgPass)
                return Unauthorized(new { error = "Invalid admin credentials." });

            string accessToken  = GenerateJwt(0, req.UserName, "All", "ADMIN");
            string refreshToken = _refreshTokens.Generate(0, req.UserName, "All", "ADMIN");
            int    expiryMinutes = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15");

            return Ok(new
            {
                accessToken,
                refreshToken,
                tokenType = "Bearer",
                expiresIn = expiryMinutes * 60,
                user = new { userName = req.UserName, roles = new[] { "ADMIN" } }
            });
        }

        // POST /api/auth/refresh
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
            string newAccess  = GenerateJwt(entry.UserId, entry.UserName, entry.Region, entry.Role);
            string newRefresh = _refreshTokens.Generate(entry.UserId, entry.UserName, entry.Region, entry.Role);
            int expiryMinutes = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15");

            return Ok(new { accessToken = newAccess, refreshToken = newRefresh, expiresIn = expiryMinutes * 60 });
        }

        // POST /api/auth/logout
        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout([FromBody] RefreshTokenRequest req)
        {
            if (!string.IsNullOrWhiteSpace(req.RefreshToken))
                _refreshTokens.Revoke(req.RefreshToken);
            return Ok(new { message = "Đăng xuất thành công." });
        }

        // POST /api/auth/register
        [EnableRateLimiting("auth")]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            string region = HttpContext.GetRegion();
            if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password)
                || string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Phone))
                return BadRequest(new { error = "Thiếu thông tin bắt buộc.", message = "Vui lòng nhập đầy đủ thông tin." });

            _logger.LogInformation("Register attempt: UserName={UserName}, Region={Region}", req.UserName, req.RegisteredRegion ?? region);

            try
            {
                int dupCount = _db.ExecuteScalarInt(region,
                    "SELECT COUNT(1) FROM Users WHERE UserName = @u OR Phone = @ph",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@u",  req.UserName);
                        cmd.Parameters.AddWithValue("@ph", req.Phone);
                    });
                if (dupCount > 0)
                    return Conflict(new { error = "Duplicate", message = "UserName hoặc Phone đã tồn tại." });

                string pwCol = DetectPasswordColumn(region);
                string hashed = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12);

                // Include Email column if it exists in DB
                bool hasEmailCol = _db.ExecuteScalarInt(region,
                    "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='Email'") > 0;

                string sql = hasEmailCol
                    ? $"INSERT INTO Users (UserName, FullName, Phone, Email, {pwCol}, RegisteredRegion) VALUES (@u, @n, @ph, @em, @pw, @r)"
                    : $"INSERT INTO Users (UserName, FullName, Phone, {pwCol}, RegisteredRegion) VALUES (@u, @n, @ph, @pw, @r)";

                _db.ExecuteNonQuery(region, sql, cmd =>
                {
                    cmd.Parameters.AddWithValue("@u",  req.UserName);
                    cmd.Parameters.AddWithValue("@n",  req.FullName);
                    cmd.Parameters.AddWithValue("@ph", req.Phone);
                    cmd.Parameters.AddWithValue("@pw", hashed);
                    cmd.Parameters.AddWithValue("@r",  req.RegisteredRegion ?? region);
                    if (hasEmailCol)
                        cmd.Parameters.AddWithValue("@em", req.Email ?? "");
                });

                // Send email verification if email provided and column exists
                if (!string.IsNullOrWhiteSpace(req.Email) && hasEmailCol)
                {
                    await SendVerificationEmailAsync(req.Email, req.UserName, region);
                    return Ok(new { message = "Đăng ký thành công. Vui lòng kiểm tra email để xác minh tài khoản.", region });
                }

                return Ok(new { message = "Đăng ký thành công.", region });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Register failed (InvalidOperation) for {UserName}", req.UserName);
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

        // GET /api/auth/verify-email?token=xxx
        [HttpGet("verify-email")]
        public IActionResult VerifyEmail([FromQuery] string token)
        {
            var entry = _emailTokens.Validate(token, "email-verify");
            if (entry == null)
                return BadRequest(new { error = "Link xác minh không hợp lệ hoặc đã hết hạn." });

            try
            {
                _db.ExecuteNonQuery(entry.Region,
                    "UPDATE Users SET IsEmailVerified=1 WHERE UserName=@u OR Email=@em",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@u",  entry.Email);
                        cmd.Parameters.AddWithValue("@em", entry.Email);
                    });
                _emailTokens.Consume(token);
                return Ok(new { message = "Email đã được xác minh thành công. Bạn có thể đăng nhập." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VerifyEmail failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST /api/auth/forgot-password
        [EnableRateLimiting("auth")]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
        {
            // Always return 200 to prevent user enumeration attacks
            const string safeMessage = "Nếu tài khoản tồn tại, email đặt lại mật khẩu đã được gửi.";

            if (string.IsNullOrWhiteSpace(req.UserNameOrEmail))
                return BadRequest(new { error = "Vui lòng nhập UserName hoặc Email." });

            string region = string.IsNullOrEmpty(req.Region) ? HttpContext.GetRegion() : req.Region;

            try
            {
                // Look up user by UserName or Email
                bool hasEmailCol = _db.ExecuteScalarInt(region,
                    "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='Email'") > 0;

                string lookupSql = hasEmailCol
                    ? "SELECT TOP 1 UserID, UserName, ISNULL(Email,'') AS Email FROM Users WHERE UserName=@q OR Email=@q"
                    : "SELECT TOP 1 UserID, UserName, '' AS Email FROM Users WHERE UserName=@q";

                var table = _db.ExecuteReader(region, lookupSql,
                    cmd => cmd.Parameters.AddWithValue("@q", req.UserNameOrEmail));

                if (table.Rows.Count == 0)
                    return Ok(new { message = safeMessage });

                var row   = table.Rows[0];
                string email    = row["Email"].ToString() ?? "";
                string userName = row["UserName"].ToString() ?? "";
                int    userId   = (int)row["UserID"];

                // Fallback: use UserName as "email" identifier if no email stored
                string recipient = !string.IsNullOrEmpty(email) ? email : req.UserNameOrEmail;

                // If the identifier looks like an email address, send the reset email
                if (!recipient.Contains('@'))
                    return Ok(new { message = safeMessage });

                string resetToken = _emailTokens.Generate(recipient, userId, "password-reset", region, expiryMinutes: 30);
                var baseUrl = _config["AppSettings:BaseUrl"] ?? "http://localhost:5108";
                string resetLink = $"{baseUrl}/reset-password.html?token={resetToken}";

                await _email.SendAsync(recipient, "Đặt lại mật khẩu RideHailing",
                    $"""
                    <h2>Đặt lại mật khẩu</h2>
                    <p>Xin chào <strong>{userName}</strong>,</p>
                    <p>Nhấn vào link bên dưới để đặt lại mật khẩu. Link có hiệu lực trong <strong>30 phút</strong>.</p>
                    <p><a href="{resetLink}" style="padding:10px 20px;background:#007bff;color:#fff;border-radius:4px;text-decoration:none;">Đặt lại mật khẩu</a></p>
                    <p>Nếu bạn không yêu cầu điều này, hãy bỏ qua email này.</p>
                    """);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ForgotPassword error for {Input}", req.UserNameOrEmail);
            }

            return Ok(new { message = safeMessage });
        }

        // POST /api/auth/reset-password
        [HttpPost("reset-password")]
        public IActionResult ResetPassword([FromBody] ResetPasswordRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.NewPassword))
                return BadRequest(new { error = "Token và mật khẩu mới là bắt buộc." });

            if (req.NewPassword.Length < 6)
                return BadRequest(new { error = "Mật khẩu phải có ít nhất 6 ký tự." });

            var entry = _emailTokens.Validate(req.Token, "password-reset");
            if (entry == null)
                return BadRequest(new { error = "Link đặt lại mật khẩu không hợp lệ hoặc đã hết hạn." });

            try
            {
                string pwCol = DetectPasswordColumn(entry.Region);
                string hashed = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, workFactor: 12);

                _db.ExecuteNonQuery(entry.Region,
                    $"UPDATE Users SET [{pwCol}]=@pw WHERE UserID=@uid",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@pw",  hashed);
                        cmd.Parameters.AddWithValue("@uid", entry.UserId!.Value);
                    });

                _emailTokens.Consume(req.Token);
                _refreshTokens.RevokeAllForUser(entry.UserId!.Value);

                return Ok(new { message = "Mật khẩu đã được đặt lại thành công. Vui lòng đăng nhập lại." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResetPassword failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private async Task SendVerificationEmailAsync(string email, string userName, string region)
        {
            try
            {
                string token = _emailTokens.Generate(email, null, "email-verify", region, expiryMinutes: 1440); // 24h
                var baseUrl  = _config["AppSettings:BaseUrl"] ?? "http://localhost:5108";
                string link  = $"{baseUrl}/api/auth/verify-email?token={token}";

                await _email.SendAsync(email, "Xác minh tài khoản RideHailing",
                    $"""
                    <h2>Xác minh tài khoản của bạn</h2>
                    <p>Xin chào <strong>{userName}</strong>,</p>
                    <p>Cảm ơn bạn đã đăng ký! Nhấn vào link bên dưới để xác minh email.</p>
                    <p><a href="{link}" style="padding:10px 20px;background:#28a745;color:#fff;border-radius:4px;text-decoration:none;">Xác minh Email</a></p>
                    <p>Link có hiệu lực trong <strong>24 giờ</strong>.</p>
                    """);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send verification email to {Email}", email);
            }
        }

        private string DetectPasswordColumn(string region)
        {
            if (_db.ExecuteScalarInt(region, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='PassWord'") > 0)
                return "PassWord";
            if (_db.ExecuteScalarInt(region, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Users' AND COLUMN_NAME='PasswordHash'") > 0)
                return "PasswordHash";
            return "Password";
        }

        private static bool VerifyPassword(string input, string stored, bool isDriver)
        {
            if (string.IsNullOrEmpty(stored)) return false;
            if (stored.StartsWith("$2"))
            {
                try { return BCrypt.Net.BCrypt.Verify(input, stored); }
                catch { return false; }
            }
            // Legacy plaintext fallback (drivers or old users)
            return stored == input;
        }

        private string GenerateJwt(int userId, string userName, string region, string role = "USER")
        {
            var jwt = _config.GetSection("JwtSettings");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            int expiryMinutes = int.Parse(jwt["ExpiryMinutes"] ?? "15");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub,        userId.ToString()),
                new Claim(ClaimTypes.NameIdentifier,          userId.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim(JwtRegisteredClaimNames.Jti,        Guid.NewGuid().ToString()),
                new Claim("region",                           region),
                new Claim(ClaimTypes.Role,                    role)
            };

            var token = new JwtSecurityToken(
                issuer:             jwt["Issuer"],
                audience:           jwt["Audience"],
                claims:             claims,
                expires:            DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
