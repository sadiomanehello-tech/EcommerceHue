using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EcommerceHue.Data;
using EcommerceHue.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EcommerceHue.Services;

/// <summary>
/// Dịch vụ Trí tuệ nhân tạo thông minh tích hợp Google Gemini API kết hợp dữ liệu thực tế từ SQLite.
/// Phục vụ đồng thời cho khung Chat trên Website và Telegram Bot.
/// </summary>
public class AiAssistantService
{
    private readonly IConfiguration _configuration;
    private readonly EcommerceContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AiAssistantService> _logger;

    public AiAssistantService(
        IConfiguration configuration,
        EcommerceContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<AiAssistantService> logger)
    {
        _configuration = configuration;
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Xử lý tin nhắn của người dùng và trả về câu trả lời thông minh
    /// </summary>
    public async Task<string> GetSmartReplyAsync(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return "Dạ, em là Trợ Lý Ẩm Thực Cố Đô. Quý khách cần tra cứu đơn hàng hay tư vấn đặc sản Huế nào ạ? 🏮";
        }

        var trimmedMessage = userMessage.Trim();

        // =====================================================================
        // PHẦN A: TRA CỨU ĐƠN HÀNG TỰ ĐỘNG BẰNG REGEX (TRUY VẤN TRỰC TIẾP SQLITE)
        // Tiết kiệm lượt gọi API Gemini, phản hồi tức thì và chính xác tuyệt đối.
        // =====================================================================
        var orderMatch = Regex.Match(trimmedMessage, @"\b(HUE-[0-9-]+)\b", RegexOptions.IgnoreCase);
        if (orderMatch.Success)
        {
            var orderCode = orderMatch.Groups[1].Value.Trim();
            return await LookupOrderAsync(orderCode);
        }

        // =====================================================================
        // PHẦN B: TƯ VẤN THÔNG MINH QUA GEMINI API DỰA TRÊN DỮ LIỆU THỰC TẾ
        // =====================================================================
        try
        {
            var apiKey = (_configuration["Gemini:ApiKey"] 
                       ?? _configuration["Gemini__ApiKey"] 
                       ?? Environment.GetEnvironmentVariable("Gemini__ApiKey") 
                       ?? Environment.GetEnvironmentVariable("Gemini:ApiKey") 
                       ?? "").Trim();

            // Nếu chưa cấu hình Gemini API Key -> fallback về FAQ thông minh
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogWarning("[AiAssistant] Chưa cấu hình Gemini API Key. Sử dụng bộ phản hồi FAQ nội bộ.");
                return await GenerateFallbackReplyAsync(trimmedMessage);
            }

            // 1. Tải danh sách sản phẩm đang bán từ SQLite
            var products = await _context.Products
                .Include(p => p.Category)
                .AsNoTracking()
                .Where(p => p.IsAvailable)
                .OrderBy(p => p.CategoryId)
                .ThenBy(p => p.Name)
                .Take(40)
                .ToListAsync();

            // 2. Tải danh sách mã giảm giá đang hoạt động từ SQLite
            var nowUtc = DateTime.UtcNow;
            var coupons = await _context.Coupons
                .AsNoTracking()
                .Where(c => c.IsActive && c.EndDate >= nowUtc)
                .OrderByDescending(c => c.DiscountPercentage)
                .ToListAsync();

            // 3. Xây dựng ngữ cảnh dữ liệu thực tế (Context Data)
            var sbContext = new StringBuilder();
            sbContext.AppendLine("--- DANH SÁCH SẢN PHẨM & TỒN KHO THỰC TẾ CỬA HÀNG ---");
            foreach (var p in products)
            {
                var catName = p.Category?.Name ?? "Đặc sản";
                var stockText = p.StockQuantity > 0 ? $"Còn {p.StockQuantity} sp" : "Tạm hết hàng";
                var expText = p.ExpiryDate.ToString("dd/MM/yyyy");
                sbContext.AppendLine($"• [{catName}] {p.Name}: Giá {p.Price:N0}đ | Tồn kho: {stockText} | HSD: {expText}");
                if (!string.IsNullOrWhiteSpace(p.Description))
                {
                    sbContext.AppendLine($"  Mô tả: {p.Description}");
                }
                if (!string.IsNullOrWhiteSpace(p.UsageAndStorage))
                {
                    sbContext.AppendLine($"  Bảo quản: {p.UsageAndStorage}");
                }
            }

            sbContext.AppendLine();
            sbContext.AppendLine("--- DANH SÁCH MÃ GIẢM GIÁ ĐANG ÁP DỤNG ---");
            if (coupons.Any())
            {
                foreach (var c in coupons)
                {
                    var minVal = c.MinOrderValue > 0 ? $"đơn từ {c.MinOrderValue:N0}đ" : "mọi đơn hàng";
                    sbContext.AppendLine($"• Mã '{c.Code}': Giảm {c.DiscountPercentage}% cho {minVal}. {c.Description}");
                }
            }
            else
            {
                sbContext.AppendLine("• Hiện tại chưa có mã giảm giá chung. Khách đăng ký email ở chân trang nhận ngay mã giảm 10%!");
            }

            // 4. Thiết lập Prompt vai trò Trợ Lý Ẩm Thực Cố Đô
            var systemInstruction = 
                "Bạn là 'Trợ Lý Ẩm Thực Cố Đô' của cửa hàng trực tuyến Đặc sản Huế (EcommerceHue).\n" +
                "Nhiệm vụ của bạn:\n" +
                "1. Trả lời thân thiện, lịch thiệp, mang đậm phong vị xứ Huế, ngắn gọn và súc tích (dưới 200 từ).\n" +
                "2. Luôn dựa vào dữ liệu sản phẩm, giá bán, số lượng tồn kho và mã giảm giá thực tế được cung cấp bên dưới.\n" +
                "3. Tuyệt đối không bịa đặt hoặc đề xuất sản phẩm không có trong danh mục của cửa hàng.\n" +
                "4. Chính sách giao hàng: Nội thành Huế giao nhanh trong 24h; các tỉnh thành khác giao 2-3 ngày qua bưu điện/GHN.\n" +
                "5. Phương thức thanh toán: Hỗ trợ chuyển khoản VietQR tự động (PayOS/BIDV) và ship COD nhận hàng kiểm tra trả tiền mặt.\n" +
                "6. Nếu khách hỏi tra cứu đơn hàng nhưng chưa đưa mã, nhắc khách cung cấp mã đơn dạng HUE-...\n" +
                "7. Hotline hỗ trợ 24/7: 0901 234 567.";

            var fullPrompt = $"{systemInstruction}\n\n{sbContext}\n\nKhách hàng hỏi: \"{trimmedMessage}\"\n\nTrợ Lý Ẩm Thực Cố Đô trả lời:";

            // 5. Gửi yêu cầu tới Gemini API
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";
            var requestPayload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = fullPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 800
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestPayload);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("GeminiApiClient");
            client.Timeout = TimeSpan.FromSeconds(15);

