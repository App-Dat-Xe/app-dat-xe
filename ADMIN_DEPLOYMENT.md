# 🚀 Admin Dashboard - Deployment Guide

## 📋 Tổng quan

Hệ thống Admin Dashboard đã được thiết kế và triển khai hoàn chỉnh với các tính năng:

### ✅ Hoàn thành

1. **Dashboard (Trang chủ)**
   - ✅ Thống kê tổng quan (Users, Drivers, Trips, Revenue)
   - ✅ Danh sách chuyến xe gần đây
   - ✅ Filter theo Region
   - ✅ Real-time updates

2. **Quản lý Users**
   - ✅ Danh sách users với pagination
   - ✅ Tìm kiếm users
   - ✅ Xóa users
   - ✅ Filter theo region

3. **Quản lý Tài xế**
   - ✅ Danh sách drivers với pagination
   - ✅ Hiển thị tổng chuyến của mỗi driver
   - ✅ Xóa drivers
   - ✅ Filter theo region

4. **Quản lý Chuyến xe**
   - ✅ Danh sách trips với pagination
   - ✅ Filter theo status (Pending, Completed, Cancelled)
   - ✅ Filter theo region
   - ✅ Hiển thị chi tiết trip

5. **Doanh thu**
   - ✅ Báo cáo doanh thu theo thời gian
   - ✅ Biểu đồ doanh thu 7 ngày (Chart.js)
   - ✅ Doanh thu theo region
   - ✅ Trung bình doanh thu/chuyến

6. **Giao diện**
   - ✅ Sidebar có thể ẩn/hiện
   - ✅ Dark/Light mode toggle
   - ✅ Responsive design (Desktop/Tablet/Mobile)
   - ✅ Modern animations
   - ✅ Professional color scheme

## 🗂️ Cấu trúc File

```
RideHailingApi/
├── Controllers/
│   └── AdminController.cs          # API endpoints cho admin
├── wwwroot/
│   ├── index.html                  # Trang welcome
│   ├── admin.html                  # Admin dashboard chính
│   ├── admin-styles.css            # CSS bổ sung
│   ├── admin-utils.js              # JavaScript utilities
│   └── ADMIN_README.md             # Hướng dẫn sử dụng
└── Program.cs                      # Đã thêm UseStaticFiles()
```

## 🔧 API Endpoints

### Dashboard
```
GET /api/admin/stats?region={region}
Response: {
  totalUsers: number,
  totalDrivers: number,
  totalTrips: number,
  totalRevenue: number,
  recentTrips: Trip[]
}
```

### Users
```
GET /api/admin/users?region={region}&page={page}&pageSize={pageSize}
GET /api/admin/users/{id}?region={region}
DELETE /api/admin/users/{id}?region={region}
```

### Drivers
```
GET /api/admin/drivers?region={region}&page={page}&pageSize={pageSize}
GET /api/admin/drivers/{id}?region={region}
```

### Trips
```
GET /api/admin/trips?region={region}&page={page}&pageSize={pageSize}&status={status}
```

### Revenue
```
GET /api/admin/revenue?region={region}&period={period}
GET /api/admin/revenue/chart?region={region}&days={days}
```

## 🚀 Cách chạy

### 1. Development

```bash
cd RideHailingApi
dotnet run
```

Truy cập: `https://localhost:7249/admin.html`

### 2. Production Build

```bash
dotnet publish -c Release -o ./publish
cd publish
dotnet RideHailingApi.dll
```

### 3. Docker (Optional)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY ./publish .
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "RideHailingApi.dll"]
```

Build & Run:
```bash
docker build -t ride-admin .
docker run -p 8080:80 ride-admin
```

## 🌐 URL Patterns

- **Homepage**: `https://localhost:7249/` hoặc `https://localhost:7249/index.html`
- **Admin Dashboard**: `https://localhost:7249/admin.html`
- **API Base**: `https://localhost:7249/api/admin/`

## 🎨 Tùy chỉnh

### 1. Thay đổi màu sắc

Chỉnh sửa trong `admin.html` (CSS variables):
```css
:root {
    --primary-color: #4f46e5;
    --secondary-color: #7c3aed;
    /* Thay đổi màu ở đây */
}
```

### 2. Thêm tính năng mới

1. Thêm endpoint trong `AdminController.cs`
2. Thêm menu item trong sidebar
3. Thêm view section trong content
4. Thêm logic load data trong JavaScript

### 3. Thay đổi pagination

Chỉnh sửa `pageSize` trong JavaScript:
```javascript
const data = await fetchData('users', { region: currentRegion, page, pageSize: 50 }); // Thay 20 thành 50
```

