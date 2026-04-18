using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace RideHailingApi.Services
{
    public class DbConnectionFactory
    {
        private readonly IConfiguration _config;
        private string _workingServer = ""; // Biến lưu lại Server nào đang sống để dùng cho lần sau

        public DbConnectionFactory(IConfiguration config)
        {
            _config = config;
        }

        public string GetConnectionString(string region, bool isFailover = false)
        {
            string dbType = isFailover ? "Replica" : "Primary";
            string baseConnStr = _config.GetConnectionString($"{region}_{dbType}") ?? "";

            // Lấy danh sách các Server có thể có từ appsettings.json
            var servers = _config.GetSection("AvailableServers").Get<string[]>();

            // Nếu đã tìm thấy server sống từ trước, dùng luôn cho lẹ
            if (!string.IsNullOrEmpty(_workingServer))
            {
                return $"Server={_workingServer};{baseConnStr}";
            }

            // Nếu chưa biết server nào sống, đi gõ cửa từng cái
            foreach (var server in servers)
            {
                string testConnStr = $"Server={server};{baseConnStr};Connection Timeout=1"; // Timeout=1 để dò cho nhanh
                try
                {
                    using var conn = new SqlConnection(testConnStr);
                    conn.Open(); // Thử mở cửa

                    // Nếu không văng lỗi tức là nhà này có người!
                    _workingServer = server; // Lưu lại để lần sau không phải dò nữa
                    return $"Server={server};{baseConnStr}";
                }
                catch (SqlException)
                {
                    // Lỗi thì bỏ qua, đi gõ cửa nhà tiếp theo
                    continue;
                }
            }

            // Nếu gõ cửa cả 3 nhà mà không ai trả lời
            throw new Exception("Không tìm thấy SQL Server nào đang chạy trên máy tính này!");
        }
    }
}