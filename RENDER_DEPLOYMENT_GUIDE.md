# 🚀 Hướng dẫn Deploy Admin Dashboard lên Render

## ✅ Đã Fix lỗi Local

### Vấn đề: Không truy cập được web local
**Nguyên nhân**: Thiếu `UseDefaultFiles()` để serve `index.html` mặc định

**Đã fix**: 
- ✅ Thêm `app.UseDefaultFiles()` vào `Program.cs`
- ✅ Sắp xếp lại middleware order đúng
- ✅ Cấu hình Kestrel để hỗ trợ Render PORT

### Cách test local:

```bash
cd RideHailingApi
dotnet run
```

Truy cập:
- **Homepage**: http://localhost:5108
- **Admin Dashboard**: http://localhost:5108/admin.html
- **HTTPS**: https://localhost:7285
- **HTTPS Admin**: https://localhost:7285/admin.html

---

## 🌐 Deploy lên Render - Có thể!

### Bước 1: Push code lên GitHub

```bash
git add .
git commit -m "Add admin dashboard and Render config"
git push origin main
```

### Bước 2: Tạo tài khoản Render

1. Truy cập: https://render.com
2. Sign up hoặc login với GitHub
3. Authorize Render để access repository của bạn

### Bước 3: Tạo Web Service mới

1. Click **"New +"** → **"Web Service"**
2. Chọn repository: `app-dat-xe`
3. Điền thông tin:

**Basic Settings:**
- **Name**: `ride-hailing-admin` (hoặc tên bạn muốn)
- **Region**: Singapore / Oregon (gần VN nhất)
- **Branch**: `main`
- **Root Directory**: để trống (hoặc `RideHailingApi`)
- **Runtime**: `.NET`

**Build & Deploy:**
- **Build Command**: 
  ```bash
  dotnet publish RideHailingApi/RideHailingApi.csproj -c Release -o out
  ```

- **Start Command**: 
  ```bash
  dotnet out/RideHailingApi.dll
  ```

**Environment Variables** (bắt buộc):
```
ASPNETCORE_ENVIRONMENT = Production
ASPNETCORE_URLS = http://0.0.0.0:$PORT

# Database connections (thay bằng connection string thật của bạn)
ConnectionStrings__NorthPrimary = Server=your-server;Database=RideHailingDB;...
ConnectionStrings__NorthReplica = Server=your-replica;Database=RideHailingDB;...
ConnectionStrings__SouthPrimary = Server=your-server;Database=RideHailingDB;...
ConnectionStrings__SouthReplica = Server=your-replica;Database=RideHailingDB;...
ConnectionStrings__CentralPrimary = Server=your-server;Database=RideHailingDB;...
ConnectionStrings__CentralReplica = Server=your-replica;Database=RideHailingDB;...
```

4. Click **"Create Web Service"**

### Bước 4: Chờ deployment

- Render sẽ tự động build và deploy
- Thời gian deploy: ~5-10 phút
- Theo dõi logs trong dashboard

### Bước 5: Truy cập ứng dụng

Sau khi deploy thành công:
- **URL**: `https://ride-hailing-admin.onrender.com`
- **Admin Dashboard**: `https://ride-hailing-admin.onrender.com/admin.html`

---

## 📦 Plan đề xuất cho Render

### 🆓 Free Plan (Đủ dùng cho demo/testing)
- **Cost**: $0/month
- **Specs**: 
  - 512 MB RAM
  - CPU shared
  - Auto-sleep sau 15 phút không hoạt động
  - 750 hours/month
- **Giới hạn**:
  - App sẽ sleep khi không dùng
  - Khởi động lại ~30s khi có request mới
  - Không có custom domain

### 💎 Starter Plan (Khuyên dùng cho production)
- **Cost**: $7/month
- **Specs**:
  - 512 MB RAM
  - CPU dedicated
  - Không sleep
  - Custom domain
  - SSL tự động
- **Phù hợp**: Ứng dụng nhỏ, admin dashboard

### 🚀 Standard Plan
- **Cost**: $25/month
- **Specs**:
  - 2 GB RAM
  - Scaling options
  - Priority support
- **Phù hợp**: Production với nhiều traffic

---

## 🗄️ Database trên Render

### Option 1: Render PostgreSQL (Khuyên dùng)

1. Tạo PostgreSQL database trên Render
2. Click **"New +"** → **"PostgreSQL"**
3. Free plan: 256 MB, expire sau 90 ngày
4. Paid plan: $7/month (1 GB)

**Lưu ý**: Render chỉ hỗ trợ PostgreSQL, không hỗ trợ SQL Server

### Option 2: Azure SQL Database (Khuyên dùng cho .NET)

Vì app hiện tại dùng SQL Server, nên tốt nhất:
1. Tạo Azure SQL Database
2. Sử dụng connection string từ Azure
3. Cấu hình connection strings trong Render environment variables

### Option 3: Railway.app (Alternative)

Railway hỗ trợ tốt hơn cho .NET + SQL Server:
- **Cost**: ~$5/month
- Hỗ trợ SQL Server container
- Deploy dễ hơn cho .NET apps

---

## 🔧 Cấu hình bổ sung cho Production