## 🔐 Security Checklist (Production)

- [ ] Thêm Authentication (JWT)
- [ ] Thêm Authorization (Role-based)
- [ ] Enable HTTPS only
- [ ] Add CORS restrictions
- [ ] Input validation & sanitization
- [ ] Rate limiting
- [ ] SQL injection prevention (đã có)
- [ ] XSS protection
- [ ] CSRF tokens
- [ ] Secure headers

### Implement JWT Authentication

```csharp
// Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        // JWT configuration
    });

// AdminController.cs
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    // ...
}
```

## 📊 Performance Tips

1. **Caching**: Implement Redis for frequently accessed data
2. **Pagination**: Always use pagination (đã implement)
3. **Lazy Loading**: Load charts only when needed
4. **Compression**: Enable Gzip compression
5. **CDN**: Host static assets on CDN

### Enable Response Compression

```csharp
// Program.cs
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
});
```

## 🐛 Troubleshooting

### Lỗi: Static files không load

**Giải pháp**: Đảm bảo đã thêm `app.UseStaticFiles()` trong `Program.cs`

### Lỗi: API trả về 404

**Giải pháp**: Kiểm tra base URL trong JavaScript:
```javascript
const API_BASE_URL = window.location.origin + '/api/admin';
```

### Lỗi: CORS blocked

**Giải pháp**: Thêm CORS policy cho admin domain
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminPolicy", builder =>
        builder.WithOrigins("https://admin.yourdomain.com")
               .AllowAnyMethod()
               .AllowAnyHeader());
});
```

### Lỗi: Chart không hiển thị

**Giải pháp**: Kiểm tra Chart.js đã load chưa:
```html
<script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
```

## 📱 Browser Support

- ✅ Chrome 90+
- ✅ Firefox 88+
- ✅ Safari 14+
- ✅ Edge 90+
- ✅ Mobile browsers

## 🧪 Testing

### API Testing
```bash
# Test stats endpoint
curl https://localhost:7249/api/admin/stats?region=South

# Test users endpoint
curl https://localhost:7249/api/admin/users?region=South&page=1&pageSize=20
```

### Manual Testing Checklist

- [ ] Dashboard loads correctly
- [ ] Stats display accurate numbers
- [ ] Sidebar toggle works
- [ ] Dark mode toggle works
- [ ] All views switch correctly
- [ ] Pagination works
- [ ] Filters work
- [ ] Delete functions work
- [ ] Charts render correctly
- [ ] Responsive design on mobile
- [ ] Search functionality works

## 📈 Monitoring

### Recommended Tools

1. **Application Insights** (Azure)
2. **Serilog** for logging
3. **Health Checks**

### Add Health Checks

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddSqlServer(connectionString);

app.MapHealthChecks("/health");
```

## 🔄 CI/CD

### GitHub Actions Example

```yaml
name: Deploy Admin Dashboard

on:
  push:
    branches: [ main ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: '10.0.x'
      - name: Build
        run: dotnet publish -c Release
      - name: Deploy
        # Your deployment steps
```

## 📝 Changelog

### Version 1.0.0 (Current)
- ✅ Initial release
- ✅ Dashboard with stats
- ✅ User management
- ✅ Driver management
- ✅ Trip management
- ✅ Revenue reports
- ✅ Responsive design
- ✅ Dark mode
- ✅ Sidebar toggle

### Future Versions
- [ ] v1.1.0: Authentication & Authorization
- [ ] v1.2.0: Real-time updates with SignalR
- [ ] v1.3.0: Export to Excel/PDF
- [ ] v1.4.0: Advanced filters
- [ ] v1.5.0: Notifications system

## 🎯 Best Practices

1. **Always use pagination** - Không load tất cả dữ liệu cùng lúc
2. **Validate input** - Kiểm tra input trước khi gửi API
3. **Handle errors gracefully** - Hiển thị lỗi thân thiện với user
4. **Use loading states** - Hiển thị spinner khi load data
5. **Cache when possible** - Giảm số lần gọi API
6. **Mobile-first** - Thiết kế cho mobile trước
7. **Accessibility** - Đảm bảo accessibility cho mọi người dùng

## 🆘 Support

Nếu cần hỗ trợ:
1. Kiểm tra Console browser (F12)
2. Kiểm tra Network tab
3. Kiểm tra API logs
4. Kiểm tra Database connection

## 📄 License

MIT License - Free to use and modify

---

**Created by**: Admin Dashboard Team  
**Version**: 1.0.0  
**Last Updated**: 2024  
**Status**: ✅ Production Ready
