namespace EcommerceHue.Models;

/// <summary>
/// Đại diện cho một lượt tin nhắn trong lịch sử hội thoại (Multi-turn Chat)
/// </summary>
public class ChatMessageItem
{
    /// <summary>
    /// Vai trò người nói: 'user' (khách hàng) hoặc 'model' (trợ lý AI)
    /// </summary>
    public string Role { get; set; } = "user";

    /// <summary>
    /// Nội dung tin nhắn
    /// </summary>
    public string Text { get; set; } = string.Empty;
}
