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

        // isFailover = false → Primary DB  |  isFailover = true → Replica DB (Read-Only)
        public string GetConnectionString(string region, bool isFailover = false)
        {
            string dbType = isFailover ? "Replica" : "Primary";
            return _config.GetConnectionString($"{region}_{dbType}") ?? "";
        }
    }
}
