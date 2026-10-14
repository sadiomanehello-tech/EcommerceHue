using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models.ViewModels;

/// <summary>
/// ViewModel cho trang Checkout – tự động điền sẵn thông tin từ tài khoản khách hàng.
/// </summary>
public sealed class CheckoutViewModel
{
    // --- Thông tin giao hàng (điền sẵn từ Customer Claims) ---

    [Required(ErrorMessage = "Họ tên người nhận không được để trống.")]
    [MaxLength(150)]
    [Display(Name = "Họ tên người nhận")]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress]
    [MaxLength(150)]
    [Display(Name = "Email")]
    public string CustomerEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại không được để trống.")]
    [Phone]
    [MaxLength(20)]
    [Display(Name = "Số điện thoại")]
    public string CustomerPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Địa chỉ giao hàng không được để trống.")]
    [MaxLength(500)]
    [Display(Name = "Địa chỉ giao hàng")]
    public string ShippingAddress { get; set; } = string.Empty;

    [MaxLength(1000)]
    [Display(Name = "Ghi chú")]
    public string? Note { get; set; }

    // --- Thông tin giỏ hàng (chỉ đọc) ---

    /// <summary>Danh sách sản phẩm trong giỏ</summary>
    public List<CartItem> CartItems { get; set; } = [];

    /// <summary>Tổng tiền hàng (VNĐ)</summary>
    public decimal SubTotal { get; set; }

    /// <summary>Phí vận chuyển</summary>
    public decimal ShippingFee { get; set; } = 0;

    /// <summary>Mã giảm giá đã áp dụng (nếu có)</summary>
    public string? CouponCode { get; set; }

    /// <summary>Phần trăm giảm giá (%)</summary>
    public int? DiscountPercentage { get; set; }

    /// <summary>Số tiền được giảm giá (VNĐ)</summary>
    public decimal DiscountAmount { get; set; } = 0;

    /// <summary>
    /// Phương thức thanh toán: "BankTransfer" (Chuyển khoản), "MoMo" (Quét mã MoMo), "VnPay" (Cổng VnPay)
    /// </summary>
    public string PaymentMethod { get; set; } = "VnPay";

    /// <summary>Tổng thanh toán cuối cùng sau khi trừ mã giảm giá</summary>
    public decimal TotalAmount => Math.Max(0, SubTotal + ShippingFee - DiscountAmount);
}
