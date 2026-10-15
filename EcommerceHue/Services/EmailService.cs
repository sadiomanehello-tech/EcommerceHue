using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace EcommerceHue.Services;

/// <summary>
/// Giao diện dịch vụ gửi email trong hệ thống EcommerceHue
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Gửi email xác nhận đơn hàng bất đồng bộ
    /// </summary>
    /// <param name="toEmail">Địa chỉ email người nhận</param>
    /// <param name="subject">Tiêu đề email</param>
    /// <param name="htmlContent">Nội dung thư dạng HTML</param>
    Task SendOrderEmailAsync(string toEmail, string subject, string htmlContent);
}

/// <summary>
/// Dịch vụ gửi thư SMTP sử dụng thư viện MailKit và MimeKit qua Gmail SMTP
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gửi email bất đồng bộ bằng SMTP Gmail với giao thức bảo mật StartTls (Port 587)
    /// </summary>
    public async Task SendOrderEmailAsync(string toEmail, string subject, string htmlContent)
    {
        try
        {
            var server = _configuration["MailSettings:Server"] ?? "smtp.gmail.com";
            var portString = _configuration["MailSettings:Port"] ?? "587";
            int.TryParse(portString, out var port);
            if (port <= 0) port = 587;

            var fromMail = _configuration["MailSettings:Mail"] ?? "sadiomanehello@gmail.com";
            var displayName = _configuration["MailSettings:DisplayName"] ?? "Đặc Sản Xứ Huế - EcommerceHue";
            var rawPassword = _configuration["MailSettings:Password"] ?? string.Empty;

            // Chuẩn hóa mật khẩu ứng dụng Gmail (loại bỏ khoảng trắng nhóm 4 ký tự)
            var password = rawPassword.Replace(" ", "").Trim();

            // Kiểm tra cấu hình mật khẩu ứng dụng Gmail (App Password)
            if (string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("[EmailService] Chưa cấu hình 'MailSettings:Password' (Mật khẩu ứng dụng Gmail) trong appsettings. Bỏ qua gửi email thực tế đến {ToEmail}.", toEmail);
                return;
            }

            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("[EmailService] Địa chỉ email nhận (toEmail) đang bị rỗng. Bỏ qua gửi thư.");
                return;
            }

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(displayName, fromMail));
            email.To.Add(MailboxAddress.Parse(toEmail.Trim()));
            email.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = htmlContent
            };
            email.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();

            // Chấp nhận mọi chứng chỉ hợp lệ từ SSL/TLS của Google
            smtp.ServerCertificateValidationCallback = (s, c, h, e) => true;

            // Kết nối đến máy chủ SMTP của Gmail (Port 587 với SecureSocketOptions.StartTls)
            await smtp.ConnectAsync(server, port, SecureSocketOptions.StartTls);

            // Xác thực tài khoản gửi thư với App Password của Gmail
            await smtp.AuthenticateAsync(fromMail, password);

            // Gửi thư
            await smtp.SendAsync(email);

            // Ngắt kết nối an toàn
            await smtp.DisconnectAsync(true);

            _logger.LogInformation("[EmailService] Đã gửi thư thành công tới {ToEmail}. Tiêu đề: {Subject}", toEmail, subject);
            Console.WriteLine($"=== [SMTP SUCCESS] Đã gửi email thành công tới {toEmail} - Tiêu đề: {subject}");
        }
        catch (Exception ex)
        {
            // Ghi nhật ký lỗi và ném ra để Controller xử lý bảo vệ luồng
            _logger.LogError(ex, "[EmailService] Gặp lỗi khi gửi email tới {ToEmail}. Chi tiết lỗi: {Message}", toEmail, ex.Message);
            throw;
        }
    }
}
