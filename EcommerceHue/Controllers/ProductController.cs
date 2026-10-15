using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Models;

namespace EcommerceHue.Controllers;

/// <summary>
/// Controller hiển thị chi tiết sản phẩm và các thông tin chuẩn hóa theo quy chuẩn EcommerceHue.
/// </summary>
public class ProductController(EcommerceContext context, ILogger<ProductController> logger) : Controller
{
    // =========================================================================
    // ACTION: Chi tiết sản phẩm – GET /Product/Detail/{id} hoặc GET /Product/{id}
    // =========================================================================

    /// <summary>
    /// Hiển thị trang chi tiết sản phẩm theo Id: thông tin thương mại, mã SKU/Barcode,
    /// và 4 Tab thông tin mở rộng (Xuất xứ & Pháp lý, Thành phần & Specs, HDSD & Bảo quản, Chính sách & Bảo hành).
    /// </summary>
    /// <param name="id">Mã định danh sản phẩm</param>
    [HttpGet]
    [Route("Product/Detail/{id:int}")]
    [Route("Product/{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        // 1. Truy vấn sản phẩm theo Id và include thông tin Category
        var product = await context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id && p.IsAvailable);

        if (product == null)
        {
            logger.LogWarning("[ProductDetail] Không tìm thấy sản phẩm với Id = {ProductId}", id);
            return NotFound();
        }

        // 2. Lấy danh sách sản phẩm liên quan cùng danh mục để hiển thị ở cuối trang
        var relatedProducts = await context.Products
            .AsNoTracking()
            .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id && p.IsAvailable)
            .Take(4)
            .ToListAsync();

        ViewBag.RelatedProducts = relatedProducts;

        return View(product);
    }

    /// <summary>
    /// Alias hỗ trợ endpoint /Product/Details/{id} theo chuẩn ASP.NET Core mặc định.
    /// </summary>
    [HttpGet]
    [Route("Product/Details/{id:int}")]
    public Task<IActionResult> Details(int id) => Detail(id);
}
