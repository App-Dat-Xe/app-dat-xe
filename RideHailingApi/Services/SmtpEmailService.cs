using System.Net;
using System.Net.Mail;

namespace RideHailingApi.Services
{
    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, string htmlBody)
        {
            var emailSection = _config.GetSection("Email");
            var smtpHost = emailSection["SmtpHost"];

            // Dev mode: log to console when SMTP is not configured
            if (string.IsNullOrEmpty(smtpHost))
            {
                _logger.LogInformation(
                    "📧 [EMAIL DEV MODE] To: {To} | Subject: {Subject}\n{Body}",
                    to, subject, htmlBody);
                return;
            }

            int smtpPort = int.TryParse(emailSection["SmtpPort"], out var p) ? p : 587;
            string smtpUser = emailSection["SmtpUser"] ?? "";
            string smtpPass = emailSection["SmtpPassword"] ?? "";
            string from = emailSection["From"] ?? "noreply@ridehailing.app";
            string fromName = emailSection["FromName"] ?? "RideHailing App";

            using var msg = new MailMessage();
            msg.From = new MailAddress(from, fromName);
            msg.To.Add(to);
            msg.Subject = subject;
            msg.Body = htmlBody;
            msg.IsBodyHtml = true;

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(smtpUser, smtpPass)
            };

            await client.SendMailAsync(msg);
            _logger.LogInformation("Email sent to {To} — {Subject}", to, subject);
        }
    }
}
