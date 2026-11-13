using System.Text.Json;
using EcommerceHue.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace EcommerceHue.Controllers;

/// <summary>
/// Webhook tiếp nhận tin nhắn từ người dùng Telegram Bot,
/// kết nối với AiAssistantService để tự động tra cứu đơn hàng và tư vấn đặc sản Huế.
/// </summary>
[ApiController]
[Route("api/telegram")]
public class TelegramWebhookController : ControllerBase
{
    private readonly AiAssistantService _aiAssistantService;
    private readonly TelegramService _telegramService;
    private readonly ILogger<TelegramWebhookController> _logger;

    public TelegramWebhookController(
        AiAssistantService aiAssistantService,
        TelegramService telegramService,
        ILogger<TelegramWebhookController> logger)
    {
        _aiAssistantService = aiAssistantService;
        _telegramService = telegramService;
        _logger = logger;
    }

    /// <summary>
    /// Endpoint nhận dữ liệu Webhook từ Telegram Server
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook([FromBody] JsonElement update)
    {
        try
        {
            // Trích xuất tin nhắn từ payload Telegram (hỗ trợ message hoặc edited_message)
            JsonElement messageElement;
            if (!update.TryGetProperty("message", out messageElement) &&
                !update.TryGetProperty("edited_message", out messageElement))
            {
                return Ok();
            }

            // Trích xuất thông tin Chat ID của người gửi
            if (!messageElement.TryGetProperty("chat", out var chatElement) ||
                !chatElement.TryGetProperty("id", out var idElement))
            {
                return Ok();
            }

            long chatId = idElement.GetInt64();

            // Trích xuất nội dung văn bản người dùng nhắn
            var incomingText = string.Empty;
            if (messageElement.TryGetProperty("text", out var textElement))
            {
                incomingText = textElement.GetString()?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(incomingText))
            {
                return Ok();
            }

            _logger.LogInformation("[TelegramWebhook] Nhận tin nhắn từ ChatId {ChatId}: '{Text}'", chatId, incomingText);

            // 1. Xử lý lệnh /start
            if (incomingText.Equals("/start", StringComparison.OrdinalIgnoreCase))
            {
                var startReply =
                    "🏮 <b>Chào mừng Quý khách đến với Trợ Lý Ẩm Thực Cố Đô Huế!</b>\n\n" +
                    "Em là Bot AI đại diện cửa hàng trực tuyến <b>EcommerceHue</b> sẵn sàng hỗ trợ Quý khách 24/7:\n\n" +
                    "• 🔍 <b>Tra cứu đơn hàng:</b> Nhập <code>/tracuu HUE-xxx</code> hoặc chỉ cần dán mã đơn hàng vào đây.\n" +
                    "• ⭐ <b>Tư vấn đặc sản:</b> Nhắn bất kỳ câu hỏi nào về Mè xửng, Tôm chua, Trà Cung Đình, Nem tré, Nón lá bài thơ...\n" +
                    "• 🎟️ <b>Mã giảm giá hôm nay:</b> Hỏi 'Hôm nay có voucher khuyến mãi gì?'\n" +
                    "• 📖 Gõ <code>/help</code> để xem hướng dẫn sử dụng.\n\n" +
                    "👉 Quý khách muốn em tư vấn món ngon hay kiểm tra đơn hàng nào ạ?";

                await _telegramService.SendMessageToChatIdAsync(chatId, startReply);
                return Ok();
            }

            // 2. Xử lý lệnh /help
            if (incomingText.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                var helpReply =
                    "📖 <b>HƯỚNG DẪN SỬ DỤNG BOT ẨM THỰC HUẾ:</b>\n\n" +
                    "1. <b>Tra cứu đơn hàng nhanh:</b>\n" +
                    "   Cú pháp: <code>/tracuu HUE-202611...</code>\n" +
                    "   <i>(Hoặc dán trực tiếp chuỗi HUE-... vào khung chat)</i>\n\n" +
                    "2. <b>Hỏi tư vấn món ăn & Quà biếu Cố Đô:</b>\n" +
                    "   Ví dụ: 'Mè xửng giòn Thuận An ăn kèm trà gì ngon?', 'Gợi ý quà biếu Huế sang trọng'\n\n" +
                    "3. <b>Chính sách & Khuyến mãi:</b>\n" +
                    "   Ví dụ: 'Có mã giảm giá không?', 'Giao hàng về Đà Nẵng mất bao lâu?', 'Có cho ship COD không?'\n\n" +
                    "📞 Hotline CSKH hỗ trợ trực tiếp: <b>0901 234 567</b>";

                await _telegramService.SendMessageToChatIdAsync(chatId, helpReply);
                return Ok();
            }

            // 3. Xử lý lệnh /tracuu <Mã_đơn>
            var queryText = incomingText;
            if (incomingText.StartsWith("/tracuu", StringComparison.OrdinalIgnoreCase))
            {
                var parts = incomingText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    queryText = parts[1].Trim();
                }
                else
                {
                    await _telegramService.SendMessageToChatIdAsync(chatId,
                        "Dạ, Quý khách vui lòng nhập mã đơn hàng phía sau lệnh. Ví dụ: <code>/tracuu HUE-20261113-123</code> ạ! 📦");
                    return Ok();
                }
            }

            // 4. Xử lý qua AiAssistantService (tự động nhận diện tra cứu đơn hàng hoặc hỏi Gemini AI)
            var reply = await _aiAssistantService.GetSmartReplyAsync(queryText);

            // 5. Gửi câu trả lời về lại đúng chat.id của người dùng Telegram
            await _telegramService.SendMessageToChatIdAsync(chatId, reply);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TelegramWebhook] Lỗi xử lý tin nhắn Webhook: {Message}", ex.Message);
        }

        // Luôn trả về HTTP 200 OK để Telegram không gửi lại tin nhắn lặp lại
        return Ok();
    }
}
