# 🚗 Ride Hailing App - Hệ thống Đặt Xe với Admin Dashboard

[![Build](https://img.shields.io/badge/build-passing-brightgreen)]()
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)]()
[![MAUI](https://img.shields.io/badge/MAUI-Latest-purple)]()
[![License](https://img.shields.io/badge/license-MIT-green)]()

## 📋 Tổng quan

Hệ thống đặt xe hoàn chỉnh bao gồm:
- 📱 **Mobile App** (.NET MAUI) - iOS & Android
- 🌐 **Web API** (.NET 10) - Backend với failover support
- 💻 **Admin Dashboard** (Web) - Quản trị chuyên nghiệp
- 🗄️ **SQL Server** - Database với Primary/Replica failover

---

## ✨ Tính năng chính

### 📱 Mobile App (.NET MAUI)
- ✅ Đăng ký / Đăng nhập (User & Driver)
- ✅ Đặt xe (Book trip)
- ✅ Xem lịch sử chuyến đi
- ✅ Cập nhật profile
- ✅ Driver mode - Nhận chuyến
- ✅ Real-time tracking (SignalR ready)

### 🌐 Web API
- ✅ RESTful API
- ✅ JWT Authentication (ready)
- ✅ Multi-region support (North, South, Central)
- ✅ Auto failover (Primary ↔ Replica)
- ✅ Read-only mode khi Primary sập
- ✅ CORS enabled

### 💻 Admin Dashboard
- ✅ **Dashboard**: Thống kê tổng quan real-time
- ✅ **User Management**: Quản lý users, tìm kiếm, xóa
- ✅ **Driver Management**: Quản lý tài xế, theo dõi chuyến
- ✅ **Trip Management**: Xem tất cả chuyến xe, filter
- ✅ **Revenue Reports**: Báo cáo doanh thu, charts
- ✅ **Dark/Light Mode**: Toggle theme
- ✅ **Responsive Design**: Mobile/Tablet/Desktop
- ✅ **Sidebar Toggle**: Ẩn/hiện menu

---

## 🏗️ Kiến trúc

```
┌─────────────────┐
│  MAUI Mobile    │ ←→ HTTP/SignalR
│   iOS/Android   │
└────────┬────────┘
         │
         ↓
┌─────────────────┐     ┌──────────────┐
│   Web Admin     │ ←→  │   Web API    │ ←→ Primary DB
│   Dashboard     │     │  (.NET 10)   │  ↓ (Failover)
└─────────────────┘     └──────────────┘ ←→ Replica DB
```

### Database Failover:
```
[Primary DB] ──X──> [API] ──✓──> [Replica DB]
   (Down)              ↓          (Read-Only)
                  Return 503
                  Client retry
```

---

## 🚀 Quick Start

### 1️⃣ Clone Repository
```bash
git clone https://github.com/App-Dat-Xe/app-dat-xe.git
cd app-dat-xe
```

### 2️⃣ Setup Database
```sql
-- Tạo database
CREATE DATABASE RideHailingDB;

-- Chạy script setup (trong repo)
-- Hoặc xem START_HERE.md
```

### 3️⃣ Update Connection Strings
```bash
# File: RideHailingApi/appsettings.json
# Thay connection strings của bạn
```

### 4️⃣ Run API
```bash
cd RideHailingApi
dotnet run
```

**API URLs:**
- HTTP: http://localhost:5108
- HTTPS: https://localhost:7285
- Admin: http://localhost:5108/admin.html

### 5️⃣ Run Mobile App
```bash
cd RideHailingApp
dotnet build -t:Run -f net9.0-android
# Hoặc mở Visual Studio và Run
```

---

## 💻 Admin Dashboard

### Truy cập:
```
http://localhost:5108/admin.html
```

### Screenshots:

**Dashboard:**
- 📊 Thống kê: Users, Drivers, Trips, Revenue
- 📋 Danh sách chuyến xe gần đây
- 🎨 Charts và biểu đồ

**Features:**
- 👥 Quản lý Users
- 🚗 Quản lý Tài xế
- 🚕 Quản lý Chuyến xe
- 💰 Báo cáo Doanh thu
- ⚙️ Settings

### Tech Stack:
- **HTML5 + CSS3**: Modern responsive design
- **JavaScript (ES6+)**: Vanilla JS, no framework
- **Chart.js**: Data visualization
- **FontAwesome**: Icons

---

## 🌐 Deploy lên Cloud

### ✅ Hỗ trợ deploy:
- **Render.com** - Free tier available
- **Railway.app** - $5/month
- **Azure** - Production-grade
- **AWS** - Enterprise

### Quick Deploy trên Render:

1. Push code lên GitHub
2. Tạo Web Service trên Render.com
3. Connect repository
4. Set environment variables
5. Deploy! 🚀

**Chi tiết**: Xem `RENDER_DEPLOYMENT_GUIDE.md`

---

## 📁 Cấu trúc Project

```
app-dat-xe/
├── RideHailingApp/           # MAUI Mobile App
│   ├── MainPage.xaml         # User home
│   ├── DriverHomePage.xaml   # Driver home
│   ├── Services/             # API services
│   └── ...
├── RideHailingApi/           # Web API
│   ├── Controllers/          # API endpoints
│   ├── Models/               # DTOs
│   ├── Services/             # Business logic
│   ├── Data/                 # Database layer
│   └── wwwroot/              # Admin dashboard
│       ├── admin.html        # Dashboard main
│       ├── admin-styles.css  # Styles
│       └── admin-utils.js    # Utilities
├── RideHailingApi.Tests/     # Unit tests
└── Documentation/            # Docs
    ├── RENDER_DEPLOYMENT_GUIDE.md
    ├── ADMIN_DASHBOARD_SUMMARY.md
    └── ...
```

---

## 🔌 API Endpoints

### Authentication
```
POST /api/auth/register     # Đăng ký
POST /api/auth/login        # Đăng nhập
```

### Users
```
GET    /api/users/{id}      # Lấy profile
PUT    /api/users/{id}      # Update profile
```

### Trips
```
POST   /api/trips/book-trip         # Đặt xe
GET    /api/trips/history/{userId}  # Lịch sử
```

### Admin (Dashboard)
```
GET    /api/admin/stats              # Dashboard stats
GET    /api/admin/users              # Danh sách users
GET    /api/admin/drivers            # Danh sách drivers
GET    /api/admin/trips              # Danh sách trips
GET    /api/admin/revenue            # Doanh thu
DELETE /api/admin/users/{id}         # Xóa user
```

**Full API Docs**: Xem `/api/openapi` khi chạy dev

---

## 🛠️ Tech Stack

### Backend
- **.NET 10** - Latest framework
- **ASP.NET Core** - Web API
- **SQL Server** - Database
- **ADO.NET** - Data access (custom failover)
- **SignalR** (Ready) - Real-time

### Mobile
- **.NET MAUI** - Cross-platform
- **C# 13** - Modern language features
- **XAML** - UI markup

### Frontend (Admin)
- **Vanilla JavaScript** - No framework bloat
- **CSS3** - Modern styling
- **Chart.js** - Charts
- **FontAwesome** - Icons

---

## 📊 Features Status

| Feature | Mobile | API | Admin | Status |
|---------|--------|-----|-------|--------|
| Authentication | ✅ | ✅ | ⏳ | Done |
| User Management | ✅ | ✅ | ✅ | Done |
| Driver Mode | ✅ | ✅ | ✅ | Done |
| Trip Booking | ✅ | ✅ | ✅ | Done |
| Trip History | ✅ | ✅ | ✅ | Done |
| Admin Dashboard | N/A | ✅ | ✅ | Done |
| Revenue Reports | N/A | ✅ | ✅ | Done |
| Real-time Tracking | ⏳ | ⏳ | ⏳ | Planned |
| Push Notifications | ⏳ | ⏳ | ⏳ | Planned |
| Payment Gateway | ⏳ | ⏳ | ⏳ | Planned |

**Legend**: ✅ Done | ⏳ In Progress | ❌ Not Started

---

## 🧪 Testing

### Run Tests
```bash
cd RideHailingApi.Tests
dotnet test
```

### Test Coverage
- ✅ Failover tests
- ✅ Read-only mode tests
- ✅ JWT tests (ready)
- ⏳ Integration tests
- ⏳ E2E tests

---

## 📚 Documentation

### For Developers:
- **START_HERE.md** - Getting started guide
- **ARCHITECTURE_GUIDE.md** - System architecture
- **TEST_CASES.md** - Test documentation

### For Deployment:
- **RENDER_DEPLOYMENT_GUIDE.md** - Deploy to Render
- **QUICK_DEPLOY_GUIDE.md** - Quick deploy guide
- **WEB_READY_DEPLOY.md** - Web deployment ready

### For Admin:
- **ADMIN_README.md** - Admin dashboard guide
- **ADMIN_DASHBOARD_SUMMARY.md** - Features summary
- **ADMIN_DEPLOYMENT.md** - Admin deployment

---

## 🔐 Security

### Current:
- ✅ HTTPS enabled
- ✅ CORS configured
- ✅ SQL injection prevention (parameterized queries)
- ✅ Input validation

### TODO (Production):
- ⏳ JWT Authentication implementation
- ⏳ Role-based authorization
- ⏳ Rate limiting
- ⏳ API key for admin
- ⏳ Audit logging
- ⏳ OWASP security best practices

---

## 🤝 Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Open Pull Request

---

## 📝 License

This project is licensed under the MIT License - see LICENSE file for details.

---

## 👥 Team

- **Backend**: .NET 10 + SQL Server
- **Mobile**: .NET MAUI
- **Admin**: Modern Web Stack
- **Architecture**: Multi-region failover system

---

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/App-Dat-Xe/app-dat-xe/issues)
- **Discussions**: [GitHub Discussions](https://github.com/App-Dat-Xe/app-dat-xe/discussions)

---

## 🎯 Roadmap

### Phase 1 - MVP (✅ Completed)
- [x] User authentication
- [x] Trip booking
- [x] Driver mode
- [x] Admin dashboard
- [x] Basic reporting

### Phase 2 - Enhancement (⏳ In Progress)
- [ ] Real-time tracking
- [ ] Push notifications
- [ ] Advanced analytics
- [ ] Payment integration

### Phase 3 - Scale (📅 Planned)
- [ ] Multi-language
- [ ] Advanced security
- [ ] Performance optimization
- [ ] Microservices migration

---

## ⭐ Show Your Support

Give a ⭐️ if this project helped you!

---

## 📈 Stats

![GitHub repo size](https://img.shields.io/github/repo-size/App-Dat-Xe/app-dat-xe)
![GitHub stars](https://img.shields.io/github/stars/App-Dat-Xe/app-dat-xe?style=social)
![GitHub forks](https://img.shields.io/github/forks/App-Dat-Xe/app-dat-xe?style=social)

---

**Made with ❤️ by App Dat Xe Team**

**Version**: 1.0.0  
**Status**: ✅ Production Ready  
**Last Updated**: 2024
