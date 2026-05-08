using RideHailingApi.Services;

namespace RideHailingApi.Middleware
{
    public class MaintenanceMiddleware
    {
        private readonly RequestDelegate _next;

        // POST paths blocked during maintenance
        private static readonly string[] _blockedPaths =
        {
            "/api/trips/book-trip",
            "/api/bookings/scheduled",
            "/api/trips/pool",
        };

        public MaintenanceMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, MaintenanceModeService maintenance)
        {
            if (maintenance.IsActive && context.Request.Method == "POST")
            {
                string path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
                bool blocked = _blockedPaths.Any(p => path.Contains(p.ToLowerInvariant()));

                if (blocked)
                {
                    context.Response.StatusCode  = 503;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(
                        $"{{\"error\":\"MaintenanceMode\",\"message\":\"{maintenance.Message}\"}}");
                    return;
                }
            }

            await _next(context);
        }
    }
}
