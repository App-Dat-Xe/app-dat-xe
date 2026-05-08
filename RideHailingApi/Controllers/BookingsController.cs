using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RideHailingApi.Models;
using RideHailingApi.Services;

namespace RideHailingApi.Controllers
{
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Route("api/[controller]")]
    public class BookingsController : ControllerBase
    {
        private readonly ScheduledTripService _service;

        public BookingsController(ScheduledTripService service)
        {
            _service = service;
        }

        [Authorize]
        [HttpPost("scheduled")]
        public async Task<IActionResult> CreateScheduled([FromBody] CreateScheduledTripRequest req)
        {
            // Basic validation
            if (req.ScheduledPickupTime <= DateTime.UtcNow.AddMinutes(14))
                return BadRequest(new { error = "ScheduledPickupTime must be at least 15 minutes in the future." });

            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            if (userId == 0) return Unauthorized();

            var trip = new ScheduledTrip
            {
                UserId = userId,
                PickupAddress = req.PickupAddress,
                PickupLat = req.PickupLat,
                PickupLng = req.PickupLng,
                DropoffAddress = req.DropoffAddress,
                DropoffLat = req.DropoffLat,
                DropoffLng = req.DropoffLng,
                VehicleType = req.VehicleType,
                ScheduledPickupTime = req.ScheduledPickupTime,
                DistanceKm = req.DistanceKm,
                Status = "Scheduled",
                Region = HttpContext.Items["Region"] as string ?? "South"
            };

            var created = await _service.CreateAsync(trip);
            return Ok(new { scheduledTripId = created.ScheduledTripId });
        }

        [Authorize]
        [HttpGet("scheduled")]
        public async Task<IActionResult> GetScheduled()
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            if (userId == 0) return Unauthorized();
            var list = await _service.GetByUserAsync(userId);
            var dto = list.Select(s => new ScheduledTripDto
            {
                ScheduledTripId = s.ScheduledTripId,
                UserId = s.UserId,
                PickupAddress = s.PickupAddress,
                DropoffAddress = s.DropoffAddress,
                VehicleType = s.VehicleType,
                ScheduledPickupTime = s.ScheduledPickupTime.ToString("o"),
                Status = s.Status,
                Region = s.Region,
                EstimatedFare = s.EstimatedFare,
                DistanceKm = s.DistanceKm,
                TripId = s.TripId,
                CreatedAt = s.CreatedAt.ToString("o"),
                CanCancel = s.Status == "Scheduled"
            }).ToList();
            return Ok(dto);
        }

        [Authorize]
        [HttpDelete("scheduled/{id:int}")]
        public async Task<IActionResult> DeleteScheduled(int id)
        {
            var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
            if (userId == 0) return Unauthorized();
            var ok = await _service.DeleteAsync(id, userId);
            if (!ok) return BadRequest(new { error = "Unable to cancel scheduled trip. It may have been already processed or does not belong to you." });
            return Ok(new { message = "Cancelled" });
        }
    }
}
