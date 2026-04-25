using Microsoft.Data.SqlClient;
using RideHailingApi.Services;
using System.Data;

namespace RideHailingApi.Data
{
    public class DataConnect
    {
        private readonly DbConnectionFactory  _factory;
        private readonly FailoverSimulator    _failover;

        public DataConnect(DbConnectionFactory factory, FailoverSimulator failover)
        {
            _factory  = factory;
            _failover = failover;
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
            // Kiểm tra giả lập failover TRƯỚC khi thử kết nối thật
            if (_failover.IsPrimaryDown(region))
                throw new InvalidOperationException(
                    $"[{region}] Primary đang bảo trì (giả lập). Không thể ghi dữ liệu vào Replica (Read-Only).");

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

        // ── READ (Scalar) ─────────────────────────────────────────────────────────
        public object? ExecuteScalar(string region, string sql, Action<SqlCommand>? parameterizer = null)
            => ExecuteRead(region, sql, parameterizer, cmd => cmd.ExecuteScalar());

        // ── READ (Table) ──────────────────────────────────────────────────────────
        public DataTable ExecuteReader(string region, string sql, Action<SqlCommand>? parameterizer = null)
            => ExecuteRead(region, sql, parameterizer, cmd =>
            {
                var table = new DataTable();
                using var reader = cmd.ExecuteReader();
                table.Load(reader);
                return table;
            });

        // ── HEALTH ────────────────────────────────────────────────────────────────
        public bool IsPrimaryAlive(string region)
        {
            try
            {
                using var conn = new SqlConnection(_factory.GetConnectionString(region, false));
                conn.Open();
                new SqlCommand("SELECT 1", conn).ExecuteScalar();
                return true;
            }
            catch { return false; }
        }

        public bool IsReplicaAlive(string region)
        {
            try
            {
                using var conn = new SqlConnection(_factory.GetConnectionString(region, true));
                conn.Open();
                new SqlCommand("SELECT 1", conn).ExecuteScalar();
                return true;
            }
            catch { return false; }
        }

        // ── PRIVATE HELPER ────────────────────────────────────────────────────────
        // Thử Primary trước; nếu Primary giả lập sập HOẶC mất kết nối → Replica.
        private T ExecuteRead<T>(string region, string sql,
            Action<SqlCommand>? parameterizer, Func<SqlCommand, T> execute)
        {
            // Giả lập failover → bỏ qua Primary, đọc thẳng Replica
            if (_failover.IsPrimaryDown(region))
                return ReadFromReplica(region, sql, parameterizer, execute);

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
                // Primary thật sự sập → Replica
                return ReadFromReplica(region, sql, parameterizer, execute);
            }
        }

        private T ReadFromReplica<T>(string region, string sql,
            Action<SqlCommand>? parameterizer, Func<SqlCommand, T> execute)
        {
            string replicaConn = _factory.GetConnectionString(region, isFailover: true);
            using var conn = new SqlConnection(replicaConn);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            parameterizer?.Invoke(cmd);
            return execute(cmd);
        }
    }
}
