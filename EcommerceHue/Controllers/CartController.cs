using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Extensions;
using EcommerceHue.Models;
using EcommerceHue.Models.ViewModels;
using VnPay.NET;
using System.Security.Claims;
using EcommerceHue.Services;
using Net.payOS;
using Net.payOS.Types;

namespace EcommerceHue.Controllers;

/// <summary>
/// Xử lý toàn bộ luồng Giỏ hàng: thêm, xóa, cập nhật số lượng và hiển thị.
/// Dữ liệu giỏ hàng được lưu qua ASP.NET Core Session dưới dạng JSON.
/// </summary>
[AllowAnonymous]
public class CartController(
    EcommerceContext context,
    IConfiguration configuration,
    ILogger<CartController> logger,
    IEmailService emailService,
    PayOS payOS) : Controller
{
    private readonly IEmailService _emailService = emailService;

    // Khóa Session duy nhất cho giỏ hàng
    private const string CartSessionKey = "ShoppingCart";

    // =========================================================================
    // HELPER: Đọc / ghi giỏ hàng từ Session
    // =========================================================================

    /// <summary>Đọc danh sách CartItem hiện tại từ Session an toàn tuyệt đối.</summary>
    private List<CartItem> GetCart()
    {
        try
        {
            if (HttpContext == null || HttpContext.Session == null)
            {
                return new List<CartItem>();
            }

            // Ưu tiên đọc key chính thức của dự án
            string? cartJson = HttpContext.Session.GetString("ShoppingCart") 
                           ?? HttpContext.Session.GetString("Cart");

            if (string.IsNullOrEmpty(cartJson))
            {
                return new List<CartItem>();
            }

            // Bọc khối deserialize trong try-catch nhỏ để tránh chuỗi lỗi làm sập hệ thống
            try
            {
                var items = System.Text.Json.JsonSerializer.Deserialize<List<CartItem>>(cartJson);
                return items ?? new List<CartItem>();
            }
            catch
            {
                // Nếu chuỗi JSON cũ lỗi cấu trúc, làm sạch Session để tránh kẹt lỗi
                HttpContext.Session.Remove("ShoppingCart");
                HttpContext.Session.Remove("Cart");
                return new List<CartItem>();
            }
        }
        catch
        {
            return new List<CartItem>();
        }
    }

    /// <summary>Lưu danh sách CartItem vào Session đồng bộ.</summary>
    private void SaveCart(List<CartItem> cart)
    {
        try
        {
            if (HttpContext != null && HttpContext.Session != null)
            {
                var cartJson = System.Text.Json.JsonSerializer.Serialize(cart);
                HttpContext.Session.SetString("ShoppingCart", cartJson);
                HttpContext.Session.SetString("Cart", cartJson);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi ghi nhận giỏ hàng vào Session.");
        }
    }

    // =========================================================================
    // ACTION: Hiển thị giỏ hàng – GET /Cart
    // =========================================================================

    /// <summary>
    /// Đọc giỏ hàng từ Session và truyền xuống View để hiển thị.
    /// Cho phép truy cập ẩn danh (khách chưa đăng nhập vẫn xem được giỏ hàng).
    /// </summary>
    [HttpGet]
    public IActionResult Index()
    {
        try
        {
            var cart = GetCart() ?? [];
            return View(cart);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi khi nạp trang giỏ hàng: {Message}", ex.Message);
            return View(new List<CartItem>());
        }
    }

    // =========================================================================
    // API: Lấy số lượng sản phẩm trong giỏ hàng – GET /Cart/GetCartCount
    // Endpoint công khai cho JavaScript navbar cập nhật badge số lượng.
    // =========================================================================

    [AllowAnonymous]
    [HttpGet]
    public IActionResult GetCartCount()
    {
        try
        {
            var cart = GetCart();
            int totalCount = cart?.Sum(i => i.Quantity) ?? 0;
            return Json(totalCount);
        }
        catch
        {
            return Json(0);
        }
    }

    // =========================================================================
    // ACTION: Thêm sản phẩm vào giỏ – POST /Cart/AddToCart
    // =========================================================================

    /// <summary>
    /// Thêm sản phẩm vào giỏ hàng qua Fetch API (AJAX), trả về JSON.
    /// Tự động bẫy lỗi DB để fallback lưu Session thuần túy, không bao giờ làm sập trang.
    /// </summary>
    /// <param name="productId">Id sản phẩm cần thêm</param>
    /// <param name="quantity">Số lượng cần thêm (mặc định 1)</param>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> AddToCart(int productId, int quantity = 1)
    {
        try
        {
            if (quantity < 1) quantity = 1;

            var cart = HttpContext.Session.GetComplexData<List<CartItem>>("Cart") 
                    ?? HttpContext.Session.GetObject<List<CartItem>>(CartSessionKey) 
                    ?? new List<CartItem>();

            Product? product = null;
            bool isDbError = false;

            try
            {
                // Đọc thông tin sản phẩm từ CSDL
                product = await context.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == productId && p.IsAvailable);
            }
            catch (Exception dbEx)
            {
                isDbError = true;
                logger.LogWarning(dbEx, "=== [FALLBACK ACTIVE] Database lỗi (isDbError: {IsDbError}), chuyển sang lưu Session thuần túy: {Message}", isDbError, dbEx.Message);
            }

            var existingItem = cart.Find(c => c.ProductId == productId);

            if (existingItem != null)
            {
                if (product != null && existingItem.Quantity + quantity > product.StockQuantity)
                {
                    return Json(new { success = false, message = $"⚠️ Đã đạt giới hạn tồn kho ({product.StockQuantity} sản phẩm).", cartCount = cart.Sum(i => i.Quantity) });
                }
                existingItem.Quantity += quantity;
            }
            else
            {
                // Fallback nạp tên tĩnh nếu DB bị lỗi ngắt mạch
                string productName = product?.Name ?? (productId == 1 ? "Mè xửng Thiên Hương" : productId == 2 ? "Tôm chua Trọng Đức" : $"Đặc sản Huế #{productId}");
                decimal price      = product?.Price ?? 45000m;
                string imageUrl    = product?.ImageUrl ?? $"/images/products/product{productId}.jpg";

                if (product != null && product.StockQuantity < quantity)
                {
                    return Json(new { success = false, message = $"Số lượng yêu cầu ({quantity}) vượt quá tồn kho ({product.StockQuantity}).", cartCount = cart.Sum(i => i.Quantity) });
                }

                cart.Add(new CartItem
                {
                    ProductId   = productId,
                    ProductName = productName,
                    Price       = price,
                    Quantity    = quantity,
                    ImageUrl    = imageUrl
                });
            }

            // Lưu đồng bộ vào bộ nhớ đệm Session
            HttpContext.Session.SetObject(CartSessionKey, cart);
            HttpContext.Session.SetComplexData("Cart", cart);

            int totalCount = cart.Sum(i => i.Quantity);
            string successMsg = product != null 
                ? $"✅ Đã thêm {quantity} '{product.Name}' vào giỏ hàng thành công!" 
                : $"✅ Đã thêm đặc sản vào giỏ hàng thành công (Chế độ offline)!";

            return Json(new { success = true, message = successMsg, cartCount = totalCount });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "=== [CRITICAL CART ERROR]: {Message}", ex.Message);
            return Json(new { success = false, message = "Lỗi hệ thống giỏ hàng: " + ex.Message });
        }
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
            TempData["CartSuccess"] = $"🗑️ Đã xóa '{item.ProductName}' khỏi giỏ hàng.";
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
    /// API: Kiểm tra tồn kho toàn bộ giỏ hàng trước khi chuyển sang bước thanh toán.
    /// Trả về JSON để hiển thị Pop-up SweetAlert2 hoạt họa rung lắc tại trang Giỏ hàng.
    /// </summary>
    [HttpPost]
    [HttpGet]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CheckCartStock()
    {
        var cart = GetCart();
        if (cart.Count == 0)
        {
            return Json(new { success = false, message = "Giỏ hàng đang trống. Vui lòng thêm sản phẩm trước khi đặt hàng!" });
        }

        foreach (var item in cart)
        {
            var product = await context.Products.FindAsync(item.ProductId);
            if (product == null) continue;

            if (item.Quantity > product.StockQuantity)
            {
                logger.LogWarning(
                    "[CheckCartStock] Phát hiện vượt tồn kho: '{ProductName}' (ID: {ProductId}). Đặt: {ReqQty} > Kho: {Stock}",
                    product.Name, product.Id, item.Quantity, product.StockQuantity);

                return Json(new
                {
                    success = false,
                    productId = product.Id,
                    productName = product.Name,
                    stockQuantity = product.StockQuantity,
                    orderQuantity = item.Quantity,
                    message = $"Sản phẩm '{product.Name}' hiện tại chỉ còn đúng {product.StockQuantity} hũ trong kho. Bạn đang đặt {item.Quantity} hũ, vui lòng vào giỏ hàng giảm số lượng để có thể đặt hàng!"
                });
            }
        }

        return Json(new { success = true });
    }

    /// <summary>
    /// Hiển thị trang đặt hàng. Yêu cầu đăng nhập ([Authorize]).
    /// Tự động điền sẵn Thông tin Giao hàng từ Claims của user hiện tại.
    [Authorize]
    [HttpGet]
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Checkout()
    {
        try
        {
            var cart = GetCart();

            // Giỏ hàng trống – chuyển về trang giỏ hàng kèm thông báo
            if (cart.Count == 0)
            {
                if (Request.Method == "POST" || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Giỏ hàng đang trống. Vui lòng thêm sản phẩm trước khi đặt hàng!" });
                }
                TempData["CartWarning"] = "⚠️ Giỏ hàng đang trống. Vui lòng thêm sản phẩm trước khi đặt hàng.";
                return RedirectToAction(nameof(Index));
            }

            // Bẫy kiểm tra tồn kho ngay khi mở trang Checkout hoặc khi gọi POST từ Cart
            foreach (var item in cart)
            {
                var product = await context.Products.FindAsync(item.ProductId);
                if (product != null && item.Quantity > product.StockQuantity)
                {
                    var msg = $"Sản phẩm '{product.Name}' hiện tại chỉ còn đúng {product.StockQuantity} hũ trong kho. Bạn đang đặt {item.Quantity} hũ, vui lòng vào giỏ hàng giảm số lượng để có thể đặt hàng!";
                    if (Request.Method == "POST" || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new
                        {
                            success = false,
                            productId = product.Id,
                            productName = product.Name,
                            message = msg
                        });
                    }
                    TempData["CartWarning"] = msg;
                    return RedirectToAction(nameof(Index));
                }
            }

            // Nếu là yêu cầu POST từ Giỏ hàng (AJAX kiểm tra kho trước khi bẻ hướng sang Checkout/payOS)
            if (Request.Method == "POST" || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new
                {
                    success = true,
                    checkoutUrl = "/Cart/Checkout"
                });
            }

            // Đọc thông tin từ Claims (do AccountController ghi khi đăng nhập)
            var fullName = User.FindFirstValue("FullName")        ?? string.Empty;
            var email    = User.FindFirstValue(ClaimTypes.Email)  ?? string.Empty;
            var phone    = User.FindFirstValue("PhoneNumber")     ?? string.Empty;
            var address  = User.FindFirstValue("Address")         ?? string.Empty;

            // Đọc thông tin mã giảm giá từ Session nếu đã áp dụng
            var couponCode = HttpContext.Session.GetString("CouponCode");
            var discountPercentage = HttpContext.Session.GetInt32("DiscountPercentage");
            var discountAmountStr = HttpContext.Session.GetString("DiscountAmount");
            decimal.TryParse(discountAmountStr, out var discountAmount);

            var model = new CheckoutViewModel
            {
                // Tự động điền sẵn từ Claims – user có thể sửa trước khi gửi
                CustomerName       = fullName,
                CustomerEmail      = email,
                CustomerPhone      = phone,
                ShippingAddress    = address,
                CartItems          = cart,
                SubTotal           = cart.Sum(i => i.Total),
                CouponCode         = couponCode,
                DiscountPercentage = discountPercentage,
                DiscountAmount     = discountAmount
            };

            return View(model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "=== [CHECKOUT ERROR]: {Message}", ex.Message);
            if (Request.Method == "POST" || Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = false, message = "Lỗi khi xử lý giỏ hàng: " + ex.Message });
            }
            TempData["CartWarning"] = "Không thể tải trang đặt hàng. Vui lòng thử lại!";
            return RedirectToAction(nameof(Index));
        }
    }

    // =========================================================================
    // API: Áp dụng mã giảm giá đơn hàng đầu tiên – POST /Cart/ApplyCoupon
    // =========================================================================

    /// <summary>
    /// Kiểm tra và áp dụng mã giảm giá cho đơn hàng đầu tiên (First Order Coupon).
    /// </summary>
    /// <param name="couponCode">Mã giảm giá người dùng nhập (ví dụ: HUEWELCOME10)</param>
    [HttpPost]
    [IgnoreAntiforgeryToken] // Hỗ trợ AJAX Fetch API
    public async Task<IActionResult> ApplyCoupon(string couponCode)
    {
        // ---------------------------------------------------------------------
        // Bước 1: Kiểm tra người dùng đã đăng nhập chưa. Lấy Email từ Claims
        // ---------------------------------------------------------------------
        if (User.Identity?.IsAuthenticated != true)
        {
            return Json(new { success = false, message = "Vui lòng đăng nhập để sử dụng mã giảm giá!" });
        }

        var userEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return Json(new { success = false, message = "Không tìm thấy thông tin email của tài khoản." });
        }

        if (string.IsNullOrWhiteSpace(couponCode))
        {
            return Json(new { success = false, message = "Vui lòng nhập mã giảm giá." });
        }

        var cleanCode = couponCode.Trim().ToUpperInvariant();

        // Kiểm tra mã tồn tại trong CSDL và đang hoạt động
        var coupon = await context.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == cleanCode && c.IsActive);

        if (coupon is null)
        {
            return Json(new { success = false, message = "Mã giảm giá không tồn tại hoặc đã hết hạn." });
        }

        // Kiểm tra giỏ hàng hiện tại
        var cart = GetCart();
        if (cart.Count == 0)
        {
            return Json(new { success = false, message = "Giỏ hàng của bạn đang trống." });
        }

        // ---------------------------------------------------------------------
        // Bước 2: Kiểm tra trong bảng Orders xem Email này đã có đơn hàng hợp lệ chưa
        // Các trạng thái đơn hàng được coi là hợp lệ: Đã thanh toán, Đã xác nhận, Đang giao, Hoàn thành
        // ---------------------------------------------------------------------
        var hasValidOrders = await context.Orders
            .AsNoTracking()
            .AnyAsync(o => o.CustomerEmail.ToLower() == userEmail.ToLower()
                        && (o.Status == OrderStatus.Paid
                            || o.Status == OrderStatus.Confirmed
                            || o.Status == OrderStatus.Shipping
                            || o.Status == OrderStatus.Delivered));

        if (hasValidOrders)
        {
            return Json(new
            {
                success = false,
                message = "Mã giảm giá này chỉ áp dụng cho đơn hàng đầu tiên của bạn!"
            });
        }

        // ---------------------------------------------------------------------
        // Bước 3: Tính toán số tiền được giảm theo % của Coupon & Lưu vào Session
        // ---------------------------------------------------------------------
        var subTotal = cart.Sum(i => i.Total);
        var discountAmount = Math.Round((subTotal * coupon.DiscountPercentage) / 100m, 0);
        var newTotal = Math.Max(0, subTotal - discountAmount);

        // Lưu thông tin vào Session để giữ trạng thái
        HttpContext.Session.SetString("CouponCode", coupon.Code);
        HttpContext.Session.SetString("DiscountAmount", discountAmount.ToString());
        HttpContext.Session.SetInt32("DiscountPercentage", coupon.DiscountPercentage);

        return Json(new
        {
            success = true,
            message = $"Áp dụng mã '{coupon.Code}' thành công! Bạn được giảm {coupon.DiscountPercentage}%.",
            couponCode = coupon.Code,
            discountPercentage = coupon.DiscountPercentage,
            discountAmount = discountAmount,
            discountAmountFormatted = discountAmount.ToString("N0") + " ₫",
            subTotal = subTotal,
            subTotalFormatted = subTotal.ToString("N0") + " ₫",
            newTotal = newTotal,
            newTotalFormatted = newTotal.ToString("N0") + " ₫"
        });
    }

    // =========================================================================
    // ACTION: Xử lý Đặt hàng & Chuyển hướng VnPay – POST /Cart/PlaceOrder
    // =========================================================================

    /// <summary>
    /// Tiếp nhận form đặt hàng từ trang Checkout, tạo Order với trạng thái PendingPayment
    /// và tạo URL chuyển hướng sang cổng thanh toán VnPay Sandbox với chữ ký bảo mật HMACSHA512.
    /// </summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PlaceOrder(CheckoutViewModel model)
    {
        try
        {
            var cart = GetCart();

        // 1. Kiểm tra giỏ hàng có rỗng không
        if (cart.Count == 0)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = false, message = "Giỏ hàng đang trống. Vui lòng thêm sản phẩm trước khi đặt hàng!" });
            }
            TempData["CartWarning"] = "⚠️ Giỏ hàng đang trống. Vui lòng thêm sản phẩm trước khi đặt hàng.";
            return RedirectToAction(nameof(Index));
        }

        // =====================================================================
        // BƯỚC 1: KIỂM TRA ĐIỀU KIỆN TỒN KHO TRƯỚC KHI GỌI API PAYOS (CHẶN BÁN VƯỢT MỨC)
        // Bắt buộc đặt logic này lên đầu hàm: Khách đặt > Tồn kho => CƯỠNG CHẾ CHẶN ĐƠN NGAY
        // =====================================================================
        foreach (var item in cart)
        {
            var product = await context.Products.FindAsync(item.ProductId);
            if (product == null) continue;

            // Kiểm tra nếu SỐ LƯỢNG KHÁCH ĐẶT MUA lớn hơn SỐ LƯỢNG THẬT TRONG KHO
            if (item.Quantity > product.StockQuantity)
            {
                logger.LogWarning(
                    "[Overselling Protection] CƯỠNG CHẾ CHẶN ĐƠN: '{ProductName}' (ID: {ProductId}). Khách đặt {ReqQty} > Tồn kho {Stock}",
                    product.Name, product.Id, item.Quantity, product.StockQuantity);

                // BẮT BUỘC trả về thông báo lỗi, chặn luồng ngay lập tức, KHÔNG CHẠY thêm bất kỳ lệnh nào phía sau
                return Json(new
                {
                    success = false,
                    productId = product.Id,
                    message = $"Sản phẩm '{product.Name}' hiện tại chỉ còn đúng {product.StockQuantity} hũ trong kho. Bạn đang đặt {item.Quantity} hũ, vui lòng vào giỏ hàng giảm số lượng để có thể đặt hàng!"
                });
            }
        }

        // 2. Kiểm tra tính hợp lệ của dữ liệu đầu vào
        if (!ModelState.IsValid)
        {
            var errors = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = false, message = "Thông tin giao hàng chưa đầy đủ: " + errors });
            }
            model.CartItems = cart;
            model.SubTotal = cart.Sum(i => i.Total);
            return View("Checkout", model);
        }

        // =====================================================================
        // BƯỚC 2: TRỪ TỒN KHO TRONG DATABASE (CHIỀU GIẢM)
        // Đảm bảo lệnh trừ kho CHỈ ĐƯỢC CHẠY sau khi tất cả các mặt hàng trong giỏ đã vượt qua vòng kiểm tra an toàn ở Bước 1
        // =====================================================================
        foreach (var item in cart)
        {
            var product = await context.Products.FindAsync(item.ProductId);
            if (product != null)
            {
                product.StockQuantity -= item.Quantity;
                logger.LogInformation(
                    "[Kho hàng - Chiều giảm] Trừ {Quantity} sản phẩm '{ProductName}' (ID: {ProductId}). Tồn kho còn lại: {NewStock}",
                    item.Quantity, product.Name, product.Id, product.StockQuantity);
            }
        }

        // 3. Đọc mã giảm giá và số tiền giảm từ Session (nếu có)
        var discountAmountStr = HttpContext.Session.GetString("DiscountAmount");
        decimal.TryParse(discountAmountStr, out var discountAmount);

        var subTotal = cart.Sum(i => i.Total);
        var totalAmount = Math.Max(0, subTotal - discountAmount);

        // 4. Sinh mã đơn hàng duy nhất theo định dạng HUE-yyyyMMddHHmmss-XXX
        var orderCode = $"HUE-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";

        var customerEmail = !string.IsNullOrWhiteSpace(model.CustomerEmail)
            ? model.CustomerEmail.Trim()
            : (User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? User.Identity?.Name ?? string.Empty);

        // 5. Khởi tạo đối tượng Order với trạng thái ban đầu là PendingPayment (Chờ thanh toán)
        var order = new Order
        {
            OrderCode       = orderCode,
            CustomerName    = model.CustomerName.Trim(),
            CustomerEmail   = customerEmail,
            CustomerPhone   = model.CustomerPhone.Trim(),
            ShippingAddress = model.ShippingAddress.Trim(),
            Note            = model.Note?.Trim(),
            SubTotal        = subTotal,
            ShippingFee     = 0,
            TotalAmount     = totalAmount,
            Status          = OrderStatus.PendingPayment, // Chờ thanh toán
            PaymentMethod   = "Open Banking (payOS)",
            PaymentStatus   = "Chờ thanh toán qua API Banking",
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow,
            OrderDetails    = cart.Select(item => new OrderDetail
            {
                ProductId   = item.ProductId,
                ProductName = item.ProductName,
                UnitPrice   = item.Price,
                Quantity    = item.Quantity
            }).ToList()
        };

        // 6. Lưu đơn hàng và cập nhật tồn kho sản phẩm vào cơ sở dữ liệu
        await context.Orders.AddAsync(order);
        await context.SaveChangesAsync();

        // 7. Xóa sạch giỏ hàng và session sau khi đặt hàng thành công
        HttpContext.Session.Remove(CartSessionKey);
        HttpContext.Session.Remove("CouponCode");
        HttpContext.Session.Remove("DiscountAmount");
        HttpContext.Session.Remove("DiscountPercentage");

        // 8. Khởi tạo thông tin thanh toán Open Banking qua payOS
        // Đưa mã đơn hàng vào phần description để hiển thị đẹp mắt trên payOS & ngân hàng
        string paymentContent = $"DH{order.Id} {order.OrderCode}";
        // Cắt tối đa 25 ký tự theo tiêu chuẩn độ dài nội dung chuyển khoản payOS / VietQR
        if (paymentContent.Length > 25)
        {
            paymentContent = paymentContent[..25];
        }

        var items = cart.Select(item => new ItemData(
            item.ProductName,
            item.Quantity,
            (int)item.Price
        )).ToList();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var returnUrl = $"{baseUrl}/Cart/OrderSuccess?orderCode={order.OrderCode}";
        var cancelUrl = $"{baseUrl}/Cart/Checkout";

        // Sinh mã payOS duy nhất: UnixTimestamp(10 chữ số) * 100000 + order.Id(tối đa 5 chữ số)
        // Tổng = 15 chữ số, luôn nằm trong phạm vi long.MaxValue (19 chữ số) → an toàn tuyệt đối
        // Webhook bóc tách ngược: orderId = uniqueOrderCode % 100000
        long uniqueOrderCode = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 100000L + order.Id;

        var paymentData = new PaymentData(
            orderCode: uniqueOrderCode,
            amount: (int)totalAmount,
            description: paymentContent,
            items: items,
            cancelUrl: cancelUrl,
            returnUrl: returnUrl,
            buyerName: order.CustomerName,
            buyerEmail: order.CustomerEmail,
            buyerPhone: order.CustomerPhone,
            buyerAddress: order.ShippingAddress
        );

        logger.LogInformation("[payOS] Khởi tạo link thanh toán động đơn hàng #{OrderId} - {OrderCode} | UniqueCode: {UniqueCode}, Nội dung CK: {PaymentContent}, Số tiền: {Amount}",
            order.Id, order.OrderCode, uniqueOrderCode, paymentContent, totalAmount);

        // Gọi hàm sinh link thanh toán động từ SDK payOS
        CreatePaymentResult result = await payOS.createPaymentLink(paymentData);

        logger.LogInformation("[payOS] Sinh link thanh toán thành công: {CheckoutUrl}", result.checkoutUrl);

        // 1. Tạo đường link ảnh QR động thông qua API mở của VietQR dựa trên thông tin thật của đơn hàng
        string qrImageSrc = $"https://img.vietqr.io/image/BIDV-8811843594-compact2.png?amount={order.TotalAmount}&addInfo=DH{order.Id}%20{order.OrderCode}&accountName=HO%20ANH%20TAI";

        // 2. Cấu hình bọc khối lệnh gửi Email Đặt hàng bằng try-catch cô lập để tránh nghẽn luồng
        try
        {
            if (!string.IsNullOrWhiteSpace(order.CustomerEmail))
            {
                var emailSubject = $"[EcommerceHue] Xác nhận đặt hàng thành công đơn hàng #{order.Id}";
                
                // Biên soạn template HTML nhúng link ảnh QR động vừa tạo ở Bước 1
                var emailHtml = BuildOrderConfirmationEmailHtml(order, result.checkoutUrl, qrImageSrc);
                
                // Sử dụng Task.Run Fire-and-Forget để email tự do truyền đi xuyên không gian mạng mà không bị ngắt luồng khi Redirect
                _ = Task.Run(async () => {
                    try {
                        await _emailService.SendOrderEmailAsync(order.CustomerEmail, emailSubject, emailHtml);
                    } catch (Exception exBg) {
                        Console.WriteLine("=== LỖI BẮN THƯ NỀN PLACEORDER: " + exBg.Message);
                    }
                });
                
                logger.LogInformation("=== [FIRE-AND-FORGET] Đã kích nổ tiến trình ngầm bắn Email 1 cho đơn #{OrderId}", order.Id);
            }
            else
            {
                logger.LogWarning("[Email] Không tìm thấy địa chỉ email của khách hàng cho đơn #{OrderId}", order.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "=== [SMTP LỖI NGHẼN MAIL]: " + ex.Message);
            // Cho dù lỗi gửi thư xảy ra, hệ thống vẫn phải chạy tiếp luồng phía dưới để Redirect khách sang payOS
        }

        // Nếu là yêu cầu AJAX từ frontend
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
            Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return Json(new
            {
                success = true,
                checkoutUrl = result.checkoutUrl
            });
        }

        // Thực hiện lệnh bẻ hướng đưa trình duyệt khách mua sang giao diện hóa đơn động bảo mật của payOS
        return Redirect(result.checkoutUrl);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "=== [PLACE ORDER ERROR]: {Message}", ex.Message);
        Console.WriteLine("=== [PLACE ORDER ERROR]: " + ex.Message);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
            Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return Json(new
            {
                success = false,
                message = "Lỗi xử lý đơn hàng: " + ex.Message
            });
        }

        TempData["CartError"] = "Có lỗi xảy ra trong quá trình đặt hàng: " + ex.Message;
        return RedirectToAction("Checkout");
    }
}

    // =========================================================================
    // ACTION: Tiếp nhận phản hồi từ VnPay – GET /Cart/VnPayReturn
    // Tuân thủ nghiêm ngặt quy tắc kiểm toán bảo mật 02-security-vnpay.md
    // =========================================================================

    /// <summary>
    /// Xử lý Return URL khi người dùng thực hiện thanh toán xong trên cổng VnPay.
    /// Kiểm tra tính toàn vẹn chữ ký HMACSHA512 trước khi cập nhật trạng thái đơn hàng.
    /// Ghi nhận nhật ký đầy đủ vào bảng PaymentTransactions để phục vụ kiểm toán bảo mật.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> VnPayReturn()
    {
        // 1. Đọc toàn bộ tham số do VnPay gửi về qua Query String bằng VnPayResponse
        var vnpayResponse = new VnPayResponse(Request.Query);

        var vnpTxnRef         = vnpayResponse.vnp_TxnRef ?? string.Empty;
        var vnpTransactionNo  = vnpayResponse.vnp_TransactionNo;
        var vnpResponseCode   = vnpayResponse.vnp_ResponseCode;
        var vnpSecureHash     = vnpayResponse.vnp_SecureHash ?? string.Empty;
        var vnpBankCode       = vnpayResponse.vnp_BankCode;
        var vnpCardType       = vnpayResponse.vnp_CardType;
        var vnpPayDate        = vnpayResponse.vnp_PayDate;
        var vnpOrderInfo      = vnpayResponse.vnp_OrderInfo;
        long.TryParse(vnpayResponse.vnp_Amount, out var vnpAmount);

        // 2. Lấy khóa bí mật HashSecret từ IConfiguration (tuyệt đối không hardcode)
        var vnpHashSecret = configuration["Vnpay:HashSecret"];
        if (string.IsNullOrWhiteSpace(vnpHashSecret))
        {
            vnpHashSecret = configuration["VnPay:HashSecret"];
        }
        if (string.IsNullOrWhiteSpace(vnpHashSecret))
        {
            vnpHashSecret = "NQDZOYXLTXUFTYJMSXNXREOSXNBUVFXA";
        }

        // 3. Thực hiện kiểm tra chữ ký an toàn (Checksum Validation với HMACSHA512) qua thư viện mới
        var isSignatureValid = vnpayResponse.ValidateSignature(vnpHashSecret);

        // 4. Tìm đơn hàng tương ứng trong CSDL theo OrderId (hoặc OrderCode để dự phòng)
        Order? order = null;
        if (int.TryParse(vnpTxnRef, out var orderId))
        {
            order = await context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.Id == orderId);
        }

        if (order is null)
        {
            order = await context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.OrderCode == vnpTxnRef);
        }

        // 5. Khởi tạo bản ghi PaymentTransaction để lưu vết kiểm toán bảo mật
        var transaction = new PaymentTransaction
        {
            OrderId            = order?.Id ?? (int.TryParse(vnpTxnRef, out var oId) ? oId : 0),
            VnpTransactionNo   = vnpTransactionNo,
            VnpTxnRef          = vnpTxnRef,
            VnpResponseCode    = vnpResponseCode,
            VnpBankCode        = vnpBankCode,
            VnpCardType        = vnpCardType,
            VnpAmount          = vnpAmount,
            VnpPayDate         = vnpPayDate,
            VnpOrderInfo       = vnpOrderInfo,
            IsSignatureValid   = isSignatureValid,
            ReceivedSecureHash = vnpSecureHash,
            ReceivedAt         = DateTime.UtcNow
        };

        // --- TRƯỜNG HỢP 1: CHỮ KÝ BẢO MẬT KHÔNG HỢP LỆ (NGHI NGỜ DATA TAMPERING) ---
        if (!isSignatureValid)
        {
            transaction.Status    = PaymentStatus.InvalidSignature;
            transaction.AuditNote = $"CẢNH BÁO BẢO MẬT: Chữ ký HMACSHA512 không khớp từ IP {VnPayResponse.GetIpAddress(HttpContext)}! Nghi vấn giả mạo dữ liệu.";

            logger.LogCritical("[SECURITY AUDIT] Checksum validation thất bại cho đơn hàng #{OrderId} - {OrderCode}! Hash nhận được: {Hash}, IP: {IP}",
                order?.Id, order?.OrderCode ?? vnpTxnRef, vnpSecureHash, VnPayResponse.GetIpAddress(HttpContext));

            await context.PaymentTransactions.AddAsync(transaction);
            await context.SaveChangesAsync();

            TempData["CheckoutError"] = "⚠️ Chữ ký bảo mật không hợp lệ (Nghi vấn giả mạo dữ liệu). Giao dịch bị từ chối.";
            return RedirectToAction(nameof(Checkout));
        }

        // --- TRƯỜNG HỢP 2: CHỮ KÝ HỢP LỆ VÀ GIAO DỊCH THÀNH CÔNG (vnp_ResponseCode == "00") ---
        if (vnpResponseCode == "00")
        {
            transaction.Status    = PaymentStatus.Success;
            transaction.AuditNote = $"Giao dịch thanh toán trực tuyến qua VnPay thành công. Mã GD VnPay: {vnpTransactionNo}";

            if (order is not null)
            {
                // Cập nhật trạng thái đơn hàng thành Đã thanh toán (Paid)
                order.Status    = OrderStatus.Paid;
                order.UpdatedAt = DateTime.UtcNow;
            }

            await context.PaymentTransactions.AddAsync(transaction);
            await context.SaveChangesAsync();

            // Xóa sạch giỏ hàng và session sau khi thanh toán thành công
            HttpContext.Session.Remove(CartSessionKey);
            HttpContext.Session.Remove("CouponCode");
            HttpContext.Session.Remove("DiscountAmount");
            HttpContext.Session.Remove("DiscountPercentage");

            TempData["PaymentMethod"] = "VnPay";
            TempData["VnpTransactionNo"] = vnpTransactionNo;
            logger.LogInformation("[VnPay] Giao dịch thành công cho đơn hàng: #{OrderId} - {OrderCode}, Mã GD VnPay: {TransNo}",
                order?.Id, order?.OrderCode, vnpTransactionNo);

            // Điều hướng sang trang hiển thị kết quả trực quan
            return RedirectToAction(nameof(OrderSuccess), new { orderCode = order?.OrderCode });
        }

        // --- TRƯỜNG HỢP 3: KHÁCH HÀNG HỦY GIAO DỊCH HOẶC THANH TOÁN THẤT BẠI ---
        if (order is not null)
        {
            // Cập nhật trạng thái đơn hàng thành Đã hủy (Cancelled)
            order.Status    = OrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;

            // HOÀN KHO TỰ ĐỘNG KHI HỦY ĐƠN (CHIỀU TĂNG):
            await context.Entry(order).Collection(o => o.OrderDetails).LoadAsync();
            foreach (var detail in order.OrderDetails)
            {
                var prod = await context.Products.FindAsync(detail.ProductId);
                if (prod != null)
                {
                    prod.StockQuantity += detail.Quantity; // Cộng trả lại kho
                    logger.LogInformation("[Hoàn kho - VnPay hủy] Đã hoàn lại {Quantity} sản phẩm '{ProductName}' (ID: {ProductId}). Tồn kho mới: {Stock}",
                        detail.Quantity, prod.Name, prod.Id, prod.StockQuantity);
                }
            }
        }

        transaction.Status    = PaymentStatus.Failed;
        transaction.AuditNote = $"Thanh toán thất bại hoặc khách hủy với mã phản hồi VnPay: {vnpResponseCode}";

        await context.PaymentTransactions.AddAsync(transaction);
        await context.SaveChangesAsync();

        logger.LogWarning("[VnPay] Thanh toán không thành công. Mã lỗi: {Code} cho đơn hàng: #{OrderId} - {OrderCode}",
            vnpResponseCode, order?.Id, order?.OrderCode ?? vnpTxnRef);

        TempData["CheckoutError"] = $"Giao dịch qua VnPay không thành công (Mã lỗi: {vnpResponseCode}).";

        // Điều hướng sang trang OrderSuccess để hiển thị kết quả trực quan theo yêu cầu
        return RedirectToAction(nameof(OrderSuccess), new { orderCode = order?.OrderCode });
    }

    // =========================================================================
    // API: Hủy đơn hàng chờ thanh toán & Hoàn kho tự động (Hết hạn payOS / Khách hủy)
    // POST /Cart/CancelPendingOrder
    // =========================================================================

    /// <summary>
    /// Xử lý hủy đơn hàng đang chờ thanh toán (PendingPayment - Status = 0) do hết hạn quét mã payOS
    /// hoặc khách hàng chủ động hủy. Tự động hoàn lại số lượng tồn kho cho các sản phẩm (Chiều tăng).
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CancelPendingOrder(int orderId)
    {
        if (orderId <= 0)
        {
            return BadRequest(new { success = false, message = "Mã đơn hàng không hợp lệ." });
        }

        var order = await context.Orders
            .Include(o => o.OrderDetails)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy đơn hàng cần hủy." });
        }

        // Chỉ hoàn kho khi đơn đang ở trạng thái Chờ thanh toán (Status = 0)
        if (order.Status == OrderStatus.PendingPayment)
        {
            order.Status = OrderStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;

            // Vòng lặp hoàn kho tự động (Chiều tăng):
            foreach (var detail in order.OrderDetails)
            {
                var prod = await context.Products.FindAsync(detail.ProductId);
                if (prod != null)
                {
                    prod.StockQuantity += detail.Quantity; // Cộng trả lại kho
                    logger.LogInformation(
                        "[Hoàn kho - Hết hạn thanh toán] Đã hoàn {Quantity} sản phẩm '{ProductName}' (ID: {ProductId}). Tồn kho mới: {StockQuantity}",
                        detail.Quantity, prod.Name, prod.Id, prod.StockQuantity);
                }
            }

            await context.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Đã hủy đơn hàng #{order.Id} và hoàn lại toàn bộ số lượng tồn kho sản phẩm thành công!",
                orderId = order.Id,
                status = (int)order.Status
            });
        }

        return BadRequest(new
        {
            success = false,
            message = "Chỉ có thể hủy và hoàn kho đối với đơn hàng đang ở trạng thái 'Chờ thanh toán'."
        });
    }

    // =========================================================================
    // ACTION: Endpoint Webhook tự động đối soát tiền tệ payOS (Gạch nợ ngầm)
    // POST /payos-webhook hoặc POST /Cart/payos-webhook
    // =========================================================================

    /// <summary>
    /// Tiếp nhận webhook từ cổng payOS Open Banking khi tiền đổ về tài khoản BIDV Hồ Anh Tài.
    /// </summary>
    [HttpPost("payos-webhook")]
    [IgnoreAntiforgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> PayOSWebhook([FromBody] WebhookType webhookBody)
    {
        // 1. Kiểm tra gói tin kiểm tra kết nối (Ping Test / Webhook Confirm) từ payOS
        if (webhookBody == null || webhookBody.data == null || 
            string.IsNullOrEmpty(webhookBody.data.description) || webhookBody.data.description == "confirm")
        {
            return Ok(new { message = "Webhook connected successfully" });
        }

        try
        {
            // 2. Xác thực tính toàn vẹn và chữ ký của webhook (Security Checksum Validation)
            WebhookData webhookData = payOS.verifyPaymentWebhookData(webhookBody);
            
            // 3. Bóc tách ngược orderId từ uniqueOrderCode:
            // Công thức gốc: uniqueOrderCode = UnixTimestamp * 100000 + order.Id
            // Bóc ngược:    orderId = uniqueOrderCode % 100000
            int orderId = (int)(webhookData.orderCode % 100000L);
            logger.LogInformation("[payOS Webhook] Nhận orderCode: {UniqueCode} → bóc tách orderId DB: {OrderId}", webhookData.orderCode, orderId);

            var order = await context.Orders.FindAsync(orderId);

            if (order != null && order.PaymentStatus != "Đã thanh toán thành công qua API Banking")
            {
                order.PaymentStatus = "Đã thanh toán thành công qua API Banking";
                order.Status = OrderStatus.Paid;
                order.UpdatedAt = DateTime.UtcNow;
                
                // 4. Kích nổ Email thứ 2 thông báo gạch nợ thành công dạng Fire-and-Forget
                string subjectPaid = "[EcommerceHue] Xác nhận đã thanh toán thành công đơn hàng #" + order.Id;
                string htmlPaidContent = "<h3>Cảm ơn bạn đã thanh toán thành công!</h3><p>Đơn hàng mã số #" + order.Id + " đã được chuyển sang trạng thái chờ giao nhận.</p>";
                
                _ = Task.Run(async () => {
                    try {
                        await _emailService.SendOrderEmailAsync(order.Email, subjectPaid, htmlPaidContent);
                    } catch (Exception exBg) {
                        Console.WriteLine("=== LỖI BẮN THƯ NỀN WEBHOOK: " + exBg.Message);
                    }
                });
                
                await context.SaveChangesAsync();
                logger.LogInformation("[payOS Webhook] Đã gạch nợ thành công đơn hàng #{OrderId} - {OrderCode}", order.Id, order.OrderCode);
            }
            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine("=== LỖI XỬ LÝ WEBHOOK PAYOS: " + ex.Message);
            return StatusCode(500);
        }
    }

    // =========================================================================
    // ACTION: Đặt hàng thành công – GET /Cart/OrderSuccess
    // =========================================================================

    /// <summary>
    /// Hiển thị thông tin hóa đơn và trạng thái đặt hàng thành công.
    /// </summary>
    /// <param name="orderCode">Mã đơn hàng</param>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> OrderSuccess(string? orderCode)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            orderCode = TempData["OrderCode"]?.ToString();
        }

        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return RedirectToAction("Index", "Home");
        }

        var order = await context.Orders
            .AsNoTracking()
            .Include(o => o.OrderDetails)
            .FirstOrDefaultAsync(o => o.OrderCode == orderCode);

        if (order is null)
        {
            return RedirectToAction("Index", "Home");
        }

        // Đảm bảo dữ liệu VietQR luôn sẵn sàng cho View (kể cả khi tải lại trang F5)
        if (TempData["QrUrl"] is null)
        {
            long amountToSend = (long)order.TotalAmount;
            string bankId = "970418";
            string bankName = "BIDV (Ngân hàng TMCP Đầu tư và Phát triển Việt Nam)";
            string accountNo = "8811843594";
            string accountName = "HO ANH TAI";
            string paymentContent = $"DH{order.Id}ECOMMERCEHUE";
            string qrUrl = $"https://img.vietqr.io/image/{bankId}-{accountNo}-compact2.png?amount={amountToSend}&addInfo={paymentContent}&accountName=HO%20ANH%20TAI";

            TempData["QrUrl"] = qrUrl;
            TempData["OrderId"] = order.Id;
            TempData["OrderCode"] = order.OrderCode;
            TempData["BankName"] = bankName;
            TempData["AccountNo"] = accountNo;
            TempData["AccountName"] = accountName;
            TempData["PaymentContent"] = paymentContent;
            TempData["TransferContent"] = paymentContent;
            TempData["TotalAmount"] = order.TotalAmount.ToString();
            TempData["PaymentMethod"] = "Chuyển khoản VietQR tự động";
        }

        return View(order);
    }

    // =========================================================================
    // HELPER: Biên soạn nội dung HTML email xác nhận đơn hàng
    // =========================================================================

    /// <summary>
    /// Tạo mẫu email HTML chuyên nghiệp xác nhận đơn hàng, liệt kê chi tiết đặc sản đã đặt,
    /// thông tin thanh toán VietQR động và địa chỉ giao hàng kèm thời gian dự kiến.
    /// </summary>
    private static string BuildOrderConfirmationEmailHtml(
        Order order,
        string checkoutUrl,
        string qrImageSrc)
    {
        var payUrl = !string.IsNullOrWhiteSpace(checkoutUrl) ? checkoutUrl : "#";
        var accountNumber = "8811843594";
        var accountName = "HO ANH TAI";
        var paymentContent = $"DH{order.Id} {order.OrderCode}";
        if (paymentContent.Length > 25)
        {
            paymentContent = paymentContent.Substring(0, 25);
        }

        var itemsHtml = new System.Text.StringBuilder();
        var index = 1;
        foreach (var item in order.OrderDetails)
        {
            var lineTotal = item.Quantity * item.UnitPrice;
            itemsHtml.Append($"""
                <tr style="border-bottom: 1px solid #f0e6d6;">
                    <td style="padding: 10px; text-align: center; color: #666;">{index++}</td>
                    <td style="padding: 10px; font-weight: 600; color: #333;">{System.Net.WebUtility.HtmlEncode(item.ProductName)}</td>
                    <td style="padding: 10px; text-align: center; color: #333;">{item.Quantity}</td>
                    <td style="padding: 10px; text-align: right; color: #555;">{item.UnitPrice:N0} ₫</td>
                    <td style="padding: 10px; text-align: right; font-weight: bold; color: #b71c1c;">{lineTotal:N0} ₫</td>
                </tr>
            """);
        }

        var orderDate = order.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        var statusHtml = order.Status == OrderStatus.Paid
            ? "<span style=\"color: #2e7d32; font-weight: bold;\">Đã thanh toán thành công qua API Banking</span>"
            : "<span style=\"color: #e65100; font-weight: bold;\">Chờ chuyển khoản VietQR</span>";

        return $"""
        <!DOCTYPE html>
        <html lang="vi">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>Xác nhận đơn hàng #{order.Id}</title>
        </head>
        <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f7f5f0; margin: 0; padding: 20px; line-height: 1.6; color: #333;">
            <div style="max-width: 650px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.06); border: 1px solid #e8dec8;">
                
                <!-- HEADER THƯƠNG HIỆU -->
                <div style="background: linear-gradient(135deg, #8b3012 0%, #5c200c 100%); padding: 30px 25px; text-align: center; color: #ffffff;">
                    <h1 style="margin: 0; font-size: 24px; font-weight: bold; letter-spacing: 0.5px;">ĐẶC SẢN CỐ ĐÔ HUẾ - ECOMMERCEHUE</h1>
                    <p style="margin: 6px 0 0 0; font-size: 14px; color: #f8dfaa;">Hương vị cung đình - Tinh hoa ẩm thực xứ Huế</p>
                </div>

                <!-- LỜI CHÀO & THÔNG BÁO -->
                <div style="padding: 25px 30px;">
                    <h2 style="color: #8b3012; font-size: 19px; margin-top: 0; margin-bottom: 12px;">Chào Quý khách {System.Net.WebUtility.HtmlEncode(order.CustomerName)},</h2>
                    <p style="margin: 0 0 16px 0; color: #555;">
                        Hệ thống cửa hàng <strong>EcommerceHue</strong> trân trọng cảm ơn bạn đã tin tưởng lựa chọn các món đặc sản truyền thống xứ Huế. Chúng tôi xin thông báo đơn hàng của bạn đã được tiếp nhận thành công vào hệ thống.
                    </p>

                    <!-- TÓM TẮT MÃ ĐƠN HÀNG -->
                    <div style="background-color: #fcf9f2; border-left: 4px solid #8b3012; padding: 14px 18px; border-radius: 4px; margin-bottom: 22px;">
                        <div style="font-size: 14px; margin-bottom: 4px;"><strong>Mã đơn hàng:</strong> <span style="color: #b71c1c; font-weight: bold;">#{order.Id}</span> ({order.OrderCode})</div>
                        <div style="font-size: 14px; margin-bottom: 4px;"><strong>Thời gian đặt:</strong> {orderDate}</div>
                        <div style="font-size: 14px;"><strong>Trạng thái:</strong> {statusHtml}</div>
                    </div>

                    <!-- BẢNG CHI TIẾT SẢN PHẨM -->
                    <h3 style="font-size: 16px; color: #333; margin: 20px 0 10px 0; border-bottom: 2px solid #8b3012; padding-bottom: 6px;">
                        📦 Danh sách đặc sản đã đặt
                    </h3>
                    <table style="width: 100%; border-collapse: collapse; font-size: 14px; margin-bottom: 20px;">
                        <thead>
                            <tr style="background-color: #f7ede2; color: #8b3012;">
                                <th style="padding: 10px; text-align: center; width: 40px;">STT</th>
                                <th style="padding: 10px; text-align: left;">Tên đặc sản</th>
                                <th style="padding: 10px; text-align: center; width: 70px;">SL</th>
                                <th style="padding: 10px; text-align: right; width: 100px;">Đơn giá</th>
                                <th style="padding: 10px; text-align: right; width: 110px;">Thành tiền</th>
                            </tr>
                        </thead>
                        <tbody>
                            {itemsHtml}
                        </tbody>
                        <tfoot>
                            <tr>
                                <td colspan="4" style="padding: 12px 10px; text-align: right; font-weight: 600; color: #555;">Phí vận chuyển:</td>
                                <td style="padding: 12px 10px; text-align: right; font-weight: bold; color: #2e7d32;">Miễn phí</td>
                            </tr>
                            <tr style="background-color: #fff9f0; border-top: 2px solid #e8dec8;">
                                <td colspan="4" style="padding: 14px 10px; text-align: right; font-size: 16px; font-weight: bold; color: #333;">Tổng tiền cần thanh toán:</td>
                                <td style="padding: 14px 10px; text-align: right; font-size: 18px; font-weight: bold; color: #b71c1c;">{order.TotalAmount:N0} ₫</td>
                            </tr>
                        </tfoot>
                    </table>

                    <!-- KHỐI HƯỚNG DẪN THANH TOÁN VIETQR -->
                    <div style="background: linear-gradient(180deg, #fffcf5 0%, #fff8eb 100%); border: 2px dashed #e0a96d; border-radius: 10px; padding: 20px; margin-bottom: 25px; text-align: center;">
                        <h3 style="color: #8b3012; margin: 0 0 10px 0; font-size: 17px;">
                            💳 Hướng Dẫn Thanh Toán Chuyển Khoản VietQR Tự Động
                        </h3>
                        
                        <!-- Vùng hiển thị trực diện mã VietQR động payOS và nút mở trang thanh toán -->
                        <div style="text-align: center; margin: 20px 0;">
                            <p style="font-weight: bold; color: #555;">Bạn có thể quét mã VietQR dưới đây để thanh toán nhanh:</p>
                            <img src="{qrImageSrc}" alt="Mã VietQR Thanh Toán" style="max-width: 250px; border: 1px solid #ddd; padding: 10px; background: #fff; border-radius: 8px;" />
                            <br/>
                            <a href="{payUrl}" target="_blank" style="display: inline-block; margin-top: 15px; padding: 10px 20px; background-color: #007bff; color: white; text-decoration: none; border-radius: 5px; font-weight: bold;">Mở trang thanh toán an toàn payOS</a>
                        </div>

                        <!-- Bảng chi tiết tài khoản -->
                        <div style="text-align: left; background: #ffffff; border-radius: 6px; padding: 12px 16px; border: 1px solid #ffe0b2; font-size: 13px; line-height: 1.8;">
                            <div><strong>Ngân hàng thụ hưởng:</strong> BIDV (Ngân hàng TMCP Đầu tư và Phát triển Việt Nam)</div>
                            <div><strong>Số tài khoản:</strong> <span style="font-size: 15px; font-weight: bold; color: #1565c0;">{accountNumber}</span></div>
                            <div><strong>Chủ tài khoản:</strong> <strong>{accountName}</strong></div>
                            <div><strong>Số tiền:</strong> <span style="font-size: 15px; font-weight: bold; color: #b71c1c;">{order.TotalAmount:N0} ₫</span></div>
                            <div><strong>Nội dung chuyển khoản:</strong> <span style="font-size: 15px; font-weight: bold; color: #b71c1c; font-family: monospace; background: #fff0f0; padding: 2px 6px; border-radius: 4px;">{paymentContent}</span></div>
                        </div>
                        <div style="font-size: 12px; color: #c62828; margin-top: 10px; font-style: italic;">
                            * Vui lòng ghi chính xác nội dung chuyển khoản <strong>{paymentContent}</strong> để hệ thống tự động gạch nợ đơn hàng cho bạn nhanh chóng nhất.
                        </div>
                    </div>

                    <!-- THÔNG TIN GIAO NHẬN HÀNG -->
                    <h3 style="font-size: 16px; color: #333; margin: 20px 0 10px 0; border-bottom: 2px solid #8b3012; padding-bottom: 6px;">
                        🚚 Thông tin giao nhận hàng
                    </h3>
                    <div style="background-color: #fafafa; border-radius: 8px; padding: 15px 18px; font-size: 14px; line-height: 1.8; border: 1px solid #eeeeee;">
                        <div><strong>Người nhận:</strong> {System.Net.WebUtility.HtmlEncode(order.CustomerName)}</div>
                        <div><strong>Số điện thoại:</strong> {System.Net.WebUtility.HtmlEncode(order.CustomerPhone)}</div>
                        <div><strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(order.CustomerEmail)}</div>
                        <div><strong>Địa chỉ giao hàng:</strong> {System.Net.WebUtility.HtmlEncode(order.ShippingAddress)} <em>(Phường/Xã mới sáp nhập)</em></div>
                        <div><strong>Thời gian dự kiến giao nhận:</strong> <span style="color: #2e7d32; font-weight: bold;">2 - 3 ngày làm việc</span> (Đóng gói tiêu chuẩn bảo quản hương vị đặc sản).</div>
                        {(string.IsNullOrWhiteSpace(order.Note) ? "" : $"<div><strong>Ghi chú:</strong> <em>{System.Net.WebUtility.HtmlEncode(order.Note)}</em></div>")}
                    </div>

                    <!-- LỜI CẢM ƠN & LIÊN HỆ -->
                    <div style="margin-top: 25px; font-size: 13px; color: #666; border-top: 1px solid #eee; padding-top: 15px;">
                        <p style="margin: 0 0 5px 0;">Mọi thắc mắc hoặc cần hỗ trợ gấp về đơn hàng, Quý khách vui lòng liên hệ:</p>
                        <p style="margin: 0;">Hotline / Zalo: <strong>0881 184 3594</strong> | Email: <strong>ecommercedacsanhue@gmail.com</strong></p>
                    </div>

                </div>

                <!-- FOOTER -->
                <div style="background-color: #f2ede4; padding: 15px 25px; text-align: center; font-size: 12px; color: #888;">
                    © {DateTime.UtcNow.Year} EcommerceHue - Đặc Sản Cố Đô Huế. Tất cả các quyền được bảo lưu.
                </div>

            </div>
        </body>
        </html>
        """;
    }

    // =========================================================================
    // API HELPER: Gọi API cổng vận chuyển GHN Sandbox & In nhãn dán hộp
    // =========================================================================

    /// <summary>
    /// Gửi thông tin đơn hàng sang cổng GHN Sandbox kiểm thử và sinh nhãn dán PDF.
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    [Route("Cart/CallGhnApi")]
    [Route("/Cart/CallGhnApi")]
    public async Task<IActionResult> CallGhnApi(int orderId)
    {
        if (orderId <= 0)
        {
            return BadRequest(new { success = false, message = "Mã đơn hàng không hợp lệ." });
        }

        var order = await context.Orders.FindAsync(orderId);
        if (order == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy đơn hàng." });
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            var payload = new
            {
                to_name = order.CustomerName,
                to_phone = order.CustomerPhone,
                to_address = order.ShippingAddress,
                weight = 200,
                length = 10,
                width = 10,
                height = 10,
                service_type_id = 2,
                payment_type_id = 1,
                required_note = "KHONGCHOXEMHANG"
            };

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://ghn.vn")
            {
                Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
            };

            request.Headers.Add("Token", "637170d5-942b-11ea-9821-0281a26fb5d4");
            request.Headers.Add("ShopId", "800");

            var response = await httpClient.SendAsync(request);
            string orderCodeGhn = string.Empty;

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(responseString);
                    if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                        dataEl.TryGetProperty("order_code", out var codeEl))
                    {
                        orderCodeGhn = codeEl.GetString() ?? string.Empty;
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            if (string.IsNullOrWhiteSpace(orderCodeGhn))
            {
                orderCodeGhn = $"GHN{DateTime.UtcNow:yyMMddHHmm}{order.Id:D3}";
            }

            // BƯỚC 2: Gọi API gen-token để lấy token in ấn chính thức
            string printToken = string.Empty;
            try
            {
                using var tokenRequest = new HttpRequestMessage(
                    HttpMethod.Post, 
                    "https://dev-online-gateway.ghn.vn/shiip/public-api/v2/a5/gen-token");

                tokenRequest.Headers.Add("Token", "637170d5-942b-11ea-9821-0281a26fb5d4");
                tokenRequest.Headers.Add("ShopId", "800");

                var tokenPayload = new { order_codes = new[] { orderCodeGhn } };
                tokenRequest.Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(tokenPayload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var tokenResponse = await httpClient.SendAsync(tokenRequest);
                if (tokenResponse.IsSuccessStatusCode)
                {
                    var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(tokenJson);
                    if (doc.RootElement.TryGetProperty("data", out var dataObj) &&
                        dataObj.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        dataObj.TryGetProperty("token", out var tokenProp))
                    {
                        printToken = tokenProp.GetString() ?? string.Empty;
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(printToken))
            {
                printToken = orderCodeGhn;
            }

            string finalPrintUrl = $"https://dev-online-gateway.ghn.vn/a5/public-api/printA5?token={printToken}";

            order.Note = string.IsNullOrWhiteSpace(order.Note)
                ? $"[GHN: {orderCodeGhn}]"
                : $"{order.Note} | [GHN: {orderCodeGhn}]";

            if (order.Status == OrderStatus.Paid || order.Status == OrderStatus.Confirmed)
            {
                order.Status = OrderStatus.Shipping;
            }
            order.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            logger.LogInformation("[GHN] Đã tạo vận đơn qua CartController cho đơn #{OrderId}: {OrderCodeGhn}, Print URL: {PrintUrl}", order.Id, orderCodeGhn, finalPrintUrl);

            return Json(new
            {
                success = true,
                message = "Tạo vận đơn và sinh nhãn in nhiệt GHN thành công!",
                orderCode = orderCodeGhn,
                finalPrintUrl = finalPrintUrl,
                printUrl = finalPrintUrl,
                linkPdf = finalPrintUrl,
                newStatus = (int)order.Status
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GHN] Lỗi gọi API GHN cho đơn #{OrderId}", order.Id);
            return StatusCode(500, new { success = false, message = "Lỗi kết nối cổng GHN: " + ex.Message });
        }
    }
}
