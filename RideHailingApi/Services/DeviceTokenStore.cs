using System.Collections.Concurrent;

namespace RideHailingApi.Services
{
    public class DeviceTokenStore
    {
        private readonly ConcurrentDictionary<int, string> _tokens = new();

        public void Register(int userId, string deviceToken)
            => _tokens[userId] = deviceToken;

        public string? Get(int userId)
            => _tokens.GetValueOrDefault(userId);

        public void Remove(int userId)
            => _tokens.TryRemove(userId, out _);
    }
}
