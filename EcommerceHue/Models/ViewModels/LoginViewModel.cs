using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models.ViewModels;

/// <summary>
/// ViewModel cho form Đăng nhập.
/// </summary>
public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Vui lòng nhập Email hoặc Số điện thoại.")]
    [Display(Name = "Email hoặc Số điện thoại")]
    public string EmailOrPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Ghi nhớ đăng nhập (Cookie persistent)</summary>
    [Display(Name = "Ghi nhớ đăng nhập")]
    public bool RememberMe { get; set; }

    /// <summary>URL chuyển hướng sau khi đăng nhập thành công</summary>
    public string? ReturnUrl { get; set; }
}
