using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EcommerceHue.Services
{
    /// <summary>
    /// Giao diện dịch vụ gửi email trong hệ thống EcommerceHue
    /// </summary>
    public interface IEmailService
    {
        /// <summary>
        /// Gửi email đơn hàng bất đồng bộ
        /// </summary>
        Task SendOrderEmailAsync(string recipientEmail, string subject, string htmlContent);
    }

    /// <summary>
    /// Dịch vụ gửi email thông qua Brevo HTTP REST API (Cổng HTTPS 443)
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            
            // Đọc cấu hình mã bảo mật an toàn từ biến môi trường của Render
            _apiKey = configuration["Brevo:ApiKey"] ?? string.Empty;
        }

        public async Task SendOrderEmailAsync(string recipientEmail, string subject, string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogError("=== [BREVO API ERROR]: Chưa cấu hình API Key trong biến môi trường Render.");
                return;
            }

            try
            {
                // Địa chỉ API REST chính thức của Brevo v3
                var url = "https://api.brevo.com/v3/smtp/email";

                // Định dạng cấu trúc gói tin JSON theo đúng chuẩn tài liệu kỹ thuật của Brevo
                var payload = new
                {
                    sender = new { name = "EcommerceHue - Đặc Sản Huế", email = "ecommercedacsanhue@gmail.com" },
                    to = new[] { new { email = recipientEmail } },
                    subject = subject,
                    htmlContent = htmlContent
                };

                var jsonString = JsonSerializer.Serialize(payload);
                var requestContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                // Nạp các thông số xác thực bảo mật vào đầu Header gói tin
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

                _logger.LogInformation("=== [BREVO HTTP API] Đang chuẩn bị bắn thư qua cổng 443 tới: {Email}", recipientEmail);

                // Thực hiện lệnh kích nổ bắn yêu cầu HTTPS truyền qua tường lửa Render công khai
                var response = await _httpClient.PostAsync(url, requestContent);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("=== [BREVO SUCCESS] Thư đã bay qua cổng 443 xuyên tường lửa Render thành công vèo vèo!");
                }
                else
                {
                    var errorDetails = await response.Content.ReadAsStringAsync();
                    _logger.LogError("=== [BREVO API REFUSED]: Mã lỗi {StatusCode} - Chi tiết: {Details}", response.StatusCode, errorDetails);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "=== [BREVO CRITICAL CRASH]: Lỗi mạng ngầm khi kết nối HTTP API: {Message}", ex.Message);
            }
        }
    }
}
