# Admin Dashboard - Hướng dẫn sử dụng

## 🎯 Tổng quan

Admin Dashboard là giao diện quản trị web chuyên nghiệp cho hệ thống Ride Hailing, được thiết kế dựa trên các app nổi tiếng như Uber Admin, Grab Admin, và các dashboard hiện đại khác.

## ✨ Tính năng chính

### 1. **Dashboard (Trang chủ)**
- Hiển thị thống kê tổng quan: Tổng Users, Tài xế, Chuyến xe, Doanh thu
- Danh sách chuyến xe gần đây
- Hỗ trợ filter theo Region (North, South, Central)

### 2. **Quản lý Users**
- Xem danh sách toàn bộ users
- Tìm kiếm users theo tên, số điện thoại
- Xóa user
- Phân trang thông minh
- Filter theo Region

### 3. **Quản lý Tài xế**
- Xem danh sách tài xế
- Hiển thị tổng số chuyến của mỗi tài xế
- Xóa tài xế
- Filter theo Region
- Phân trang

### 4. **Quản lý Chuyến xe**
- Xem toàn bộ chuyến xe
- Filter theo trạng thái (Pending, Completed, Cancelled)
- Filter theo Region
- Hiển thị thông tin chi tiết: Khách hàng, Tài xế, Điểm đón/trả
- Phân trang

### 5. **Báo cáo Doanh thu**
- Thống kê doanh thu theo thời gian (Hôm nay, Tuần, Tháng, Năm)
- Biểu đồ doanh thu 7 ngày qua (Chart.js)
- Doanh thu theo từng Region
- Trung bình doanh thu/chuyến

### 6. **Giao diện hiện đại**
- Sidebar có thể ẩn/hiện
- Dark Mode / Light Mode
- Responsive design (Desktop, Tablet, Mobile)
- Animation mượt mà
- Icon đẹp mắt (FontAwesome)

## 🚀 Cách sử dụng

### Truy cập Admin Dashboard

1. Khởi động API:
```bash
cd RideHailingApi
dotnet run
```

2. Mở trình duyệt và truy cập:
```
https://localhost:7249/
hoặc
https://localhost:7249/admin.html
```

### Các API Endpoints

#### Dashboard
- `GET /api/admin/stats?region=South` - Lấy thống kê tổng quan

#### Users
- `GET /api/admin/users?region=South&page=1&pageSize=20` - Lấy danh sách users
- `GET /api/admin/users/{id}?region=South` - Lấy thông tin user
- `DELETE /api/admin/users/{id}?region=South` - Xóa user

#### Drivers
- `GET /api/admin/drivers?region=South&page=1&pageSize=20` - Lấy danh sách tài xế
- `GET /api/admin/drivers/{id}?region=South` - Lấy thông tin tài xế

#### Trips
- `GET /api/admin/trips?region=South&page=1&pageSize=20&status=Pending` - Lấy danh sách chuyến xe

#### Revenue
- `GET /api/admin/revenue?region=South&period=month` - Lấy báo cáo doanh thu
- `GET /api/admin/revenue/chart?region=South&days=7` - Lấy dữ liệu biểu đồ

## 🎨 Thiết kế

### Color Scheme
- **Primary**: `#4f46e5` (Indigo)
- **Secondary**: `#7c3aed` (Purple)
- **Success**: `#10b981` (Green)
- **Warning**: `#f59e0b` (Orange)
- **Danger**: `#ef4444` (Red)
- **Info**: `#3b82f6` (Blue)

### Typography
- Font: Segoe UI
- Responsive font sizes
- Clear hierarchy

### Layout
- **Sidebar**: 260px (collapsed: 70px)
- **Header**: 70px height
- **Content**: Flexible with padding
- **Cards**: 15px border radius, subtle shadow

## 📱 Responsive Breakpoints

- **Desktop**: > 1024px (Full layout)
- **Tablet**: 768px - 1024px (Adjusted grid)
- **Mobile**: < 768px (Stacked layout, hidden sidebar)

## 🌙 Dark Mode

Nhấn nút Moon/Sun ở header để chuyển đổi giữa Light và Dark mode.

## 🔒 Bảo mật

**Lưu ý**: Hiện tại dashboard chưa có authentication. Trong production, cần:
- Thêm JWT authentication
- Role-based access control (RBAC)
- Rate limiting
- Input validation
- HTTPS enforced

## 🛠️ Tùy chỉnh

### Thay đổi màu sắc
Chỉnh sửa CSS variables trong `admin.html`:
```css
:root {
    --primary-color: #4f46e5;
    --secondary-color: #7c3aed;
    /* ... */
}
```

### Thêm menu item
Thêm vào `.sidebar .menu`:
```html
<li class="menu-item" data-view="new-view">
    <i class="fas fa-icon"></i>
    <span>Menu Name</span>
</li>
```

### Thêm view mới
Thêm section trong `.content`:
```html
<div class="view-section" id="new-view-view">
    <!-- Nội dung view -->
</div>
```

## 📊 Công nghệ sử dụng

- **Frontend**: HTML5, CSS3, Vanilla JavaScript
- **Charts**: Chart.js
- **Icons**: FontAwesome 6.4
- **Backend**: ASP.NET Core Web API (.NET 10)
- **Database**: SQL Server với failover support

## 🐛 Xử lý lỗi

Dashboard tự động xử lý các trường hợp:
- API không phản hồi
- Không có dữ liệu
- Lỗi network
- Primary DB sập (chuyển sang Read-Only mode)

## 📝 Ghi chú

1. Dữ liệu được tải tự động khi chuyển view
2. Pagination hoạt động tự động
3. Filter theo Region được lưu trong session
4. Charts tự động refresh khi thay đổi region/period

## 🎯 Best Practices

1. **Performance**: Sử dụng pagination để không tải quá nhiều dữ liệu
2. **UX**: Loading spinner khi fetch data
3. **Accessibility**: Semantic HTML, keyboard navigation
4. **Mobile-first**: Responsive từ mobile lên desktop
5. **Error handling**: Graceful degradation

## 📞 Hỗ trợ

Nếu gặp vấn đề, vui lòng kiểm tra:
1. API có đang chạy không?
2. Database có kết nối được không?
3. Console browser có báo lỗi gì không?
4. Network tab có request nào failed không?

## 🚀 Tính năng tương lai

- [ ] Authentication & Authorization
- [ ] Real-time updates (SignalR)
- [ ] Export dữ liệu (Excel, PDF)
- [ ] Advanced filters & search
- [ ] Notification system
- [ ] User profile management
- [ ] Settings page
- [ ] Multi-language support
- [ ] Advanced analytics

---

**Phiên bản**: 1.0.0  
**Ngày tạo**: 2024  
**Tác giả**: Admin Dashboard Team