### 1. Update DbConnectionFactory.cs

Đảm bảo connection strings được đọc từ environment variables:

```csharp
var primary = configuration[$"ConnectionStrings:{region}Primary"] 
              ?? throw new Exception($"Missing connection string for {region}Primary");
```

### 2. Update appsettings.Production.json

Tạo file này nếu chưa có:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "NorthPrimary": "USE_ENVIRONMENT_VARIABLE",
    "NorthReplica": "USE_ENVIRONMENT_VARIABLE",
    "SouthPrimary": "USE_ENVIRONMENT_VARIABLE",
    "SouthReplica": "USE_ENVIRONMENT_VARIABLE",
    "CentralPrimary": "USE_ENVIRONMENT_VARIABLE",
    "CentralReplica": "USE_ENVIRONMENT_VARIABLE"
  }
}
```

### 3. Enable HTTPS Redirect (Production)

```csharp
// Program.cs - already enabled
app.UseHttpsRedirection();
```

### 4. Add Health Check

```csharp
// Program.cs
builder.Services.AddHealthChecks();

app.MapHealthChecks("/health");
```

---

## 🎯 Alternative: Railway.app (Dễ hơn cho .NET)

### Ưu điểm Railway:
- ✅ Hỗ trợ .NET tốt hơn
- ✅ Có thể chạy SQL Server container
- ✅ Deploy đơn giản hơn
- ✅ Free $5 credit/month
- ✅ Pricing rõ ràng: ~$5-10/month

### Cách deploy lên Railway:

1. Truy cập: https://railway.app
2. Login với GitHub
3. Click **"New Project"** → **"Deploy from GitHub repo"**
4. Chọn repository
5. Railway tự động detect .NET và deploy
6. Thêm environment variables
7. Deploy xong!

**URL**: `https://[your-app].up.railway.app/admin.html`

---

## 🔐 Security Checklist trước khi deploy

- [ ] Thêm authentication (JWT)
- [ ] Validate input
- [ ] Enable HTTPS only
- [ ] Secure connection strings (dùng environment variables)
- [ ] Add rate limiting
- [ ] Enable CORS cho specific domains
- [ ] Add logging (Serilog)
- [ ] Error handling
- [ ] Health checks
- [ ] Monitoring

---

## 📊 So sánh nền tảng

| Platform | .NET Support | SQL Server | Price | Ease | Recommend |
|----------|-------------|------------|-------|------|-----------|
| **Render** | ⭐⭐⭐ | ❌ (PostgreSQL only) | $0-7/mo | ⭐⭐⭐ | ✅ Nếu dùng PostgreSQL |
| **Railway** | ⭐⭐⭐⭐⭐ | ✅ (Container) | $5-10/mo | ⭐⭐⭐⭐⭐ | ✅✅ Best cho .NET |
| **Azure** | ⭐⭐⭐⭐⭐ | ✅ Native | $10+/mo | ⭐⭐⭐ | ✅ Production tốt nhất |
| **Heroku** | ⭐⭐ | ❌ | Expensive | ⭐⭐ | ❌ Không khuyên dùng |

---

## 🚀 Khuyến nghị

### Cho Demo/Testing:
**→ Render Free Plan**
- Deploy admin dashboard
- Dùng PostgreSQL của Render
- Chuyển đổi app sang dùng PostgreSQL (hoặc dùng Azure SQL)

### Cho Production:
**→ Railway.app hoặc Azure**
- Railway: Dễ deploy, giá hợp lý ($5-10/mo)
- Azure: Production-grade, scale tốt ($10+/mo)

---

## 📝 Checklist Deploy

### Trước khi deploy:
- [x] Code đã push lên GitHub
- [x] `Program.cs` đã config PORT
- [x] `UseDefaultFiles()` và `UseStaticFiles()` đã thêm
- [ ] Connection strings sẵn sàng
- [ ] Environment variables chuẩn bị

### Sau khi deploy:
- [ ] Test admin dashboard: `/admin.html`
- [ ] Test API endpoints: `/api/admin/stats`
- [ ] Test mobile app connection
- [ ] Kiểm tra logs
- [ ] Monitor performance

---

## 🆘 Troubleshooting

### Lỗi: App không start
**Solution**: Check logs trong Render dashboard, đảm bảo environment variables đúng

### Lỗi: Database connection failed
**Solution**: Kiểm tra connection strings, đảm bảo SQL Server/PostgreSQL accessible từ Render IP

### Lỗi: Static files không load
**Solution**: Đảm bảo wwwroot folder được include trong publish

### Lỗi: CORS blocked
**Solution**: Update CORS policy trong `Program.cs` để allow Render domain

---

## 📞 Support

- **Render Docs**: https://render.com/docs
- **Railway Docs**: https://docs.railway.app
- **Azure Docs**: https://docs.microsoft.com/azure

---

**✅ Đã sẵn sàng deploy!**

**Quick start**:
```bash
# 1. Test local
dotnet run

# 2. Push to GitHub
git add .
git commit -m "Ready for deployment"
git push origin main

# 3. Deploy trên Render/Railway
# Follow steps ở trên

# 4. Enjoy! 🎉
```
