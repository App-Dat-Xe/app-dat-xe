using Microsoft.Extensions.Configuration;

namespace RideHailingApi.Services
{
    public class DbConnectionFactory
    {
        private readonly IConfiguration _config;
        public DbConnectionFactory(IConfiguration config)
        {
            _config = config;
        }

        public string GetConnectionString(string region, bool isFailover = false)
        {
            string dbType = isFailover ? "Replica" : "Primary";
            // Thêm ?? "" ở cuối để đảm bảo nó không bao giờ bị Null
            return _config.GetConnectionString($"{region}_{dbType}") ?? "";
        }
    }
}