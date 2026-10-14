using System.ComponentModel.DataAnnotations;

namespace EcommerceHue.Models;

/// <summary>
/// Bài viết tin tức / Cẩm nang ẩm thực Huế (Blog Module).
/// </summary>
public sealed class Post
{
    public int Id { get; set; }

    /// <summary>Tiêu đề bài viết</summary>
    [Required(ErrorMessage = "Tiêu đề bài viết không được để trống.")]
    [MaxLength(250, ErrorMessage = "Tiêu đề không vượt quá 250 ký tự.")]
    public required string Title { get; set; }

    /// <summary>Đường dẫn thân thiện URL chuẩn SEO (ví dụ: top-5-quan-bun-bo-hue-ngon)</summary>
    [Required(ErrorMessage = "Slug không được để trống.")]
    [MaxLength(250)]
    public required string Slug { get; set; }

    /// <summary>Tóm tắt ngắn bài viết hiển thị ở danh sách</summary>
    [Required(ErrorMessage = "Tóm tắt bài viết không được để trống.")]
    [MaxLength(500)]
    public required string Summary { get; set; }

    /// <summary>Nội dung chi tiết bài viết (hỗ trợ HTML/văn bản đa dạng)</summary>
    [Required(ErrorMessage = "Nội dung bài viết không được để trống.")]
    public required string Content { get; set; }

    /// <summary>Đường dẫn ảnh đại diện bài viết</summary>
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>Ngày tạo/đăng bài viết</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>Trạng thái hiển thị công khai (true = hiển thị, false = bản nháp/ẩn)</summary>
    public bool IsPublished { get; set; } = true;
}
