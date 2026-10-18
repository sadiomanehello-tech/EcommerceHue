using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace EcommerceHue.Extensions;

/// <summary>
/// Phương thức mở rộng cho ISession: Hỗ trợ lưu trữ và đọc dữ liệu đối tượng phức tạp (JSON) an toàn tuyệt đối.
/// Bẫy lỗi Null Reference và ngoại lệ JSON để tránh lỗi HTTP 500 khi chạy trên môi trường Cloud / Render.
/// </summary>
public static class SessionExtensions
{
    /// <summary>
    /// Đọc dữ liệu phức tạp từ Session theo Key với cơ chế bẫy lỗi null string và try-catch toàn diện.
    /// </summary>
    public static T? GetComplexData<T>(this ISession? session, string key)
    {
        try
        {
            if (session == null)
            {
                return default;
            }

            var data = session.GetString(key);
            if (string.IsNullOrEmpty(data))
            {
                // Hỗ trợ dự phòng đọc thêm khóa "ShoppingCart" nếu tìm "Cart" chưa thấy
                if (key == "Cart")
                {
                    data = session.GetString("ShoppingCart");
                    if (string.IsNullOrEmpty(data))
                    {
                        return default; // Trả về null an toàn nếu chưa có giỏ hàng
                    }
                }
                else
                {
                    return default; // Trả về null an toàn nếu chưa có giỏ hàng
                }
            }

            return JsonSerializer.Deserialize<T>(data);
        }
        catch
        {
            return default; // Bẻ gãy mọi lỗi xung đột chuỗi JSON
        }
    }

    /// <summary>
    /// Lưu trữ dữ liệu phức tạp vào Session dưới dạng chuỗi JSON.
    /// </summary>
    public static void SetComplexData(this ISession? session, string key, object value)
    {
        try
        {
            if (session == null) return;
            session.SetString(key, JsonSerializer.Serialize(value));
        }
        catch
        {
            // Bỏ qua ngoại lệ ghi session để giữ hệ thống hoạt động ổn định
        }
    }

    /// <summary>
    /// Alias tương thích cho GetComplexData (phục vụ các Controller cũ sử dụng GetObject)
    /// </summary>
    public static T? GetObject<T>(this ISession? session, string key) => session.GetComplexData<T>(key);

    /// <summary>
    /// Alias tương thích cho SetComplexData (phục vụ các Controller cũ sử dụng SetObject)
    /// </summary>
    public static void SetObject<T>(this ISession? session, string key, T value)
    {
        if (value != null)
        {
            session.SetComplexData(key, value);
        }
    }
}
