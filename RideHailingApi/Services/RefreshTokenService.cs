using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RideHailingApi.Services
{
    public record RefreshTokenEntry(int UserId, string UserName, string Region, string Role, DateTime ExpiresAt);

    public class RefreshTokenService
    {
        private readonly ConcurrentDictionary<string, RefreshTokenEntry> _tokens = new();

        public string Generate(int userId, string userName, string region, string role = "USER")
        {
            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            _tokens[token] = new RefreshTokenEntry(userId, userName, region, role, DateTime.UtcNow.AddDays(30));
            return token;
        }

        public RefreshTokenEntry? Validate(string token)
        {
            if (!_tokens.TryGetValue(token, out var entry)) return null;
            if (entry.ExpiresAt < DateTime.UtcNow) { _tokens.TryRemove(token, out _); return null; }
            return entry;
        }

        public void Revoke(string token) => _tokens.TryRemove(token, out _);

        public void RevokeAllForUser(int userId)
        {
            var toRemove = _tokens.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList();
            foreach (var k in toRemove) _tokens.TryRemove(k, out _);
        }
    }
}
