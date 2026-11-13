using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EcommerceHue.Models;

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

        /// <summary>
        /// Gửi email chiến dịch khuyến mãi (Voucher Marketing) tới khách hàng qua Brevo API
        /// </summary>
        Task<bool> SendPromotionEmailAsync(string toEmail, string customerName, Coupon coupon);
    }

    /// <summary>
    /// Dịch vụ gửi email thông qua Brevo HTTP REST API (Cổng HTTPS 443)
    /// Đã loại bỏ hoàn toàn các thư viện SMTP cũ (SmtpClient, MailKit, MimeKit, Port 587/465).
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
            _apiKey = configuration["Brevo__ApiKey"] 
                   ?? configuration["Brevo:ApiKey"] 
                   ?? Environment.GetEnvironmentVariable("Brevo__ApiKey")
                   ?? Environment.GetEnvironmentVariable("Brevo:ApiKey")
                   ?? string.Empty;
        }

        public async Task SendOrderEmailAsync(string recipientEmail, string subject, string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogError("=== [BREVO API ERROR]: Chưa cấu hình API Key trong biến môi trường Render.");
                Console.WriteLine("=== [BREVO API ERROR]: Chưa cấu hình API Key trong biến môi trường Render. Vui lòng thiết lập Brevo__ApiKey trên Render Dashboard!");
                return;
            }

            try
            {
                // Đảm bảo nút tra cứu Telegram Bot luôn hiện diện trong email xác nhận đơn hàng
                var finalHtmlContent = htmlContent;
                if (!string.IsNullOrWhiteSpace(finalHtmlContent) && !finalHtmlContent.Contains("https://t.me/ecommercehue_bot"))
                {
                    var telegramButtonHtml = """
                        <div style="text-align: center; margin: 25px 0;">
                            <a href="https://t.me/ecommercehue_bot" target="_blank" rel="noopener noreferrer" style="display: inline-block; padding: 12px 26px; background-color: #0088cc; color: #ffffff; text-decoration: none; border-radius: 25px; font-weight: bold; font-size: 14px; box-shadow: 0 4px 10px rgba(0, 136, 204, 0.25);">
                                ✈️ Tra cứu hành trình đơn hàng trên Telegram
                            </a>
                            <div style="font-size: 12px; color: #666; margin-top: 6px;">
                                (Nhận cập nhật tiến độ vận chuyển và tư vấn tự động 24/7)
                            </div>
                        </div>
                    """;

                    if (finalHtmlContent.Contains("</body>"))
                    {
                        finalHtmlContent = finalHtmlContent.Replace("</body>", telegramButtonHtml + "</body>");
                    }
                    else
                    {
                        finalHtmlContent += telegramButtonHtml;
                    }
                }

                // Địa chỉ API REST chính thức của Brevo v3
                var url = "https://api.brevo.com/v3/smtp/email";

                // Định dạng cấu trúc gói tin JSON theo đúng chuẩn tài liệu kỹ thuật của Brevo
                var payload = new
                {
                    sender = new { name = "EcommerceHue - Đặc Sản Huế", email = "sadiomanehello@gmail.com" },
                    to = new[] { new { email = recipientEmail } },
                    subject = subject,
                    htmlContent = finalHtmlContent
                };

                var jsonString = JsonSerializer.Serialize(payload);
                var requestContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                // Nạp các thông số xác thực bảo mật vào đầu Header gói tin
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

                _logger.LogInformation("=== [BREVO HTTP API] Đang chuẩn bị bắn thư qua cổng 443 tới: {Email}", recipientEmail);
                Console.WriteLine($"=== [BREVO HTTP API] Đang chuẩn bị bắn thư qua cổng 443 tới: {recipientEmail}");

                // Thực hiện lệnh kích nổ bắn yêu cầu HTTPS truyền qua tường lửa Render công khai
                var response = await _httpClient.PostAsync(url, requestContent);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("=== [BREVO SUCCESS] Thư đã bay qua cổng 443 xuyên tường lửa Render thành công vèo vèo!");
                    Console.WriteLine($"=== [BREVO SUCCESS] Thư đã bay qua cổng 443 tới {recipientEmail} thành công! Status: {response.StatusCode}");
                }
                else
                {
                    var errorDetails = await response.Content.ReadAsStringAsync();
                    _logger.LogError("=== [BREVO API REFUSED]: Mã lỗi {StatusCode} - Chi tiết: {Details}", response.StatusCode, errorDetails);
                    Console.WriteLine($"=== [BREVO API REFUSED]: Mã lỗi {response.StatusCode} - Chi tiết: {errorDetails}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "=== [BREVO CRITICAL CRASH]: Lỗi mạng ngầm khi kết nối HTTP API: {Message}", ex.Message);
                Console.WriteLine($"=== [BREVO CRITICAL CRASH]: Lỗi mạng ngầm khi kết nối HTTP API: {ex.Message}");
            }
        }

        public async Task<bool> SendPromotionEmailAsync(string toEmail, string customerName, Coupon coupon)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                _logger.LogError("=== [BREVO API ERROR]: Chưa cấu hình API Key trong biến môi trường Render.");
                Console.WriteLine("=== [BREVO API ERROR]: Chưa cấu hình Brevo__ApiKey trên Render!");
                return false;
            }

            try
            {
                var url = "https://api.brevo.com/v3/smtp/email";
                string displayName = string.IsNullOrWhiteSpace(customerName) ? "Quý khách" : customerName.Trim();
                string subject = $"🎁 [EcommerceHue] Tặng Bạn Mã Ưu Đãi {coupon.DiscountPercentage}% - {coupon.Code}";

                string conditionText = coupon.MinOrderValue > 0
                    ? $"Đơn hàng tối thiểu: <strong style='color: #4a154b;'>{coupon.MinOrderValue:N0} đ</strong>"
                    : "Áp dụng cho mọi giá trị đơn hàng";

                string maxDiscountText = coupon.MaxDiscountAmount.HasValue && coupon.MaxDiscountAmount.Value > 0
                    ? $" | Giảm tối đa: <strong style='color: #4a154b;'>{coupon.MaxDiscountAmount.Value:N0} đ</strong>"
                    : string.Empty;

                string htmlBody = $"""
                <!DOCTYPE html>
                <html lang="vi">
                <head>
                    <meta charset="UTF-8">
                    <title>{subject}</title>
                </head>
                <body style="margin: 0; padding: 0; background-color: #f6f3f8; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; color: #333;">
                    <div style="max-width: 600px; margin: 30px auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.06); border: 1px solid #ebdcf0;">
                        
                        <!-- HEADER TÍM HUẾ HOÀNG GIA -->
                        <div style="background: linear-gradient(135deg, #581c87 0%, #7e22ce 100%); padding: 35px 25px; text-align: center; color: #ffffff;">
                            <div style="font-size: 24px; font-weight: 800; letter-spacing: 1px; text-transform: uppercase;">
                                🏯 EcommerceHue
                            </div>
                            <div style="font-size: 13px; opacity: 0.9; margin-top: 5px;">
                                Tinh Hoa Đặc Sản Cố Đô • Đậm Đà Hương Vị Xứ Huế
                            </div>
                        </div>

                        <!-- BODY -->
                        <div style="padding: 30px 25px;">
                            <p style="font-size: 16px; margin-top: 0;">
                                Kính chào <strong>{displayName}</strong>,
                            </p>
                            <p style="font-size: 14px; line-height: 1.6; color: #555;">
                                EcommerceHue xin trân trọng gửi tặng Quý khách mã ưu đãi đặc biệt để cùng gia đình thưởng thức trọn vẹn những món quà đặc sản Huế thơm ngon nức tiếng cố đô:
                            </p>

                            <!-- KHỐI VOUCHER TÍM HUẾ ĐẸP MẮT -->
                            <div style="background: #faf5ff; border: 2px dashed #a855f7; border-radius: 10px; padding: 22px; text-align: center; margin: 25px 0;">
                                <div style="font-size: 13px; text-transform: uppercase; font-weight: bold; color: #7e22ce; letter-spacing: 1px;">
                                    🎉 ƯU ĐÃI ĐỘC QUYỀN DÀNH CHO BẠN
                                </div>
                                <div style="font-size: 32px; font-weight: 800; color: #581c87; letter-spacing: 2px; margin: 10px 0; font-family: monospace;">
                                    {coupon.Code}
                                </div>
                                <div style="display: inline-block; background: #581c87; color: #fff; font-weight: bold; padding: 4px 16px; border-radius: 20px; font-size: 14px;">
                                    GIẢM {coupon.DiscountPercentage}%
                                </div>
                                <div style="font-size: 13px; color: #6b21a8; margin-top: 14px; font-weight: 600;">
                                    {coupon.Description ?? "Ưu đãi đặc sản Huế hấp dẫn"}
                                </div>
                                <div style="font-size: 12px; color: #666; margin-top: 8px;">
                                    {conditionText}{maxDiscountText}
                                </div>
                                <div style="font-size: 12px; color: #b91c1c; margin-top: 6px; font-weight: 500;">
                                    ⏳ Hạn dùng: Đến hết ngày {coupon.EndDate.ToLocalTime():dd/MM/yyyy}
                                </div>
                            </div>

                            <!-- NÚT MUA SẮM NGAY -->
                            <div style="text-align: center; margin: 30px 0;">
                                <a href="https://ecommercehue.onrender.com" 
                                   style="display: inline-block; background: linear-gradient(135deg, #7e22ce 0%, #581c87 100%); color: #ffffff; text-decoration: none; padding: 14px 34px; border-radius: 30px; font-weight: bold; font-size: 15px; box-shadow: 0 4px 12px rgba(88, 28, 135, 0.3);">
                                    🛒 MUA SẮM & ÁP DỤNG MÃ NGAY
                                </a>
                            </div>

                            <p style="font-size: 13px; color: #777; line-height: 1.5; margin-bottom: 0;">
                                💡 <em>Mẹo nhỏ:</em> Quý khách chỉ cần chọn mua các sản phẩm yêu thích (Mè xửng Thiên Hương, Tôm chua Trọng Đức, Trà cung đình...) và nhập mã <strong>{coupon.Code}</strong> tại bước thanh toán để được khấu trừ tự động!
                            </p>
                        </div>

                        <!-- FOOTER -->
                        <div style="background-color: #f5f0f8; padding: 18px 25px; text-align: center; font-size: 12px; color: #777; border-top: 1px solid #ebdcf0;">
                            <div><strong>EcommerceHue - Tinh Hoa Đặc Sản Cố Đô</strong></div>
                            <div style="margin-top: 4px;">Hotline: 0901 234 567 | Chợ Đông Ba, Đường Trần Hưng Đạo, TP. Huế</div>
                            <div style="margin-top: 4px; font-size: 11px; color: #999;">Thư điện tử được gửi tự động từ hệ thống chăm sóc khách hàng EcommerceHue.</div>
                        </div>

                    </div>
                </body>
                </html>
                """;

                var payload = new
                {
                    sender = new { name = "EcommerceHue - Khuyến Mãi Đặc Sản", email = "sadiomanehello@gmail.com" },
                    to = new[] { new { email = toEmail, name = displayName } },
                    subject = subject,
                    htmlContent = htmlBody
                };

                var jsonString = JsonSerializer.Serialize(payload);
                var requestContent = new StringContent(jsonString, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);

                var response = await _httpClient.PostAsync(url, requestContent);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("=== [BREVO PROMO] Đã gửi email khuyến mãi {CouponCode} tới {Email} thành công!", coupon.Code, toEmail);
                    return true;
                }
                else
                {
                    var errorDetails = await response.Content.ReadAsStringAsync();
                    _logger.LogError("=== [BREVO PROMO FAILED] Không thể gửi tới {Email}. Code: {StatusCode}, Lỗi: {Error}", toEmail, response.StatusCode, errorDetails);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "=== [BREVO PROMO CRASH] Ngoại lệ khi gửi email tới {Email}: {Message}", toEmail, ex.Message);
                return false;
            }
        }
    }
}
