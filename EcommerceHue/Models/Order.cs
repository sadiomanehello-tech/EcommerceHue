using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models;

/// <summary>
/// Trạng thái đơn hàng trong vòng đời xử lý
/// </summary>
public enum OrderStatus
{
    /// <summary>Chờ thanh toán</summary>
    PendingPayment = 0,

    /// <summary>Đã thanh toán – chờ xác nhận</summary>
    Paid = 1,

    /// <summary>Đã xác nhận – đang chuẩn bị hàng</summary>
    Confirmed = 2,

    /// <summary>Đang vận chuyển</summary>
    Shipping = 3,

    /// <summary>Đã giao hàng thành công</summary>
    Delivered = 4,

    /// <summary>Đơn hàng đã bị hủy</summary>
    Cancelled = 5
}

/// <summary>
/// Đơn hàng của khách hàng
/// </summary>
public sealed class Order
{
    public int Id { get; set; }

    /// <summary>Mã đơn hàng duy nhất (dùng để tra cứu / gửi VnPay)</summary>
    [Required]
    [MaxLength(50)]
    public required string OrderCode { get; set; }

    // --- Thông tin người đặt ---

    [Required(ErrorMessage = "Họ tên người nhận không được để trống.")]
    [MaxLength(150)]
    public required string CustomerName { get; set; }

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress]
    [MaxLength(150)]
    public required string CustomerEmail { get; set; }

    [Required(ErrorMessage = "Số điện thoại không được để trống.")]
    [Phone]
    [MaxLength(20)]
    public required string CustomerPhone { get; set; }

    /// <summary>Địa chỉ giao hàng</summary>
    [Required(ErrorMessage = "Địa chỉ giao hàng không được để trống.")]
    [MaxLength(500)]
    public required string ShippingAddress { get; set; }

    /// <summary>Ghi chú của khách</summary>
    [MaxLength(1000)]
    public string? Note { get; set; }

    // --- Tài chính ---

    /// <summary>Tổng tiền đơn hàng (VNĐ, chưa tính phí ship)</summary>
    public decimal SubTotal { get; set; }

    /// <summary>Phí vận chuyển (VNĐ)</summary>
    public decimal ShippingFee { get; set; } = 0;

    /// <summary>Tổng thanh toán = SubTotal + ShippingFee</summary>
    public decimal TotalAmount { get; set; }

    // --- Trạng thái ---

    /// <summary>Trạng thái đơn hàng</summary>
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;

    /// <summary>Thời điểm đặt hàng</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm cập nhật gần nhất</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation Properties ---

    /// <summary>Danh sách chi tiết đơn hàng</summary>
    public ICollection<OrderDetail> OrderDetails { get; set; } = [];

    /// <summary>Lịch sử giao dịch thanh toán VnPay</summary>
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = [];
}
