using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EcommerceHue.Models;

/// <summary>
/// Chi tiết từng dòng sản phẩm trong một đơn hàng (bảng trung gian Order - Product)
/// </summary>
public sealed class OrderDetail
{
    public int Id { get; set; }

    // --- Khóa ngoại ---

    /// <summary>Id đơn hàng cha</summary>
    [Required]
    public int OrderId { get; set; }

    /// <summary>Id sản phẩm</summary>
    [Required]
    public int ProductId { get; set; }

    // --- Snapshot tại thời điểm đặt hàng ---
    // (Lưu lại tên và giá để tránh ảnh hưởng khi sản phẩm thay đổi sau này)

    /// <summary>Tên sản phẩm tại thời điểm đặt hàng</summary>
    [Required]
    [MaxLength(200)]
    public required string ProductName { get; set; }

    /// <summary>Đơn giá tại thời điểm đặt hàng (VNĐ)</summary>
    [Required]
    [Column(TypeName = "decimal(18,0)")]
    public decimal UnitPrice { get; set; }

    /// <summary>Số lượng đặt mua</summary>
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public int Quantity { get; set; }

    /// <summary>Thành tiền = UnitPrice × Quantity</summary>
    [Column(TypeName = "decimal(18,0)")]
    public decimal LineTotal => UnitPrice * Quantity;

    // --- Navigation Properties ---

    /// <summary>Đơn hàng cha</summary>
    public Order? Order { get; set; }

    /// <summary>Sản phẩm</summary>
    public Product? Product { get; set; }
}
