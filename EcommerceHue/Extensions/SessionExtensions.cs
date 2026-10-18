using System.Text.Json;

namespace EcommerceHue.Extensions;

/// <summary>
/// Phương thức mở rộng (Extension Methods) cho ISession để lưu/đọc
/// đối tượng phức tạp dưới dạng JSON trong ASP.NET Core Session.
/// </summary>
public static class SessionExtensions
{
    /// <summary>
    /// Serialize đối tượng <typeparamref name="T"/> thành JSON và lưu vào Session.
    /// </summary>
    /// <typeparam name="T">Kiểu đối tượng cần lưu</typeparam>
    /// <param name="session">Session hiện tại</param>
    /// <param name="key">Khóa Session</param>
    /// <param name="value">Giá trị cần lưu</param>
    public static void SetObject<T>(this ISession session, string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        session.SetString(key, json);
    }

    /// <summary>
    /// Đọc và deserialize đối tượng <typeparamref name="T"/> từ Session.
    /// Trả về <c>default(T)</c> nếu khóa không tồn tại hoặc chuỗi JSON không hợp lệ.
    /// </summary>
    /// <typeparam name="T">Kiểu đối tượng cần đọc</typeparam>
    /// <param name="session">Session hiện tại</param>
    /// <param name="key">Khóa Session</param>
    /// <returns>Đối tượng được deserialize hoặc default(T)</returns>
    public static T? GetObject<T>(this ISession session, string key)
    {
        try
        {
            var json = session.GetString(key);
            if (string.IsNullOrWhiteSpace(json))
                return default;

            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }
}
