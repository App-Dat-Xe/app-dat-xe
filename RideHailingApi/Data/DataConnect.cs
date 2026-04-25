using Microsoft.Data.SqlClient;
using RideHailingApi.Services;
using System.Data;

namespace RideHailingApi.Data
{
    public class DataConnect
    {
        private readonly DbConnectionFactory _factory;

        public DataConnect(DbConnectionFactory factory)
        {
            _factory = factory;
        }

        // Thực thi INSERT trả về scalar (ví dụ SCOPE_IDENTITY()) vào Primary DB.
        // Ném ngoại lệ nếu Primary không khả dụng.
        public object? ExecuteScalarWrite(string region, string sql, Action<SqlCommand>? parameterizer = null)
        {
            string connStr = _factory.GetConnectionString(region, isFailover: false);
            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();
                using var cmd = new SqlCommand(sql, conn);
                parameterizer?.Invoke(cmd);
                return cmd.ExecuteScalar();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"[{region}] Server Chính không khả dụng. Không thể ghi dữ liệu vào Replica (Read-Only).", ex);
            }
        }

        // Thực thi ghi (INSERT/UPDATE/DELETE) vào Primary DB.
        // Ném ngoại lệ nếu Primary không khả dụng — không cho phép ghi vào Replica.
        public int ExecuteNonQuery(string region, string sql, Action<SqlCommand>? parameterizer = null)
        {
            string connStr = _factory.GetConnectionString(region, isFailover: false);
            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();
                using var cmd = new SqlCommand(sql, conn);
                parameterizer?.Invoke(cmd);
                return cmd.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"[{region}] Server Chính không khả dụng. Không thể ghi dữ liệu vào Replica (Read-Only).", ex);
            }
        }

        // Thực thi câu lệnh trả về một giá trị đơn (COUNT, @@SERVERNAME, ...).
        // Tự động chuyển sang Replica nếu Primary sập.
        public object? ExecuteScalar(string region, string sql, Action<SqlCommand>? parameterizer = null)
        {
            return ExecuteRead(region, sql, parameterizer, cmd => cmd.ExecuteScalar());
        }

        // Thực thi câu lệnh SELECT và trả về DataTable.
        // Tự động chuyển sang Replica nếu Primary sập.
        public DataTable ExecuteReader(string region, string sql, Action<SqlCommand>? parameterizer = null)
        {
            return ExecuteRead(region, sql, parameterizer, cmd =>
            {
                var table = new DataTable();
                using var reader = cmd.ExecuteReader();
                table.Load(reader);
                return table;
            });
        }

        // Kiểm tra Primary có sống không (dùng cho endpoint /api/trips/health).
        public bool IsPrimaryAlive(string region)
        {
            try
            {
                string connStr = _factory.GetConnectionString(region, isFailover: false);
                using var conn = new SqlConnection(connStr);
                conn.Open();
                using var cmd = new SqlCommand("SELECT 1", conn);
                cmd.ExecuteScalar();
                return true;
            }
            catch { return false; }
        }

        // Kiểm tra Replica có sống không.
        public bool IsReplicaAlive(string region)
        {
            try
            {
                string connStr = _factory.GetConnectionString(region, isFailover: true);
                using var conn = new SqlConnection(connStr);
                conn.Open();
                using var cmd = new SqlCommand("SELECT 1", conn);
                cmd.ExecuteScalar();
                return true;
            }
            catch { return false; }
        }

        // Helper dùng chung cho mọi lệnh đọc: thử Primary trước, fallback Replica nếu sập.
        private T ExecuteRead<T>(string region, string sql, Action<SqlCommand>? parameterizer, Func<SqlCommand, T> execute)
        {
            string primaryConn = _factory.GetConnectionString(region, isFailover: false);
            try
            {
                using var conn = new SqlConnection(primaryConn);
                conn.Open();
                using var cmd = new SqlCommand(sql, conn);
                parameterizer?.Invoke(cmd);
                return execute(cmd);
            }
            catch (SqlException)
            {
                // Primary sập → chuyển sang Replica (Read-Only fallback)
                string replicaConn = _factory.GetConnectionString(region, isFailover: true);
                using var fbConn = new SqlConnection(replicaConn);
                fbConn.Open();
                using var fbCmd = new SqlCommand(sql, fbConn);
                parameterizer?.Invoke(fbCmd);
                return execute(fbCmd);
            }
        }
    }
}
