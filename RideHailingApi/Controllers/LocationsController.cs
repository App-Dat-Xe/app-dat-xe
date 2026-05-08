using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using RideHailingApi.Data;
using RideHailingApi.Middleware;
using System.Data;

namespace RideHailingApi.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Route("api/[controller]")]
    public class LocationsController : ControllerBase
    {
        private readonly DataConnect _db;

        public LocationsController(DataConnect db) { _db = db; }

        // GET /api/locations/search?q=keyword&region=South
        [HttpGet("search")]
        public IActionResult Search([FromQuery] string q, [FromQuery] string? region = null)
        {
            string r = NormalizeRegion(region ?? HttpContext.GetRegion());

            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
                return Ok(new List<object>());

            try
            {
                var table = _db.ExecuteReader(r,
                    @"SELECT TOP 10 LocationID, LocationName, Address, Latitude, Longitude
                      FROM Locations
                      WHERE LocationName LIKE @q OR Address LIKE @q
                      ORDER BY LocationName",
                    cmd => cmd.Parameters.AddWithValue("@q", $"%{q.Trim()}%"));

                return Ok(MapToList(table));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/locations/popular?region=South
        // Trả về địa điểm phổ biến: những nơi được đặt xe nhiều nhất
        [HttpGet("popular")]
        public IActionResult Popular([FromQuery] string? region = null)
        {
            string r = NormalizeRegion(region ?? HttpContext.GetRegion());

            try
            {
                var table = _db.ExecuteReader(r,
                    @"SELECT TOP 8 l.LocationID, l.LocationName, l.Address, l.Latitude, l.Longitude
                      FROM Locations l
                      ORDER BY l.LocationID",
                    null);

                return Ok(MapToList(table));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/locations/all?region=South
        [HttpGet("all")]
        public IActionResult GetAll([FromQuery] string? region = null)
        {
            string r = NormalizeRegion(region ?? HttpContext.GetRegion());
            try
            {
                var table = _db.ExecuteReader(r,
                    "SELECT LocationID, LocationName, Address, Latitude, Longitude FROM Locations ORDER BY LocationName",
                    null);

                return Ok(MapToList(table));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // POST /api/locations — thêm địa điểm mới (dùng khi tạo chuyến với địa chỉ Google)
        [HttpPost]
        public IActionResult Create([FromBody] CreateLocationRequest req)
        {
            string r = NormalizeRegion(req.Region ?? HttpContext.GetRegion());
            try
            {
                var id = _db.ExecuteScalarWrite(r,
                    @"IF NOT EXISTS (SELECT 1 FROM Locations WHERE LocationName = @name AND ABS(Latitude - @lat) < 0.001 AND ABS(Longitude - @lng) < 0.001)
                      BEGIN
                          INSERT INTO Locations (LocationName, Address, Latitude, Longitude)
                          VALUES (@name, @addr, @lat, @lng);
                          SELECT SCOPE_IDENTITY();
                      END
                      ELSE
                          SELECT LocationID FROM Locations WHERE LocationName = @name AND ABS(Latitude - @lat) < 0.001 AND ABS(Longitude - @lng) < 0.001",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("@name", req.LocationName);
                        cmd.Parameters.AddWithValue("@addr", req.Address ?? req.LocationName);
                        cmd.Parameters.AddWithValue("@lat",  req.Latitude);
                        cmd.Parameters.AddWithValue("@lng",  req.Longitude);
                    });

                return Ok(new { locationId = Convert.ToInt32(id) });
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

        // ─── Helpers ───

        private static string NormalizeRegion(string r) =>
            r.StartsWith("N", StringComparison.OrdinalIgnoreCase) ? "North" : "South";

        private static List<LocationDto> MapToList(DataTable table)
        {
            var list = new List<LocationDto>();
            foreach (DataRow row in table.Rows)
            {
                list.Add(new LocationDto
                {
                    LocationID   = (int)row["LocationID"],
                    LocationName = row["LocationName"].ToString() ?? "",
                    Address      = row["Address"] is DBNull ? "" : row["Address"].ToString() ?? "",
                    Latitude     = (double)row["Latitude"],
                    Longitude    = (double)row["Longitude"]
                });
            }
            return list;
        }
    }

    public class LocationDto
    {
        public int    LocationID   { get; set; }
        public string LocationName { get; set; } = "";
        public string Address      { get; set; } = "";
        public double Latitude     { get; set; }
        public double Longitude    { get; set; }
    }

    public class CreateLocationRequest
    {
        public string  LocationName { get; set; } = "";
        public string? Address      { get; set; }
        public double  Latitude     { get; set; }
        public double  Longitude    { get; set; }
        public string? Region       { get; set; }
    }
}
