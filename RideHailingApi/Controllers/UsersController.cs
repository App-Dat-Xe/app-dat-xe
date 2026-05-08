using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RideHailingApi.Data;
using RideHailingApi.Middleware;
using RideHailingApi.Models;
using RideHailingApi.Services;

namespace RideHailingApi.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly DataConnect _db;
        private readonly ILogger<UsersController> _logger;
        private readonly DeviceTokenStore _deviceTokens;

        public UsersController(DataConnect db, ILogger<UsersController> logger, DeviceTokenStore deviceTokens)
        {
            _db = db;
            _logger = logger;
            _deviceTokens = deviceTokens;
        }

        // GET /api/users/{id}
        [HttpGet("{id:int}")]
        public IActionResult GetProfile(int id)
        {
            string region = HttpContext.GetRegion();
            try
            {
                var table = _db.ExecuteReader(region,
                    "SELECT UserID, UserName, FullName, Phone, RegisteredRegion FROM Users WHERE UserID = @id",
                    cmd => cmd.Parameters.AddWithValue("@id", id));

                if (table.Rows.Count == 0)
                    return NotFound(new { error = "Không tìm thấy người dùng." });

                var row = table.Rows[0];
                return Ok(new UserDto
                {
                    UserID           = (int)row["UserID"],
                    UserName         = row["UserName"].ToString() ?? "",
                    FullName         = row["FullName"].ToString() ?? "",
                    Phone            = row["Phone"].ToString() ?? "",
                    RegisteredRegion = row["RegisteredRegion"].ToString() ?? ""
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // PUT /api/users/{id}
        [Authorize]
        [HttpPut("{id:int}")]
        public IActionResult UpdateProfile(int id, [FromBody] UpdateProfileRequest req)
        {
            // Only allow updating own profile
            var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (subClaim == null || int.Parse(subClaim) != id)
                return Forbid();

            string region = HttpContext.GetRegion();
            try
            {
                int rows;
                if (string.IsNullOrEmpty(req.NewPassword))
                {
                    rows = _db.ExecuteNonQuery(region,
                        "UPDATE Users SET FullName = @n, Phone = @ph WHERE UserID = @id",
                        cmd =>
                        {
                            cmd.Parameters.AddWithValue("@n",  req.FullName);
                            cmd.Parameters.AddWithValue("@ph", req.Phone);
                            cmd.Parameters.AddWithValue("@id", id);
                        });
                }
                else
                {
                    // Hash the new password before storing
                    string hashed = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, workFactor: 12);
                    rows = _db.ExecuteNonQuery(region,
                        "UPDATE Users SET FullName = @n, Phone = @ph, PassWord = @pw WHERE UserID = @id",
                        cmd =>
                        {
                            cmd.Parameters.AddWithValue("@n",  req.FullName);
                            cmd.Parameters.AddWithValue("@ph", req.Phone);
                            cmd.Parameters.AddWithValue("@pw", hashed);
                            cmd.Parameters.AddWithValue("@id", id);
                        });
                }

                if (rows == 0)
                    return NotFound(new { error = "Không tìm thấy người dùng." });
                return Ok(new { message = "Cập nhật thành công." });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(503, new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST /api/users/device-token — đăng ký FCM device token
        [Authorize]
        [HttpPost("device-token")]
        public IActionResult RegisterDeviceToken([FromBody] DeviceTokenRequest req)
        {
            var subClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            int callerUserId = int.TryParse(subClaim, out var uid) ? uid : 0;

            // Use the userId from the JWT (ignore req.UserId to prevent spoofing)
            if (callerUserId == 0)
                return Unauthorized();

            if (string.IsNullOrWhiteSpace(req.DeviceToken))
                return BadRequest(new { error = "DeviceToken là bắt buộc." });

            _deviceTokens.Register(callerUserId, req.DeviceToken);
            _logger.LogInformation("Device token registered for user {UserId} ({Platform})", callerUserId, req.Platform);

            return Ok(new { message = "Device token đã được đăng ký." });
        }
    }
}
