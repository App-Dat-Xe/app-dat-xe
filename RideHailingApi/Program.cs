using RideHailingApi.Data;
using RideHailingApi.Middleware;
using RideHailingApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<DbConnectionFactory>();
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
app.UseCors();
app.UseMiddleware<RegionMiddleware>();   // Đọc X-Region header trước khi vào controller
app.UseAuthorization();
app.MapControllers();
app.Run();
