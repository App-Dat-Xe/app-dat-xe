namespace RideHailingApi.Middleware
{
    // Đọc header X-Region và gắn vào HttpContext.Items["Region"].
    // Controller dùng HttpContext.Items["Region"] để chọn DB cluster (North/South).
    public class RegionMiddleware
    {
        private const string HeaderName = "X-Region";
        private const string DefaultRegion = "South";
        private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "North", "South" };

        private readonly RequestDelegate _next;

        public RegionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string region = DefaultRegion;
            if (context.Request.Headers.TryGetValue(HeaderName, out var value))
            {
                var raw = value.ToString().Trim();
                if (Allowed.Contains(raw))
                {
                    // Chuẩn hoá viết hoa chữ đầu (north → North)
                    region = char.ToUpper(raw[0]) + raw.Substring(1).ToLower();
                }
            }
            context.Items["Region"] = region;
            await _next(context);
        }
    }

    public static class RegionContextExtensions
    {
        // Helper để controller lấy Region nhanh: HttpContext.GetRegion()
        public static string GetRegion(this HttpContext context)
        {
            return context.Items["Region"] as string ?? "South";
        }
    }
}
