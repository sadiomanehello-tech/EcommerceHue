using System;
using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models;

/// <summary>
/// Mã giảm giá (Coupon / Voucher) thông minh cho khách hàng.
/// Hỗ trợ kiểm tra giá trị đơn tối thiểu, mức giảm tối đa, thời hạn và số lượt dùng.
/// </summary>
public sealed class Coupon
{
    public int Id { get; set; }

    /// <summary>
    /// Mã giảm giá (Ví dụ: HUEWELCOME10, SALE1111)
    /// </summary>
    [Required(ErrorMessage = "Mã giảm giá không được để trống.")]
    [MaxLength(50)]
    public required string Code { get; set; }

    /// <summary>
    /// Mô tả chi tiết chương trình khuyến mãi (Ví dụ: Giảm 15% mừng ngày đôi 11/11)
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Phần trăm giảm giá (Ví dụ: 10 ứng với 10%)
    /// </summary>
    [Required]
    [Range(1, 100, ErrorMessage = "Phần trăm giảm giá phải từ 1% đến 100%.")]
    public int DiscountPercentage { get; set; }

    /// <summary>
    /// Giá trị đơn hàng tối thiểu (VNĐ) để áp dụng mã (Mặc định 0đ = không giới hạn)
    /// </summary>
    [Range(0, 100_000_000, ErrorMessage = "Giá trị đơn tối thiểu không hợp lệ.")]
    public decimal MinOrderValue { get; set; } = 0;

    /// <summary>
    /// Mức giảm tối đa (VNĐ). Nếu null = giảm theo đúng %, không chặn trần
    /// </summary>
    [Range(0, 10_000_000, ErrorMessage = "Mức giảm tối đa không hợp lệ.")]
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>
    /// Thời điểm bắt đầu có hiệu lực
    /// </summary>
    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Thời điểm hết hạn sử dụng (Mặc định 1 tháng từ lúc tạo)
    /// </summary>
    public DateTime EndDate { get; set; } = DateTime.UtcNow.AddMonths(1);

    /// <summary>
    /// Tổng số lượt được phép sử dụng tối đa của mã
    /// </summary>
    [Range(1, 1_000_000, ErrorMessage = "Giới hạn sử dụng phải từ 1 lượt trở lên.")]
    public int UsageLimit { get; set; } = 100;

    /// <summary>
    /// Số lượt đã được khách hàng sử dụng thực tế
    /// </summary>
    public int UsedCount { get; set; } = 0;

    /// <summary>
    /// Nhãn phân loại sự kiện ('Normal', 'DoubleDay', 'Holiday', 'Weekend', 'EndOfMonth')
    /// </summary>
    [MaxLength(50)]
    public string EventTag { get; set; } = "Normal";

    /// <summary>
    /// Trạng thái hoạt động của mã
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Thời điểm tạo mã giảm giá trong hệ thống
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
