using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Extensions;
using EcommerceHue.Models;
using EcommerceHue.Models.ViewModels;
using System.Security.Claims;

namespace EcommerceHue.Controllers;

/// <summary>
/// Xử lý toàn bộ luồng Giỏ hàng: thêm, xóa, cập nhật số lượng và hiển thị.
/// Dữ liệu giỏ hàng được lưu qua ASP.NET Core Session dưới dạng JSON.
/// </summary>
public class CartController(EcommerceContext context) : Controller
{
    // Khóa Session duy nhất cho giỏ hàng
    private const string CartSessionKey = "ShoppingCart";

    // =========================================================================
    // HELPER: Đọc / ghi giỏ hàng từ Session
    // =========================================================================

    /// <summary>Đọc danh sách CartItem hiện tại từ Session.</summary>
    private List<CartItem> GetCart()
        => HttpContext.Session.GetObject<List<CartItem>>(CartSessionKey) ?? [];

    /// <summary>Lưu danh sách CartItem vào Session.</summary>
    private void SaveCart(List<CartItem> cart)
        => HttpContext.Session.SetObject(CartSessionKey, cart);

    // =========================================================================
    // ACTION: Hiển thị giỏ hàng – GET /Cart
    // =========================================================================

    /// <summary>
    /// Đọc giỏ hàng từ Session và truyền xuống View để hiển thị.
    /// </summary>
    public IActionResult Index()
    {
        var cart = GetCart();
        return View(cart);
    }

    // =========================================================================
    // API: Lấy số lượng sản phẩm trong giỏ hàng – GET /Cart/GetCartCount
    // Endpoint công khai cho JavaScript navbar cập nhật badge số lượng.
    // =========================================================================

    /// <summary>
    /// Trả về tổng số lượng item trong giỏ hàng hiện tại dưới dạng JSON.
    /// </summary>
    [HttpGet]
    public IActionResult GetCartCount()
    {
        var cart  = GetCart();
        // Tổng số lượng (không phải số dòng): ví dụ 2 cái áo + 3 cái quần = 5
        var count = cart.Sum(i => i.Quantity);
        return Json(new { count });
    }

    // =========================================================================
    // ACTION: Thêm sản phẩm vào giỏ – POST /Cart/AddToCart
    // =========================================================================

    /// <summary>
    /// Thêm sản phẩm vào giỏ hàng qua Fetch API (AJAX), trả về JSON.
    /// Phương thức POST, xác thực qua header X-Requested-With do client gửi kèm.
    /// - success: true/false
    /// - message: thông báo hiển thị Toast
    /// - cartCount: tổng số lượng sản phẩm trong giỏ (cập nhật badge navbar)
    /// </summary>
    /// <param name="productId">Id sản phẩm cần thêm</param>
    [HttpPost]
    [IgnoreAntiforgeryToken]   // AJAX gửi header X-Requested-With thay thế CSRF token
    public async Task<IActionResult> AddToCart(int productId)
    {
        // Kiểm tra yêu cầu đến từ AJAX (bảo vệ cơ bản chống CSRF)
        if (Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            return BadRequest(new { success = false, message = "Yêu cầu không hợp lệ." });

        // Truy vấn sản phẩm từ DB (AsNoTracking vì chỉ đọc)
        var product = await context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.IsAvailable);

        if (product is null)
            return Json(new { success = false, message = "Sản phẩm không tồn tại hoặc đã ngừng bán." });

        var cart         = GetCart();
        var existingItem = cart.FirstOrDefault(c => c.ProductId == productId);

        if (existingItem is not null)
        {
            // Kiểm tra ràng buộc tồn kho trước khi tăng số lượng
            if (existingItem.Quantity >= product.StockQuantity)
                return Json(new
                {
                    success   = false,
                    message   = $"⚠️ Đã đạt giới hạn tồn kho ({product.StockQuantity} sản phẩm).",
                    cartCount = cart.Sum(i => i.Quantity)
                });

            existingItem.Quantity++;
        }
        else
        {
            // Kiểm tra tồn kho lần đầu thêm vào giỏ
            if (product.StockQuantity <= 0)
                return Json(new { success = false, message = $"Sản phẩm '{product.Name}' đã hết hàng." });

            cart.Add(new CartItem
            {
                ProductId   = product.Id,
                ProductName = product.Name,
                Price       = product.Price,
                Quantity    = 1,
                ImageUrl    = product.ImageUrl
            });
        }

        SaveCart(cart);

        var newCount = cart.Sum(i => i.Quantity);
        return Json(new
        {
            success   = true,
            message   = $"✅ Đã thêm '{product.Name}' vào giỏ hàng!",
            cartCount = newCount
        });
    }

