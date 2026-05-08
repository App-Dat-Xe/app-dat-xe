using RideHailingApi.Services;

namespace RideHailingApi.Middleware
{
    // Chặn tất cả POST/PUT/PATCH/DELETE khi region đang chạy Backup DB (DegradedMode = true).
    // GET vẫn được phép đi qua để đọc dữ liệu từ Replica.
    // Các path đặc biệt (health check, admin manual override) được miễn trừ.
    public class DegradedModeMiddleware
    {
        private static readonly HashSet<string> ExemptPrefixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "/health",
            "/api/admin",   // Admin vẫn cần gọi simulate-up để tắt degraded mode
            "/api/auth",    // Allow auth endpoints (login/register) to work in degraded/dev scenarios
            "/hubs"         // SignalR hub không bị ảnh hưởng
        };

        private readonly RequestDelegate _next;

        public DegradedModeMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, IConnectionStringResolver resolver)
        {
            // Lấy region từ header (đã được RegionMiddleware parse)
            string region = context.Items["Region"] as string ?? "South";
            bool isDegraded = resolver.IsDegradedMode(region);

            // Gắn thông tin trạng thái DB vào Header của MỌI response để Frontend cập nhật thời gian thực
            context.Response.OnStarting(() => {
                context.Response.Headers["X-Database-Degraded"] = isDegraded.ToString().ToLower();
                return Task.CompletedTask;
            });

            var method = context.Request.Method.ToUpperInvariant();
            bool isWrite = method is "POST" or "PUT" or "PATCH" or "DELETE";

            if (!isWrite)
            {
                await _next(context);
                return;
            }

            // Kiểm tra miễn trừ
            var path = context.Request.Path.Value ?? "";
            if (ExemptPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            // Diagnostic: log resolver state to help debug unexpected 503
            try
            {
                var logger = context.RequestServices.GetService(typeof(Microsoft.Extensions.Logging.ILogger<DegradedModeMiddleware>)) as Microsoft.Extensions.Logging.ILogger;
                var target = resolver.GetCurrentTarget(region);
                logger?.LogDebug("DegradedModeMiddleware: method={Method} region={Region} isDegraded={IsDegraded} target={Target}", method, region, resolver.IsDegradedMode(region), target);
            }
            catch { }

            if (resolver.IsDegradedMode(region))
            {
                var target = resolver.GetCurrentTarget(region);
                string modeDesc = target == DatabaseTarget.None ? "Unavailable" : "Degraded";

                // Log at Information/Warning so it's visible in console
                try
                {
                    var logger = context.RequestServices.GetService(typeof(Microsoft.Extensions.Logging.ILogger<DegradedModeMiddleware>)) as Microsoft.Extensions.Logging.ILogger;
                    logger?.LogWarning("DegradedModeMiddleware: blocking write request {Method} {Path} for region {Region} — target={Target} isDegraded={IsDegraded}", context.Request.Method, context.Request.Path, region, target, resolver.IsDegradedMode(region));
                }
                catch { }

                context.Response.StatusCode  = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";

                string message = target == DatabaseTarget.None
                    ? "Hiện tại hệ thống không thể truy cập cơ sở dữ liệu. Vui lòng thử lại sau."
                    : "Hệ thống đang chạy trên máy chủ dự phòng. Chức năng ghi tạm thời bị khóa.";

                await context.Response.WriteAsJsonAsync(new
                {
                    error   = "DegradedMode",
                    mode    = modeDesc,
                    region,
                    message
                });
                return;
            }

            await _next(context);
        }
    }
}
