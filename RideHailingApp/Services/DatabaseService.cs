using SQLite;

namespace multilingualAudioTravelApp.Services;

public class PoiEntity  //POI
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Image { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; }
    public int Priority { get; set; }
    public int CooldownMinutes { get; set; } = 5;
}

public class UserEntity  //user
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string FullName { get; set; }
}
public class DatabaseService
{
    private SQLiteAsyncConnection _db;
    private readonly string _dbPath;

    public DatabaseService()
    {
        _dbPath = Path.Combine(FileSystem.AppDataDirectory, "tourapp.db");
    }

    private async Task InitAsync()
    {
        if (_db != null) return;

        _db = new SQLiteAsyncConnection(_dbPath);
        await _db.CreateTableAsync<PoiEntity>();
        await _db.CreateTableAsync<UserEntity>();

        var count = await _db.Table<PoiEntity>().CountAsync();
        if (count == 0)
            await SeedDataAsync();
    }

    public async Task<List<PoiEntity>> GetAllPoisAsync()
    {
        await InitAsync();
        return await _db.Table<PoiEntity>().ToListAsync();
    }

    public async Task AddPoiAsync(PoiEntity poi)
    {
        await InitAsync();
        await _db.InsertAsync(poi);
    }

    public async Task UpdatePoiAsync(PoiEntity poi)
    {
        await InitAsync();
        await _db.UpdateAsync(poi);
    }

    public async Task DeletePoiAsync(int id)
    {
        await InitAsync();
        await _db.DeleteAsync<PoiEntity>(id);
    }

    private async Task SeedDataAsync()
    {
        var samples = new List<PoiEntity>
        {
            new PoiEntity { Name = "Khu phố ẩm thực Vĩnh Khánh", Description = "Phố ẩm thực nổi tiếng quận 4 với rất nhiều món ngon hấp dẫn.", Image = "vinhkhanh.jpg", Latitude = 10.761923, Longitude = 106.701964, Radius = 100, Priority = 10 },
            new PoiEntity { Name = "Quán Ốc Oanh", Description = "Quán ốc lâu đời và nổi tiếng nhất khu Vĩnh Khánh.", Image = "ocoanh.jpg", Latitude = 10.761410, Longitude = 106.702820, Radius = 80, Priority = 8 },
            new PoiEntity { Name = "Quán Ốc Phát", Description = "Ốc Phát Vĩnh Khánh vẫn luôn là điểm đến quen thuộc cho các tín đồ mê ốc.", Image = "ocphat.jpg", Latitude = 10.761921, Longitude = 106.702151, Radius = 70, Priority = 6 },
            new PoiEntity { Name = "Quán bún cá Châu Đốc", Description = "Nước lèo thanh mát, đậm đà hương vị ăn cùng bún, chả cá và cá.", Image = "bunca.jpg", Latitude = 10.761455, Longitude = 106.702660, Radius = 70, Priority = 6 },
            new PoiEntity { Name = "Quán Ốc Loan", Description = "Không gian thoáng mát, nổi tiếng với nước chấm cực kỳ ngon.", Image = "ocloan.jpg", Latitude = 10.761170, Longitude = 106.702710, Radius = 70, Priority = 6 }
        };
        await _db.InsertAllAsync(samples);
    }

    // Đăng ký tài khoản mới
    public async Task<bool> RegisterAsync(string email, string password, string fullName)
    {
        await InitAsync();

        // Kiểm tra email đã tồn tại chưa
        var existing = await _db.Table<UserEntity>()
            .Where(u => u.Email == email)
            .FirstOrDefaultAsync();

        if (existing != null) return false; // Email đã dùng rồi

        await _db.InsertAsync(new UserEntity
        {
            Email = email,
            Password = password,
            FullName = fullName
        });
        return true;
    }

    // Đăng nhập - kiểm tra email + password
    public async Task<UserEntity> LoginAsync(string email, string password)
    {
        await InitAsync();

        return await _db.Table<UserEntity>()
            .Where(u => u.Email == email && u.Password == password)
            .FirstOrDefaultAsync();
    }

    // Cập nhật tên + email
    public async Task<bool> UpdateProfileAsync(string currentEmail, string newName, string newEmail, string newPassword = null)
    {
        await InitAsync();

        var user = await _db.Table<UserEntity>()
            .Where(u => u.Email == currentEmail)
            .FirstOrDefaultAsync();

        if (user == null) return false;

        if (newEmail != currentEmail)
        {
            var existing = await _db.Table<UserEntity>()
                .Where(u => u.Email == newEmail)
                .FirstOrDefaultAsync();
            if (existing != null) return false;
        }

        user.FullName = newName;
        user.Email = newEmail;

        // Chỉ đổi password nếu người dùng có nhập
        if (!string.IsNullOrEmpty(newPassword))
            user.Password = newPassword;

        await _db.UpdateAsync(user);

        Preferences.Set("userName", newName);
        Preferences.Set("userEmail", newEmail);

        return true;
    }




}