            var response = await client.PostAsync(endpoint, httpContent);
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0 &&
                    candidates[0].TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textProp))
                {
                    var geminiText = textProp.GetString();
                    if (!string.IsNullOrWhiteSpace(geminiText))
                    {
                        return geminiText.Trim();
                    }
                }
            }
            else
            {
                var errBody = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("[AiAssistant] Gemini API trả về mã lỗi HTTP {Status}: {Error}", response.StatusCode, errBody);
            }

            // Fallback nếu Gemini API không thành công
            return await GenerateFallbackReplyAsync(trimmedMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiAssistant] Ngoại lệ khi gọi Gemini API: {Message}. Chuyển sang fallback.", ex.Message);
            return await GenerateFallbackReplyAsync(trimmedMessage);
        }
    }

    /// <summary>
    /// Tra cứu chi tiết đơn hàng trong SQLite theo mã OrderCode
    /// </summary>
    private async Task<string> LookupOrderAsync(string orderCode)
    {
        try
        {
            var cleanCode = orderCode.Trim();
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderCode == cleanCode || o.OrderCode.ToLower() == cleanCode.ToLower());

            if (order == null)
            {
                return $"Dạ, hệ thống không tìm thấy đơn hàng có mã <code>{WebUtility.HtmlEncode(cleanCode)}</code>. Quý khách vui lòng kiểm tra lại mã đơn trong Email xác nhận hoặc tin nhắn SMS nhé! 🏮";
            }

            var statusText = order.Status switch
            {
                OrderStatus.PendingPayment => "⏳ Chờ thanh toán chuyển khoản",
                OrderStatus.Paid           => "✅ Đã thanh toán (Đang chuẩn bị gói hàng)",
                OrderStatus.Confirmed      => "📦 Đã xác nhận đơn (Chờ đóng gói)",
                OrderStatus.Shipping       => "🚚 Đang giao hàng đến bạn",
                OrderStatus.Delivered      => "🎉 Đã giao hàng thành công",
                OrderStatus.Cancelled      => "❌ Đơn hàng đã bị hủy",
                _                          => order.Status.ToString()
            };

            bool isCod = (order.PaymentMethod != null && order.PaymentMethod.Contains("COD"))
                         || (order.Note != null && order.Note.Contains("COD"));

            var paymentMethodText = isCod ? "Thanh toán khi nhận hàng (COD)" : "Chuyển khoản VietQR tự động";
            var orderTimeVn = order.CreatedAt.AddHours(7);

            var sb = new StringBuilder();
            sb.AppendLine($"📦 <b>THÔNG TIN ĐƠN HÀNG #{WebUtility.HtmlEncode(order.OrderCode)}</b>");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"📊 <b>Trạng thái:</b> <b>{statusText}</b>");
            sb.AppendLine($"💳 <b>Phương thức:</b> {paymentMethodText}");
            sb.AppendLine($"👤 <b>Người nhận:</b> {WebUtility.HtmlEncode(order.CustomerName)} (📞 {WebUtility.HtmlEncode(order.CustomerPhone)})");
            sb.AppendLine($"📍 <b>Địa chỉ:</b> {WebUtility.HtmlEncode(order.ShippingAddress)}");
            sb.AppendLine($"⏰ <b>Thời gian đặt:</b> {orderTimeVn:dd/MM/yyyy HH:mm} (Giờ VN)");
            sb.AppendLine();
            sb.AppendLine("🛍️ <b>Chi tiết sản phẩm:</b>");
            if (order.OrderDetails != null && order.OrderDetails.Any())
            {
                int index = 1;
                foreach (var d in order.OrderDetails)
                {
                    sb.AppendLine($"  {index++}. <b>{WebUtility.HtmlEncode(d.ProductName)}</b> × <b>{d.Quantity}</b> ({d.UnitPrice:N0}đ)");
                }
            }
            else
            {
                sb.AppendLine("  <i>(Xem chi tiết trên hệ thống)</i>");
            }

            sb.AppendLine();
            sb.AppendLine($"💰 <b>Tổng thanh toán:</b> <b>{order.TotalAmount:N0} VNĐ</b>");
            if (!string.IsNullOrWhiteSpace(order.Note))
            {
                sb.AppendLine($"📝 <b>Ghi chú:</b> <i>{WebUtility.HtmlEncode(order.Note)}</i>");
            }

            sb.AppendLine();
            sb.AppendLine("💡 <i>Cần hỗ trợ thay đổi địa chỉ hoặc hối giao gấp, Quý khách vui lòng gọi Hotline 0901 234 567 nhé!</i>");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiAssistant] Lỗi khi tra cứu đơn hàng #{OrderCode}: {Message}", orderCode, ex.Message);
            return $"Dạ, đã xảy ra lỗi trong quá trình tra cứu đơn hàng #{orderCode}. Quý khách vui lòng thử lại sau ít phút hoặc liên hệ Hotline 0901 234 567 ạ!";
        }
    }

    /// <summary>
    /// Bộ phản hồi thông minh dự phòng (Fallback FAQ) khi không có mạng hoặc chưa cấu hình Gemini API
    /// </summary>
    private async Task<string> GenerateFallbackReplyAsync(string query)
    {
        var lower = query.ToLower();

        // 1. Hỏi về mã giảm giá
        if (lower.Contains("giảm giá") || lower.Contains("mã") || lower.Contains("voucher") || lower.Contains("coupon") || lower.Contains("khuyến mãi"))
        {
            try
            {
                var coupons = await _context.Coupons
                    .AsNoTracking()
                    .Where(c => c.IsActive && c.EndDate >= DateTime.UtcNow)
                    .OrderByDescending(c => c.DiscountPercentage)
                    .Take(3)
                    .ToListAsync();

                if (coupons.Any())
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("🎁 <b>CÁC MÃ GIẢM GIÁ ĐANG ÁP DỤNG HÔM NAY:</b>");
                    foreach (var c in coupons)
                    {
                        var minVal = c.MinOrderValue > 0 ? $"đơn từ {c.MinOrderValue:N0}đ" : "mọi đơn hàng";
                        sb.AppendLine($"• Nhập <code>{c.Code}</code>: Giảm {c.DiscountPercentage}% cho {minVal}.");
                    }
                    sb.AppendLine("👉 Quý khách hãy nhập mã ở bước Thanh toán để được giảm giá ngay nhé!");
                    return sb.ToString();
                }
            }
            catch
            {
                // Bỏ qua lỗi DB fallback
            }

            return "🎁 Bạn chỉ cần nhập Email vào ô Đăng ký nhận bản tin ở chân trang, hệ thống sẽ tự động gửi mã <code>HUEDACSAN10</code> giảm 10% vào Gmail của bạn ngay lập tức ạ!";
        }

        // 2. Hỏi về vận chuyển / ship
        if (lower.Contains("vận chuyển") || lower.Contains("ship") || lower.Contains("giao hàng") || lower.Contains("bao lâu") || lower.Contains("thời gian"))
        {
            return "🚚 <b>Chính sách giao nhận đặc sản Huế:</b>\n" +
                   "• Nội thành TP Huế: Giao nhanh trong 24h bằng shipper riêng.\n" +
                   "• Các tỉnh thành khác: Giao từ 2-3 ngày qua bưu điện/GHN, đóng thùng xốp hút chân không bảo quản tiêu chuẩn.\n" +
                   "• Miễn phí giao hàng cho đơn từ 500.000đ!";
        }

        // 3. Hỏi về phương thức thanh toán
        if (lower.Contains("thanh toán") || lower.Contains("vietqr") || lower.Contains("cod") || lower.Contains("chuyển khoản") || lower.Contains("tiền mặt"))
        {
            return "💳 <b>Phương thức thanh toán tiện lợi:</b>\n" +
                   "1. <b>Chuyển khoản VietQR tự động (PayOS/BIDV):</b> Quét mã QR thanh toán tức thì, hệ thống tự động gạch nợ và xác nhận đơn.\n" +
                   "2. <b>Thanh toán khi nhận hàng (COD):</b> Khách hàng được kiểm tra đúng đặc sản Huế trước khi thanh toán tiền mặt cho shipper.";
        }

        // 4. Hỏi về đặc sản nổi bật / gợi ý món ngon
        if (lower.Contains("đặc sản") || lower.Contains("nổi bật") || lower.Contains("gợi ý") || lower.Contains("mua gì") || lower.Contains("quà biếu"))
        {
            try
            {
                var topProducts = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.IsAvailable && p.StockQuantity > 0)
                    .OrderByDescending(p => p.Price)
                    .Take(4)
                    .ToListAsync();

                if (topProducts.Any())
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("🏮 <b>TOP ĐẶC SẢN HUẾ ĐƯỢC YÊU THÍCH NHẤT:</b>");
                    foreach (var p in topProducts)
                    {
                        sb.AppendLine($"• <b>{p.Name}</b>: {p.Price:N0}đ (Tồn kho: {p.StockQuantity})");
                    }
                    sb.AppendLine("💡 Tất cả đặc sản đều được chọn lọc từ các làng nghề gia truyền Cố Đô Huế!");
                    return sb.ToString();
                }
            }
            catch
            {
                // Bỏ qua lỗi
            }

            return "🏮 Đặc sản Huế nổi tiếng gồm có: Mè xửng giòn Thuận An, Tôm chua gia truyền, Trà Cung Đình Nhất Dạ Đế Vương, Bánh ép khô giòn rụm và Nem tré Cố Đô. Mời Quý khách dạo xem trên trang chủ để chọn món ưng ý nhé!";
        }

        // 5. Câu trả lời mặc định
        return "Dạ, cảm ơn Quý khách đã nhắn tin! Em là Trợ Lý Ẩm Thực Cố Đô. Quý khách có thể:\n" +
               "• Nhập mã đơn hàng dạng <code>HUE-...</code> để tra cứu lộ trình đơn.\n" +
               "• Hỏi về sản phẩm, giá bán, bảo quản hoặc các mã giảm giá hôm nay.\n" +
               "• Gọi ngay Hotline <b>0901 234 567</b> để được chuyên viên ẩm thực Huế tư vấn trực tiếp 24/7 ạ! 🏮";
    }
}
