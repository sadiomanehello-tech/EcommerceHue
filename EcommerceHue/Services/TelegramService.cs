using System.Net;
using System.Text;
using System.Text.Json;
using EcommerceHue.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EcommerceHue.Services;

/// <summary>
/// Dịch vụ gửi thông báo đơn hàng và giao dịch thời gian thực qua Telegram Bot API.
/// </summary>
public class TelegramService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<TelegramService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gửi thông báo đơn hàng tới nhóm / kênh quản trị Telegram qua Bot API.
    /// Bọc hoàn toàn trong try-catch, không làm ảnh hưởng đến luồng giao dịch chính nếu lỗi mạng.
    /// </summary>
    /// <param name="order">Đơn hàng cần thông báo</param>
    /// <param name="titlePrefix">Tiêu đề thông báo (ví dụ: 🛍️ ĐƠN HÀNG MỚI CẦN XỬ LÝ)</param>
    public async Task SendOrderNotificationAsync(Order order, string titlePrefix)
    {
        if (order == null) return;

        // Đọc cấu hình linh hoạt từ appsettings hoặc biến môi trường Render
        var botToken = (_configuration["Telegram:BotToken"] 
                     ?? _configuration["Telegram__BotToken"] 
                     ?? Environment.GetEnvironmentVariable("Telegram__BotToken") 
                     ?? "").Trim();

        var chatId = (_configuration["Telegram:ChatId"] 
                   ?? _configuration["Telegram__ChatId"] 
                   ?? Environment.GetEnvironmentVariable("Telegram__ChatId") 
                   ?? "").Trim();

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            var warnMsg = "[Telegram] Cảnh báo: Biến cấu hình 'Telegram__BotToken' hoặc 'Telegram__ChatId' chưa được thiết lập. Bỏ qua gửi thông báo Telegram.";
            Console.WriteLine(warnMsg);
            _logger.LogWarning(warnMsg);
            return;
        }

        try
        {
            // Giờ Việt Nam (UTC+7)
            var orderTimeUtc = order.CreatedAt == default ? DateTime.UtcNow : order.CreatedAt;
            var vnTime = orderTimeUtc.AddHours(7);

            var sb = new StringBuilder();
            sb.AppendLine($"<b>{WebUtility.HtmlEncode(titlePrefix)}</b>");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"🔖 <b>Mã đơn:</b> <code>#{WebUtility.HtmlEncode(order.OrderCode)}</code>");
            sb.AppendLine($"⏰ <b>Thời gian:</b> {vnTime:dd/MM/yyyy HH:mm:ss} (Giờ VN)");
            sb.AppendLine();
            sb.AppendLine($"👤 <b>Khách hàng:</b> {WebUtility.HtmlEncode(order.CustomerName)}");
            sb.AppendLine($"📞 <b>Số điện thoại:</b> <code>{WebUtility.HtmlEncode(order.CustomerPhone)}</code>");
            sb.AppendLine($"📍 <b>Địa chỉ:</b> {WebUtility.HtmlEncode(order.ShippingAddress)}");
            if (!string.IsNullOrWhiteSpace(order.CustomerEmail))
            {
                sb.AppendLine($"📧 <b>Email:</b> {WebUtility.HtmlEncode(order.CustomerEmail)}");
            }

            sb.AppendLine();
            sb.AppendLine("📦 <b>Chi tiết sản phẩm:</b>");
            if (order.OrderDetails != null && order.OrderDetails.Any())
            {
                int index = 1;
                foreach (var detail in order.OrderDetails)
                {
                    var pName = WebUtility.HtmlEncode(detail.ProductName);
                    sb.AppendLine($"  {index++}. <b>{pName}</b> × <b>{detail.Quantity}</b> ({detail.UnitPrice:N0}đ)");
                }
            }
            else
            {
                sb.AppendLine("  <i>(Xem chi tiết sản phẩm trên hệ thống)</i>");
            }

            if (!string.IsNullOrWhiteSpace(order.Note))
            {
                sb.AppendLine();
                sb.AppendLine($"📝 <b>Ghi chú / Dịch vụ:</b>\n<i>{WebUtility.HtmlEncode(order.Note)}</i>");
            }

            sb.AppendLine();
            sb.AppendLine($"💰 <b>Tổng thanh toán:</b> <b>{order.TotalAmount:N0} VNĐ</b>");
            var pMethod = string.IsNullOrWhiteSpace(order.PaymentMethod) ? "Chưa xác định" : order.PaymentMethod;
            sb.AppendLine($"💳 <b>Phương thức:</b> {WebUtility.HtmlEncode(pMethod)}");
            if (!string.IsNullOrWhiteSpace(order.PaymentStatus))
            {
                sb.AppendLine($"📊 <b>Trạng thái:</b> <i>{WebUtility.HtmlEncode(order.PaymentStatus)}</i>");
            }

            sb.AppendLine();
            sb.AppendLine("<a href=\"https://ecommercehue.onrender.com/Admin/Orders\">👉 Bấm vào đây để mở Trang Quản Trị &amp; Đóng gói</a>");

            var messageContent = sb.ToString();

            var payload = new
            {
                chat_id = chatId,
                text = messageContent,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var stringContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var httpClient = _httpClientFactory.CreateClient("TelegramBotClient");
            httpClient.Timeout = TimeSpan.FromSeconds(10); // Timeout ngắn để không giữ tài nguyên lâu

            var endpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var response = await httpClient.PostAsync(endpoint, stringContent);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Telegram] Gửi thông báo đơn hàng #{OrderCode} thành công.", order.OrderCode);
                Console.WriteLine($"[Telegram] ✅ Đã gửi thông báo đơn #{order.OrderCode} qua Telegram Bot thành công!");
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[Telegram] Gửi thông báo đơn hàng #{OrderCode} thất bại (HTTP {Status}): {Body}",
                    order.OrderCode, response.StatusCode, responseBody);
                Console.WriteLine($"[Telegram] ⚠️ Gửi thông báo đơn #{order.OrderCode} thất bại (HTTP {response.StatusCode}): {responseBody}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telegram] Ngoại lệ khi gửi thông báo đơn hàng #{OrderCode}: {Message}", order.OrderCode, ex.Message);
            Console.WriteLine($"[Telegram] ❌ Ngoại lệ khi gửi thông báo đơn #{order.OrderCode}: {ex.Message}");
        }
    }
}
