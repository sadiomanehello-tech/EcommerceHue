using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models;

/// <summary>
/// Danh mục sản phẩm đặc sản Huế (ví dụ: Bánh, Mứt, Nước chấm, ...)
/// </summary>
public sealed class Category
{
    public int Id { get; set; }

    /// <summary>Tên danh mục</summary>
    [Required(ErrorMessage = "Tên danh mục không được để trống.")]
    [MaxLength(100, ErrorMessage = "Tên danh mục không vượt quá 100 ký tự.")]
    public required string Name { get; set; }

    /// <summary>Mô tả ngắn về danh mục</summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Đường dẫn ảnh đại diện danh mục</summary>
    [MaxLength(255)]
    public string? ImageUrl { get; set; }

    /// <summary>Thứ tự hiển thị trên trang chủ</summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>Trạng thái kích hoạt danh mục</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Thời điểm tạo bản ghi</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Navigation Properties ---

    /// <summary>Danh sách sản phẩm thuộc danh mục này</summary>
    public ICollection<Product> Products { get; set; } = [];
}
