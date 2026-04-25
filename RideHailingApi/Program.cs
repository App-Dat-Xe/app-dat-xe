using RideHailingApi.Data;
using RideHailingApi.Middleware;
using RideHailingApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddSingleton<FailoverSimulator>();   // Singleton — giữ trạng thái xuyên suốt app
builder.Services.AddScoped<DataConnect>();

// CORS — cho phép MAUI client gọi từ Android emulator / Windows desktop
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseStaticFiles();   // Phục vụ file tĩnh từ thư mục wwwroot (admin.html)
app.UseCors();
app.UseMiddleware<RegionMiddleware>();
app.UseAuthorization();
app.MapControllers();

// Redirect /admin → /admin.html cho tiện
app.MapGet("/admin", () => Results.Redirect("/admin.html"));

app.Run();
