using Asp.Versioning;
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
builder.Services.AddSingleton<DatabaseRuntimeState>();
builder.Services.AddSingleton<IDatabaseProbe, SqlDatabaseProbe>();
builder.Services.AddSingleton<IConnectionStringResolver, ConnectionStringResolver>();
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<FailoverSimulator>();
builder.Services.AddHostedService<DatabaseFailoverMonitorService>();
// ─────────────────────────────────────────────────────────────────────────────

builder.Services.AddScoped<DataConnect>();

// EF Core DbContext for ScheduledTrips
builder.Services.AddDbContext<DataContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("South_Primary");
    options.UseSqlServer(cs);
});

builder.Services.AddScoped<ScheduledTripService>();
builder.Services.AddSingleton<FareService>();
builder.Services.AddSingleton<RefreshTokenService>();
builder.Services.AddSingleton<MaintenanceModeService>();

// ── Email & Auth Token Services ──────────────────────────────────────────────
builder.Services.AddSingleton<EmailTokenService>();
builder.Services.AddTransient<IEmailService, SmtpEmailService>();

// ── FCM Push Notifications ───────────────────────────────────────────────────
builder.Services.AddSingleton<DeviceTokenStore>();
builder.Services.AddScoped<IFcmNotificationService, FcmNotificationService>();
builder.Services.AddHttpClient("fcm");

// ── Scheduled Trip Dispatcher ────────────────────────────────────────────────
builder.Services.AddHostedService<ScheduledTripDispatcherService>();

// ── Rate Limiting ────────────────────────────────────────────────────────────
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

// ── JWT Authentication ────────────────────────────────────────────────────────
var jwt = builder.Configuration.GetSection("JwtSettings");
var key = Encoding.UTF8.GetBytes(jwt["Key"] ?? throw new InvalidOperationException("JwtSettings:Key missing"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwt["Issuer"],
            ValidAudience            = jwt["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(key),
            ClockSkew                = TimeSpan.FromSeconds(60)
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
                ctx.Response.StatusCode  = 401;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync(
                    "{\"error\":\"Unauthorized\",\"message\":\"Missing or invalid Authorization header\"}");
            },
            OnForbidden = ctx =>
            {
                ctx.Response.StatusCode  = 403;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync(
                    "{\"error\":\"Forbidden\",\"message\":\"You do not have permission to access this resource\"}");
            }
        };
    });

builder.Services.AddAuthorization();

// ── API Versioning ────────────────────────────────────────────────────────────
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion                  = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions                  = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-API-Version"),
        new QueryStringApiVersionReader("api-version")
    );
}).AddMvc();

// ── CORS ──────────────────────────────────────────────────────────────────────
var allowed = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
if (allowed == null || allowed.Length == 0)
    allowed = new[] { "https://localhost:7285", "http://localhost:5108", "http://192.168.1.121:5108" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", p => p
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.AddOpenApi();

var app = builder.Build();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseCors("DefaultCorsPolicy");
app.UseMiddleware<RegionMiddleware>();
app.UseMiddleware<DegradedModeMiddleware>();
app.UseMiddleware<MaintenanceMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<TripHub>("/hubs/trip");
app.MapGet("/admin", () => Results.Redirect("/admin.html"));
app.Run();
