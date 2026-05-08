using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RideHailingApi.Services
{
    public record EmailTokenEntry(string Email, int? UserId, string Purpose, string Region, DateTime ExpiresAt);

    public class EmailTokenService
    {
        private readonly ConcurrentDictionary<string, EmailTokenEntry> _tokens = new();

        public string Generate(string email, int? userId, string purpose, string region = "South", int expiryMinutes = 30)
        {
            // URL-safe base64 token
            var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            var token = raw.Replace("+", "-").Replace("/", "_").Replace("=", "");
            _tokens[token] = new EmailTokenEntry(email, userId, purpose, region, DateTime.UtcNow.AddMinutes(expiryMinutes));
            return token;
        }

        public EmailTokenEntry? Validate(string token, string purpose)
        {
            if (!_tokens.TryGetValue(token, out var entry)) return null;
            if (entry.Purpose != purpose) return null;
            if (entry.ExpiresAt < DateTime.UtcNow)
            {
                _tokens.TryRemove(token, out _);
                return null;
            }
            return entry;
        }

        public void Consume(string token) => _tokens.TryRemove(token, out _);
    }
}
