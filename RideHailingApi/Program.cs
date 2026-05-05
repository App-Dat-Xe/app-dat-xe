using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using RideHailingApi.Data;
using Microsoft.EntityFrameworkCore;
using RideHailingApi.Hubs;
using RideHailingApi.Middleware;
using RideHailingApi.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5108");

builder.Services.AddControllers();

// ── Failover Infrastructure ──────────────────────────────────────────────────
builder.Services.AddSingleton<DatabaseRuntimeState>();          // Per-region runtime state
builder.Services.AddSingleton<IDatabaseProbe, SqlDatabaseProbe>(); // SQL health probe
builder.Services.AddSingleton<IConnectionStringResolver, ConnectionStringResolver>(); // CS resolver
builder.Services.AddSingleton<DbConnectionFactory>();           // Raw config reader (còn dùng ở legacy code)
builder.Services.AddSingleton<FailoverSimulator>();             // Admin manual override + event log
builder.Services.AddHostedService<DatabaseFailoverMonitorService>(); // Auto health-check background service
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddScoped<DataConnect>();
// Register EF Core DbContext for ScheduledTrips and future models.
builder.Services.AddDbContext<DataContext>(options =>
{
    // Use South_Primary as default for migrations / administrative tasks.
    var cs = builder.Configuration.GetConnectionString("South_Primary");
    options.UseSqlServer(cs);
});
// ScheduledTripService depends on EF DbContext
builder.Services.AddScoped<ScheduledTripService>();
builder.Services.AddSingleton<FareService>();
builder.Services.AddSingleton<RefreshTokenService>();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.AddFixedWindowLimiter("booking", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
    options.RejectionStatusCode = 429;
});
builder.Services.AddSignalR();

// JWT Authentication
var jwt = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwt["Key"] ?? throw new InvalidOperationException("JwtSettings:Key missing"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.FromSeconds(60)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            },
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = 401;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync(
                    "{\"error\":\"Unauthorized\",\"message\":\"Missing or invalid Authorization header\"}");
            },
            OnForbidden = ctx =>
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync(
                    "{\"error\":\"Forbidden\",\"message\":\"You do not have permission to access this resource\"}");
            }
        };
    });

builder.Services.AddAuthorization();

// Tighten CORS: read allowed origins from configuration
var allowed = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
if (allowed == null || allowed.Length == 0)
{
    // safe default for local development
    allowed = new[] { "https://localhost:7285", "http://localhost:5108", "http://192.168.1.121:5108" };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", p => p
        .WithOrigins(allowed)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddOpenApi();
var app = builder.Build();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.UseHttpsRedirection();
app.UseDefaultFiles();  // Enable serving index.html by default
app.UseStaticFiles();   // Enable static files (admin dashboard, styles, etc)
app.UseRateLimiter();
app.UseCors("DefaultCorsPolicy");
app.UseMiddleware<RegionMiddleware>();
app.UseMiddleware<DegradedModeMiddleware>();
app.UseAuthorization();
app.MapControllers();
// Expose admin debug endpoints only in Development environment
if (app.Environment.IsDevelopment())
{
    // admin/debug controller is protected by JWT role; keep available for local testing
}
app.MapHub<TripHub>("/hubs/trip");
app.MapGet("/admin", () => Results.Redirect("/admin.html"));
app.Run();
