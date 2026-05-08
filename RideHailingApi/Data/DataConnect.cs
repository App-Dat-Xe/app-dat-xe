using Microsoft.Data.SqlClient;
using RideHailingApi.Services;
using Microsoft.Extensions.Logging;
using System.Data;
using Microsoft.EntityFrameworkCore;
using RideHailingApi.Models;

namespace RideHailingApi.Data
{
    public class DataConnect
    {
        private readonly DbContextOptions<DataContext>? _efOptions;
        private readonly IConnectionStringResolver _resolver;
        private readonly DatabaseRuntimeState      _state;
        private readonly ILogger<DataConnect>?     _logger;

        public DataConnect(IConnectionStringResolver resolver, DatabaseRuntimeState state, ILogger<DataConnect>? logger = null)
        {
            _resolver = resolver;
            _state    = state;
            _logger   = logger;
        }

        // ── WRITE ─────────────────────────────────────────────────────────────────
        // Ghi đồng thời vào cả Primary và Replica để giả lập đồng bộ dữ liệu.
        // Ném lỗi nếu đang DegradedMode (Primary sập).

        public object? ExecuteScalarWrite(string region, string sql, Action<SqlCommand>? parameterizer = null)
        {
            EnsureWritable(region);
            var (primaryCs, replicaCs) = _resolver.GetDualConnectionStrings(region);

            object? result = null;
            // 1. Ghi vào Primary
            try
            {
                using var conn = new SqlConnection(primaryCs);
                conn.Open();
                using var cmd = new SqlCommand(sql, conn);
                parameterizer?.Invoke(cmd);
                result = cmd.ExecuteScalar();
            }
            catch (SqlException ex)
            {
                _logger?.LogError(ex, "ExecuteScalarWrite (Primary) failed for region {Region}.", region);
                throw;
            }

            // 2. Ghi vào Replica (Best effort trong môi trường giả lập này)
            try
            {
                using var conn = new SqlConnection(replicaCs);
                conn.Open();
                using var cmd = new SqlCommand(sql, conn);
                parameterizer?.Invoke(cmd);
                cmd.ExecuteScalar();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "ExecuteScalarWrite (Replica sync) failed for region {Region}.", region);
            }

            return result;
        }

        public int ExecuteNonQuery(string region, string sql, Action<SqlCommand>? parameterizer = null)
        {
            EnsureWritable(region);
            var (primaryCs, replicaCs) = _resolver.GetDualConnectionStrings(region);

            int result = 0;
            // 1. Ghi vào Primary
            try
            {
                using var conn = new SqlConnection(primaryCs);
                conn.Open();
                using var cmd = new SqlCommand(sql, conn);
                parameterizer?.Invoke(cmd);
                result = cmd.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                _logger?.LogError(ex, "ExecuteNonQuery (Primary) failed for region {Region}.", region);
                throw;
            }

            // 2. Ghi vào Replica
            try
            {
                using var conn = new SqlConnection(replicaCs);
                conn.Open();
                using var cmd = new SqlCommand(sql, conn);
                parameterizer?.Invoke(cmd);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "ExecuteNonQuery (Replica sync) failed for region {Region}.", region);
            }

            return result;
        }

        // ── READ ──────────────────────────────────────────────────────────────────
        // Dùng connection string hiện tại (Primary hoặc Backup tuỳ trạng thái).

        public object? ExecuteScalar(string region, string sql, Action<SqlCommand>? parameterizer = null)
            => ExecuteRead(region, sql, parameterizer, cmd => cmd.ExecuteScalar());

        // Helper to execute scalar and return int safely
        public int ExecuteScalarInt(string region, string sql, Action<SqlCommand>? parameterizer = null)
        {
            var obj = ExecuteScalar(region, sql, parameterizer);
            try { return Convert.ToInt32(obj ?? 0); } catch { return 0; }
        }

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
            => _state.GetState(region).PrimaryHealthy;

        public bool IsReplicaAlive(string region)
            => _state.GetState(region).BackupHealthy;

        // ── PRIVATE ───────────────────────────────────────────────────────────────

        private void EnsureWritable(string region)
        {
            if (_resolver.IsDegradedMode(region))
            {
                var target = _resolver.GetCurrentTarget(region);
                string reason = target == DatabaseTarget.None
                    ? $"[{region}] Cả Primary và Backup đều không khả dụng."
                    : $"[{region}] Đang chạy trên Backup DB (DegradedMode). Không thể ghi dữ liệu.";
                throw new InvalidOperationException(reason);
            }
        }

        private T ExecuteRead<T>(string region, string sql,
            Action<SqlCommand>? parameterizer, Func<SqlCommand, T> execute)
        {
            // Dùng connection string do resolver quyết định (Primary hoặc Backup theo trạng thái hiện tại)
            string cs = _resolver.GetConnectionString(region);
            using var conn = new SqlConnection(cs);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            parameterizer?.Invoke(cmd);
            return execute(cmd);
        }
    }
}
