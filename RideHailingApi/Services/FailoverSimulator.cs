namespace RideHailingApi.Services
{
    // Singleton — quản lý trạng thái giả lập failover cho từng region.
    // Khi IsPrimaryDown("South") = true:
    //   - ExecuteNonQuery  → ném exception → API trả 503 (không thể ghi)
    //   - ExecuteReader    → bỏ qua Primary, đọc thẳng từ Replica (vẫn xem được lịch sử)
    public class FailoverSimulator
    {
        private readonly HashSet<string> _simulatedDown = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<LogEntry> _log = new();
        private readonly object _lock = new();

        public bool IsPrimaryDown(string region)
        {
            lock (_lock) return _simulatedDown.Contains(region);
        }

        public void SetPrimaryDown(string region)
        {
            lock (_lock)
            {
                _simulatedDown.Add(region);
                Append(region, $"⚠ Primary [{region}] được giả lập SẬP — App chuyển sang Replica");
            }
        }

        public void SetPrimaryUp(string region)
        {
            lock (_lock)
            {
                _simulatedDown.Remove(region);
                Append(region, $"✅ Primary [{region}] được khôi phục — App trở lại bình thường");
            }
        }

        public void Append(string region, string message)
        {
            lock (_lock)
            {
                _log.Insert(0, new LogEntry { Time = DateTime.Now, Region = region, Message = message });
                if (_log.Count > 100) _log.RemoveAt(_log.Count - 1);
            }
        }

        public List<LogEntry> GetLogs()
        {
            lock (_lock) return _log.ToList();
        }

        public record LogEntry
        {
            public DateTime Time    { get; init; }
            public string Region    { get; init; } = "";
            public string Message   { get; init; } = "";
        }
    }
}
