using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models;

/// <summary>
/// Thông tin khách hàng đăng ký tài khoản trên website đặc sản Huế.
/// Mật khẩu luôn được lưu dưới dạng BCrypt hash (quy tắc 02-security-vnpay.md).
/// </summary>
public sealed class Customer
{
    public int Id { get; set; }

    /// <summary>Tên đăng nhập – duy nhất trong hệ thống</summary>
    [Required(ErrorMessage = "Tên đăng nhập không được để trống.")]
    [MaxLength(50, ErrorMessage = "Tên đăng nhập không vượt quá 50 ký tự.")]
    public required string Username { get; set; }

    /// <summary>
    /// Mật khẩu đã được băm bằng BCrypt.
    /// TUYỆT ĐỐI KHÔNG lưu mật khẩu dạng plaintext (quy tắc 02-security-vnpay.md).
    /// </summary>
    [Required]
    [MaxLength(255)]
    public required string PasswordHash { get; set; }

    /// <summary>Họ và tên đầy đủ</summary>
    [Required(ErrorMessage = "Họ tên không được để trống.")]
    [MaxLength(150, ErrorMessage = "Họ tên không vượt quá 150 ký tự.")]
    public required string FullName { get; set; }

    /// <summary>Địa chỉ email</summary>
    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(150)]
    public required string Email { get; set; }

    /// <summary>Số điện thoại liên hệ</summary>
    [Required(ErrorMessage = "Số điện thoại không được để trống.")]
    [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
    [MaxLength(20)]
    public required string PhoneNumber { get; set; }

    /// <summary>Địa chỉ giao hàng mặc định</summary>
    [Required(ErrorMessage = "Địa chỉ giao hàng không được để trống.")]
    [MaxLength(500)]
    public required string Address { get; set; }

    /// <summary>Thời điểm đăng ký tài khoản</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation Properties ---

    /// <summary>Danh sách đơn hàng của khách hàng</summary>
    public ICollection<Order> Orders { get; set; } = [];
}
