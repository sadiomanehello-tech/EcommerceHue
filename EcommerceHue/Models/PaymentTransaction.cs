using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models;

/// <summary>
/// Trạng thái kết quả giao dịch thanh toán từ VnPay phản hồi
/// </summary>
public enum PaymentStatus
{
    /// <summary>Đang xử lý / chưa có phản hồi</summary>
    Pending = 0,

    /// <summary>Thanh toán thành công (VnPay xác nhận)</summary>
    Success = 1,

    /// <summary>Thanh toán thất bại</summary>
    Failed = 2,

    /// <summary>Chữ ký không hợp lệ – có dấu hiệu giả mạo (Data Tampering)</summary>
    InvalidSignature = 3,

    /// <summary>Giao dịch đã bị hoàn tiền</summary>
    Refunded = 4
}

/// <summary>
/// Nhật ký (log) mỗi lần VnPay phản hồi (IPN / Return URL).
/// Theo quy tắc 02-security-vnpay.md: lưu đầy đủ để phục vụ kiểm toán bảo mật,
/// đặc biệt ghi lại mọi trường hợp chữ ký không hợp lệ.
/// </summary>
public sealed class PaymentTransaction
{
    public int Id { get; set; }

    // --- Khóa ngoại ---

    /// <summary>Id đơn hàng liên quan</summary>
    [Required]
    public int OrderId { get; set; }

    // --- Thông tin từ VnPay phản hồi ---

    /// <summary>Mã giao dịch phía VnPay (vnp_TransactionNo)</summary>
    [MaxLength(50)]
    public string? VnpTransactionNo { get; set; }

    /// <summary>Mã đơn hàng gửi sang VnPay (vnp_TxnRef)</summary>
    [MaxLength(50)]
    public string? VnpTxnRef { get; set; }

    /// <summary>Mã phản hồi (vnp_ResponseCode). "00" = thành công</summary>
    [MaxLength(10)]
    public string? VnpResponseCode { get; set; }

    /// <summary>Mã ngân hàng (vnp_BankCode)</summary>
    [MaxLength(20)]
    public string? VnpBankCode { get; set; }

    /// <summary>Số thẻ (vnp_CardType, VD: ATM / VISA)</summary>
    [MaxLength(20)]
    public string? VnpCardType { get; set; }

    /// <summary>Số tiền giao dịch (vnp_Amount / 100 = số tiền thực, VNĐ)</summary>
    public long VnpAmount { get; set; }

    /// <summary>Thời gian giao dịch tại VnPay (vnp_PayDate – yyyyMMddHHmmss)</summary>
    [MaxLength(20)]
    public string? VnpPayDate { get; set; }

    /// <summary>Thông tin đơn hàng mô tả (vnp_OrderInfo)</summary>
    [MaxLength(255)]
    public string? VnpOrderInfo { get; set; }

    // --- Kiểm tra chữ ký (theo quy tắc 02-security-vnpay.md) ---

    /// <summary>
    /// Kết quả xác thực chữ ký HMACSHA512.
    /// TRUE = hợp lệ; FALSE = nghi vấn giả mạo → KHÔNG cập nhật trạng thái đơn hàng.
    /// </summary>
    public bool IsSignatureValid { get; set; }

    /// <summary>Chữ ký nhận được từ VnPay (vnp_SecureHash)</summary>
    [MaxLength(256)]
    public string? ReceivedSecureHash { get; set; }

    // --- Trạng thái xử lý ---

    /// <summary>Kết quả xử lý giao dịch phía hệ thống</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>
    /// Ghi chú lỗi / cảnh báo bảo mật để phục vụ kiểm toán.
    /// Ví dụ: "Chữ ký không khớp – IP: 1.2.3.4 – nghi giả mạo dữ liệu"
    /// </summary>
    [MaxLength(1000)]
    public string? AuditNote { get; set; }

    /// <summary>Thời điểm nhận phản hồi từ VnPay</summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation Properties ---

    /// <summary>Đơn hàng liên quan</summary>
    public Order? Order { get; set; }
}
