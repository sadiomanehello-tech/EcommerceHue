using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models;

/// <summary>
/// Mã giảm giá (Coupon / Voucher) cho khách hàng.
/// </summary>
public sealed class Coupon
{
    public int Id { get; set; }

    /// <summary>
    /// Mã giảm giá (Ví dụ: HUEWELCOME10)
    /// </summary>
    [Required(ErrorMessage = "Mã giảm giá không được để trống.")]
    [MaxLength(50)]
    public required string Code { get; set; }

    /// <summary>
    /// Phần trăm giảm giá (Ví dụ: 10 ứng với 10%)
    /// </summary>
    [Required]
    [Range(1, 100, ErrorMessage = "Phần trăm giảm giá phải từ 1% đến 100%.")]
    public int DiscountPercentage { get; set; }

    /// <summary>
    /// Trạng thái hoạt động của mã
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Thời điểm tạo mã giảm giá
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