    // =========================================================================
    // ACTION: Xóa sản phẩm khỏi giỏ – POST /Cart/RemoveFromCart
    // =========================================================================

    /// <summary>
    /// Xóa một sản phẩm khỏi giỏ hàng trong Session theo ProductId.
    /// </summary>
    /// <param name="productId">Id sản phẩm cần xóa</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveFromCart(int productId)
    {
        var cart = GetCart();
        var item = cart.FirstOrDefault(c => c.ProductId == productId);

        if (item is not null)
        {
            cart.Remove(item);
            SaveCart(cart);
            TempData["CartSuccess"] = $"\U0001f5d1\ufe0f \u0110\u00e3 x\u00f3a '{item.ProductName}' kh\u1ecfi gi\u1ecf h\u00e0ng.";
        }

        return RedirectToAction(nameof(Index));
    }

    // =========================================================================
    // ACTION: Cập nhật số lượng – POST /Cart/UpdateQuantity
    // =========================================================================

    /// <summary>
    /// Cập nhật số lượng sản phẩm trong giỏ hàng.
    /// Nếu số lượng mới ≤ 0 thì tự động xóa khỏi giỏ.
    /// Kiểm tra ràng buộc tồn kho từ DB.
    /// </summary>
    /// <param name="productId">Id sản phẩm cần cập nhật</param>
    /// <param name="quantity">Số lượng mới</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantity(int productId, int quantity)
    {
        var cart = GetCart();
        var item = cart.FirstOrDefault(c => c.ProductId == productId);

        if (item is null)
            return RedirectToAction(nameof(Index));

        if (quantity <= 0)
        {
            // Số lượng về 0 → tự động xóa khỏi giỏ
            cart.Remove(item);
            SaveCart(cart);
            return RedirectToAction(nameof(Index));
        }

        // Kiểm tra tồn kho từ DB
        var stockQuantity = await context.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.StockQuantity)
            .FirstOrDefaultAsync();

        if (quantity > stockQuantity)
        {
            TempData["CartWarning"] =
                $"⚠️ Chỉ còn {stockQuantity} sản phẩm trong kho. Số lượng đã được điều chỉnh.";
            item.Quantity = stockQuantity;
        }
        else
        {
            item.Quantity = quantity;
        }

        SaveCart(cart);
        return RedirectToAction(nameof(Index));
    }

    // =========================================================================
    // ACTION: Xóa toàn bộ giỏ hàng – POST /Cart/Clear
    // =========================================================================

    /// <summary>Xóa hoàn toàn giỏ hàng trong Session.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Clear()
    {
        HttpContext.Session.Remove(CartSessionKey);
        TempData["CartSuccess"] = "Giỏ hàng đã được làm trống.";
        return RedirectToAction(nameof(Index));
    }

    // =========================================================================
    // ACTION: Trang Checkout – GET /Cart/Checkout
    // [Authorize]: Nếu chưa đăng nhập, tự động chuyển sang /Account/Login?returnUrl=/Cart/Checkout
    // =========================================================================

    /// <summary>
    /// Hiển thị trang đặt hàng. Yêu cầu đăng nhập ([Authorize]).
    /// Tự động điền sẵn Thông tin Giao hàng từ Claims của user hiện tại.
    /// </summary>
    [Authorize]
    [HttpGet]
    public IActionResult Checkout()
    {
        var cart = GetCart();

        // Giỏ hàng trống – chuyển về trang giỏ hàng kèm thông báo
        if (cart.Count == 0)
        {
            TempData["CartWarning"] = "⚠️ Giỏ hàng đang trống. Vui lòng thêm sản phẩm trước khi đặt hàng.";
            return RedirectToAction(nameof(Index));
        }

        // Đọc thông tin từ Claims (do AccountController ghi khi đăng nhập)
        var fullName = User.FindFirstValue("FullName")        ?? string.Empty;
        var email    = User.FindFirstValue(ClaimTypes.Email)  ?? string.Empty;
        var phone    = User.FindFirstValue("PhoneNumber")     ?? string.Empty;
        var address  = User.FindFirstValue("Address")         ?? string.Empty;

        var model = new CheckoutViewModel
        {
            // Tự động điền sẵn từ Claims – user có thể sửa trước khi gửi
            CustomerName    = fullName,
            CustomerEmail   = email,
            CustomerPhone   = phone,
            ShippingAddress = address,
            CartItems       = cart,
            SubTotal        = cart.Sum(i => i.Total)
        };

        return View(model);
    }
}
