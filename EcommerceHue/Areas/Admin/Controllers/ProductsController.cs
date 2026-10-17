using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Models;

namespace EcommerceHue.Areas.Admin.Controllers;

/// <summary>
/// Phân hệ Quản trị Sản phẩm & Điều phối Tồn kho (Inventory Management) dành cho Admin.
/// Áp dụng cơ chế phân quyền nghiêm ngặt [Authorize(Roles = "Admin")].
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/[controller]")]
[Route("Admin/[controller]/[action]")]
public class ProductsController(
    EcommerceContext context,
    ILogger<ProductsController> logger) : Controller
{
    // =========================================================================
    // 1. ACTION: Danh sách sản phẩm & Báo cáo tồn kho – GET /Admin/Products
    // =========================================================================

    /// <summary>
    /// Hiển thị toàn bộ danh sách đặc sản Huế kèm thông tin tồn kho (StockQuantity)
    /// và hạn sử dụng (ExpiryDate), ưu tiên hiển thị các mặt hàng hết hàng hoặc sắp hết lên đầu.
    /// </summary>
    [HttpGet]
    [Route("/Admin/Products")]
    [Route("/Admin/Products/Index")]
    public async Task<IActionResult> Index()
    {
        var products = await context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .OrderBy(p => p.StockQuantity) // Ưu tiên hết hàng / sắp hết lên trên để thủ kho chú ý
            .ThenBy(p => p.Id)
            .ToListAsync();

        logger.LogInformation("[Admin] Tải danh sách sản phẩm quản lý tồn kho: {Count} mặt hàng", products.Count);

        return View(products);
    }

    // =========================================================================
    // 2. API: Nhập thêm hàng nhanh (Quick Restock) – POST /Admin/Products/QuickRestock
    // =========================================================================

    /// <summary>
    /// Tiếp nhận yêu cầu nhập thêm hàng cho sản phẩm, tăng số lượng tồn kho (Chiều tăng)
    /// và trả về kết quả JSON để giao diện cập nhật ngay lập tức mà không tải lại trang.
    /// </summary>
    /// <param name="productId">Mã định danh sản phẩm</param>
    /// <param name="quantity">Số lượng nhập thêm (phải > 0)</param>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> QuickRestock(int productId, int quantity)
    {
        if (productId <= 0)
        {
            return BadRequest(new { success = false, message = "Mã sản phẩm không hợp lệ." });
        }

        if (quantity == 0)
        {
            return BadRequest(new { success = false, message = "Số lượng điều chỉnh kho phải khác 0." });
        }

        var product = await context.Products.FindAsync(productId);
        if (product == null)
        {
            logger.LogWarning("[Admin QuickRestock] Không tìm thấy sản phẩm với ID = {ProductId}", productId);
            return NotFound(new { success = false, message = "Không tìm thấy sản phẩm cần điều chỉnh tồn kho." });
        }

        // Kiểm tra an toàn: Số lượng sau khi giảm không được âm
        if (product.StockQuantity + quantity < 0)
        {
            return BadRequest(new
            {
                success = false,
                message = $"Số lượng giảm ({Math.Abs(quantity)} gói) vượt quá tồn kho hiện tại ({product.StockQuantity} gói). Tồn kho không thể âm!"
            });
        }

        var oldStock = product.StockQuantity;
        // Thực hiện điều chỉnh kho 2 chiều (Cộng số dương = nhập kho, cộng số âm = trừ hao hụt/lỗi):
        product.StockQuantity += quantity;

        await context.SaveChangesAsync();

        var actionText = quantity > 0 ? $"Nhập thêm {quantity}" : $"Điều chỉnh giảm {Math.Abs(quantity)}";
        logger.LogInformation(
            "[Admin QuickRestock] {ActionText} gói/hộp cho '{ProductName}' (ID: {ProductId}). Tồn kho từ {OldStock} -> {NewStock}",
            actionText, product.Name, product.Id, oldStock, product.StockQuantity);

        return Json(new
        {
            success = true,
            message = $"{actionText} sản phẩm cho '{product.Name}' thành công!",
            productId = product.Id,
            productName = product.Name,
            oldStockQuantity = oldStock,
            newStockQuantity = product.StockQuantity,
            newStock = product.StockQuantity,
            isOutOfStock = product.StockQuantity == 0
        });
    }
}
