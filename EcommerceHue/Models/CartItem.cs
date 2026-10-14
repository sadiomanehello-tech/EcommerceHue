namespace EcommerceHue.Models;

/// <summary>
/// Một dòng sản phẩm trong giỏ hàng – lưu tạm thời qua ASP.NET Core Session (JSON).
/// Đây là POCO thuần túy, KHÔNG ánh xạ vào Database.
/// </summary>
public sealed class CartItem
{
    /// <summary>Id sản phẩm – dùng làm khóa tra cứu trong Session</summary>
    public int ProductId { get; set; }

    /// <summary>Tên sản phẩm (snapshot tại thời điểm thêm vào giỏ)</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Đơn giá (VNĐ) – snapshot tại thời điểm thêm vào giỏ</summary>
    public decimal Price { get; set; }

    /// <summary>Số lượng khách đặt</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Đường dẫn ảnh đại diện sản phẩm</summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Thành tiền = Đơn giá × Số lượng.
    /// Thuộc tính tính toán, không lưu vào Session (JsonIgnore ẩn).
    /// </summary>
    public decimal Total => Price * Quantity;
}
