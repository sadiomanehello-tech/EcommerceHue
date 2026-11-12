using System.Net;
using System.Text;
using System.Text.Json;
using EcommerceHue.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EcommerceHue.Services;

/// <summary>
/// Dịch vụ gửi thông báo đơn hàng, giám sát hệ thống và điều hành cửa hàng qua Telegram Bot API.
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
    /// Đọc thông số xác thực Telegram Bot từ IConfiguration hoặc biến môi trường
    /// </summary>
    private (string botToken, string chatId) GetCredentials()
    {
        var botToken = (_configuration["Telegram:BotToken"] 
                     ?? _configuration["Telegram__BotToken"] 
                     ?? Environment.GetEnvironmentVariable("Telegram__BotToken") 
                     ?? Environment.GetEnvironmentVariable("Telegram:BotToken")
                     ?? "").Trim();

        var chatId = (_configuration["Telegram:ChatId"] 
                   ?? _configuration["Telegram__ChatId"] 
                   ?? Environment.GetEnvironmentVariable("Telegram__ChatId") 
                   ?? Environment.GetEnvironmentVariable("Telegram:ChatId")
                   ?? "").Trim();

        return (botToken, chatId);
    }

    /// <summary>
    /// Gửi bất kỳ thông điệp định dạng HTML nào tới ChatId của Quản trị viên
    /// </summary>
    public async Task SendRawMessageAsync(string htmlMessage)
    {
        if (string.IsNullOrWhiteSpace(htmlMessage)) return;

        var (botToken, chatId) = GetCredentials();
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            var warnMsg = "[Telegram] Cảnh báo: Biến cấu hình 'Telegram__BotToken' hoặc 'Telegram__ChatId' chưa được thiết lập. Bỏ qua gửi thông báo Telegram.";
            Console.WriteLine(warnMsg);
            _logger.LogWarning(warnMsg);
            return;
        }

        try
        {
            var payload = new
            {
                chat_id = chatId,
                text = htmlMessage,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var stringContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var httpClient = _httpClientFactory.CreateClient("TelegramBotClient");
            httpClient.Timeout = TimeSpan.FromSeconds(10); // Timeout ngắn để không làm chậm luồng giao dịch

            var endpoint = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var response = await httpClient.PostAsync(endpoint, stringContent);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Telegram] Gửi tin nhắn thành công.");
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[Telegram] Gửi tin nhắn thất bại (HTTP {Status}): {Body}",
                    response.StatusCode, responseBody);
                Console.WriteLine($"[Telegram] ⚠️ Gửi tin nhắn thất bại (HTTP {response.StatusCode}): {responseBody}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telegram] Ngoại lệ khi gửi tin nhắn: {Message}", ex.Message);
            Console.WriteLine($"[Telegram] ❌ Ngoại lệ khi gửi tin nhắn Telegram: {ex.Message}");
        }
    }

    /// <summary>
    /// Gửi thông báo đơn hàng mới / xác nhận chuyển khoản thành công tới Quản trị viên
    /// </summary>
    public async Task SendOrderNotificationAsync(Order order, string titlePrefix)
    {
        if (order == null) return;

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

            await SendRawMessageAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telegram] Lỗi khi tạo tin nhắn đơn hàng: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Gửi cảnh báo khẩn cấp khi số lượng tồn kho của một sản phẩm xuống dưới mức an toàn (5)
    /// </summary>
    public async Task SendLowStockAlertAsync(Product product)
    {
        if (product == null) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("⚠️ <b>CẢNH BÁO TỒN KHO THẤP</b>");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📦 Sản phẩm: <b>{WebUtility.HtmlEncode(product.Name)}</b>");
            sb.AppendLine($"📉 Số lượng còn lại: <b>{product.StockQuantity}</b> sản phẩm (Dưới mức an toàn 5).");
            sb.AppendLine("👉 <a href=\"https://ecommercehue.onrender.com/Admin/Products\">Bấm vào đây để nhập thêm hàng ngay</a>");

            await SendRawMessageAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telegram] Lỗi khi gửi cảnh báo tồn kho thấp: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Gửi cảnh báo danh sách sản phẩm cận hạn sử dụng (trong vòng 15 ngày tới)
    /// </summary>
    public async Task SendExpiryAlertAsync(List<Product> expiringProducts)
    {
        if (expiringProducts == null || !expiringProducts.Any()) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("⏳ <b>CẢNH BÁO ĐẶC SẢN CẬN HẠN SỬ DỤNG (HSD &lt; 15 NGÀY)</b>");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"Hệ thống phát hiện <b>{expiringProducts.Count}</b> sản phẩm có hạn sử dụng cận kề:");
            sb.AppendLine();

            int idx = 1;
            foreach (var p in expiringProducts)
            {
                sb.AppendLine($"{idx++}. <b>{WebUtility.HtmlEncode(p.Name)}</b>");
                sb.AppendLine($"   • Tồn kho: <b>{p.StockQuantity}</b> | HSD: <b>{p.ExpiryDate:dd/MM/yyyy}</b>");
            }

            sb.AppendLine();
            sb.AppendLine("💡 <i>Nhắc nhở: Hãy tạo Voucher giảm giá hoặc chiến dịch Flash Sale xả kho để giải phóng hàng kịp thời!</i>");
            sb.AppendLine("🎟️ <a href=\"https://ecommercehue.onrender.com/Admin/Coupons\">Bấm vào đây để tạo Voucher ngay</a>");

            await SendRawMessageAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telegram] Lỗi khi gửi cảnh báo cận date: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Gửi cảnh báo lỗi kỹ thuật hệ thống cho Quản trị viên / Lập trình viên
    /// </summary>
    public async Task SendSystemAlertAsync(string moduleName, string errorDetail)
    {
        try
        {
            var vnNow = DateTime.UtcNow.AddHours(7);
            var sb = new StringBuilder();
            sb.AppendLine("🚨 <b>CẢNH BÁO SỰ CỐ HỆ THỐNG</b>");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"🔧 Phân hệ: <b>{WebUtility.HtmlEncode(moduleName)}</b>");
            sb.AppendLine($"❌ Chi tiết: <code>{WebUtility.HtmlEncode(errorDetail ?? "Không rõ chi tiết lỗi")}</code>");
            sb.AppendLine($"🕒 Thời gian: {vnNow:HH:mm:ss dd/MM/yyyy} (Giờ VN)");

            await SendRawMessageAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telegram] Lỗi khi gửi cảnh báo sự cố hệ thống: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Gửi bản tin tổng kết doanh thu và tình hình kinh doanh trong ngày
    /// </summary>
    public async Task SendDailyRevenueReportAsync(
        DateTime reportDate, 
        int totalOrders, 
        int completedOrders, 
        int cancelledOrders, 
        decimal payosRevenue, 
        decimal codRevenue, 
        string topProductName)
    {
        try
        {
            var totalRevenue = payosRevenue + codRevenue;

            var sb = new StringBuilder();
            sb.AppendLine("📊 <b>BÁO CÁO DOANH THU &amp; KINH DOANH TRONG NGÀY</b>");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📅 <b>Ngày báo cáo:</b> {reportDate:dd/MM/yyyy}");
            sb.AppendLine();
            sb.AppendLine("📈 <b>TỔNG QUAN ĐƠN HÀNG:</b>");
            sb.AppendLine($"  • Tổng số đơn tiếp nhận: <b>{totalOrders}</b> đơn");
            sb.AppendLine($"  • Đơn hoàn thành / Đã thanh toán: <b>{completedOrders}</b> đơn");
            sb.AppendLine($"  • Đơn đã hủy: <b>{cancelledOrders}</b> đơn");
            sb.AppendLine();
            sb.AppendLine("💰 <b>DOANH THU THỰC TẾ:</b>");
            sb.AppendLine($"  • Chuyển khoản VietQR (PayOS): <b>{payosRevenue:N0} VNĐ</b>");
            sb.AppendLine($"  • Tiền mặt giao hàng (COD): <b>{codRevenue:N0} VNĐ</b>");
            sb.AppendLine($"  👉 <b>TỔNG DOANH THU THỰC TẾ:</b> <b>{totalRevenue:N0} VNĐ</b>");
            sb.AppendLine();
            sb.AppendLine($"🏆 <b>Sản phẩm bán chạy nhất:</b> <b>{WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(topProductName) ? "Chưa có dữ liệu" : topProductName)}</b>");
            sb.AppendLine();
            sb.AppendLine("<a href=\"https://ecommercehue.onrender.com/Admin/Orders\">👉 Bấm vào đây để mở Trang Quản Trị Đơn Hàng</a>");

            await SendRawMessageAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Telegram] Lỗi khi gửi báo cáo doanh thu ngày: {Message}", ex.Message);
        }
    }
}
