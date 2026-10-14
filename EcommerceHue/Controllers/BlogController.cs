using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Models;

namespace EcommerceHue.Controllers;

/// <summary>
/// Quản lý luồng hiển thị Tin tức & Cẩm nang ẩm thực Huế (Blog Module).
/// </summary>
public class BlogController(EcommerceContext context, ILogger<BlogController> logger) : Controller
{
    // =========================================================================
    // DANH SÁCH BÀI VIẾT – GET /Blog hoặc GET /Blog/Index
    // =========================================================================

    /// <summary>
    /// Hiển thị danh sách các bài viết đã xuất bản, sắp xếp theo ngày đăng mới nhất.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var posts = await context.Posts
            .AsNoTracking()
            .Where(p => p.IsPublished)
            .OrderByDescending(p => p.CreatedDate)
            .ToListAsync();

        return View(posts);
    }

    // =========================================================================
    // CHI TIẾT BÀI VIẾT – GET /blog/{slug}
    // =========================================================================

    /// <summary>
    /// Hiển thị chi tiết nội dung bài viết dựa trên đường dẫn Slug thân thiện chuẩn SEO.
    /// </summary>
    /// <param name="slug">Chuỗi định danh URL thân thiện (ví dụ: top-5-quan-bun-bo-hue-ngon)</param>
    [HttpGet("blog/{slug}")]
    public async Task<IActionResult> Details(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return RedirectToAction(nameof(Index));

        var normalizedSlug = slug.Trim().ToLowerInvariant();

        var post = await context.Posts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == normalizedSlug && p.IsPublished);

        if (post is null)
        {
            logger.LogWarning("[Blog] Không tìm thấy bài viết với slug: '{Slug}'", slug);
            return NotFound();
        }

        // Lấy danh sách các bài viết mới khác để hiển thị khối gợi ý đọc thêm
        var recentPosts = await context.Posts
            .AsNoTracking()
            .Where(p => p.IsPublished && p.Id != post.Id)
            .OrderByDescending(p => p.CreatedDate)
            .Take(3)
            .ToListAsync();

        ViewBag.RecentPosts = recentPosts;

        return View(post);
    }
}
