using RideHailingApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Đăng ký các dịch vụ
builder.Services.AddControllers();
builder.Services.AddSingleton<DbConnectionFactory>();

// Dùng OpenAPI có sẵn của .NET bản mới (thay cho Swagger)
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();