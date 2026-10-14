using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EcommerceHue.Models;

/// <summary>
/// Sản phẩm đặc sản Huế.
/// Bắt buộc phải có <see cref="StockQuantity"/> (số lượng tồn kho)
/// và <see cref="ExpiryDate"/> (hạn sử dụng) theo quy chuẩn dự án.
/// </summary>
public sealed class Product
{
    public int Id { get; set; }

    /// <summary>Tên sản phẩm</summary>
    [Required(ErrorMessage = "Tên sản phẩm không được để trống.")]
    [MaxLength(200, ErrorMessage = "Tên sản phẩm không vượt quá 200 ký tự.")]
    public required string Name { get; set; }

    /// <summary>Mô tả chi tiết sản phẩm</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Giá bán (VNĐ)</summary>
    [Required]
    [Column(TypeName = "decimal(18,0)")]
    [Range(0, double.MaxValue, ErrorMessage = "Giá sản phẩm phải lớn hơn hoặc bằng 0.")]
    public decimal Price { get; set; }

    /// <summary>
    /// Giá gốc trước khi giảm (VNĐ, nếu có).
    /// Đánh dấu [NotMapped] để phục vụ hiển thị giá gạch ngang mà không đổi schema CSDL.
    /// </summary>
    [NotMapped]
    public decimal? OriginalPrice { get; set; }

    /// <summary>Đường dẫn ảnh chính của sản phẩm</summary>
    [MaxLength(255)]
    public string? ImageUrl { get; set; }

    /// <summary>Slug thân thiện URL (ví dụ: banh-it-ram-hue)</summary>
    [MaxLength(200)]
    public string? Slug { get; set; }

    // =====================================================================
    // BẮTBUỘC theo quy chuẩn 01-coding-style.md:
    // Mọi Product liên quan đến thực phẩm đặc sản Huế đều phải có
    // StockQuantity và ExpiryDate.
    // =====================================================================

    /// <summary>Số lượng tồn kho hiện tại</summary>
    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho không được âm.")]
    public int StockQuantity { get; set; }

    /// <summary>Hạn sử dụng của sản phẩm (thực phẩm)</summary>
    [Required(ErrorMessage = "Hạn sử dụng là thông tin bắt buộc đối với thực phẩm.")]
    public DateOnly ExpiryDate { get; set; }

    // =====================================================================

    /// <summary>Trạng thái đang bán hay ẩn</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>Thời điểm tạo bản ghi</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Thời điểm cập nhật gần nhất</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // --- Khóa ngoại ---

    /// <summary>Id danh mục sản phẩm</summary>
    [Required]
    public int CategoryId { get; set; }

    // --- Navigation Properties ---

    /// <summary>Danh mục sản phẩm</summary>
    public Category? Category { get; set; }

    /// <summary>Danh sách chi tiết đơn hàng chứa sản phẩm này</summary>
    public ICollection<OrderDetail> OrderDetails { get; set; } = [];
}
