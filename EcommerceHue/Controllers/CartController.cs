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
public class CartController(
    EcommerceContext context,
    IConfiguration configuration,
    ILogger<CartController> logger) : Controller
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
        var cart = GetCart();

        // 1. Kiểm tra giỏ hàng có rỗng không
        if (cart.Count == 0)
        {
            TempData["CartWarning"] = "⚠️ Giỏ hàng đang trống. Vui lòng thêm sản phẩm trước khi đặt hàng.";
            return RedirectToAction(nameof(Index));
        }

        // 2. Kiểm tra tính hợp lệ của dữ liệu đầu vào
        if (!ModelState.IsValid)
        {
            model.CartItems = cart;
            model.SubTotal = cart.Sum(i => i.Total);
            return View("Checkout", model);
        }

        // 3. Đọc mã giảm giá và số tiền giảm từ Session (nếu có)
        var discountAmountStr = HttpContext.Session.GetString("DiscountAmount");
        decimal.TryParse(discountAmountStr, out var discountAmount);

        var subTotal = cart.Sum(i => i.Total);
        var totalAmount = Math.Max(0, subTotal - discountAmount);

        // 4. Sinh mã đơn hàng duy nhất theo định dạng HUE-yyyyMMddHHmmss-XXX
        var orderCode = $"HUE-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";

        // 5. Khởi tạo đối tượng Order với trạng thái ban đầu là PendingPayment (Chờ thanh toán VnPay)
        var order = new Order
        {
            OrderCode       = orderCode,
            CustomerName    = model.CustomerName.Trim(),
            CustomerEmail   = model.CustomerEmail.Trim(),
            CustomerPhone   = model.CustomerPhone.Trim(),
            ShippingAddress = model.ShippingAddress.Trim(),
            Note            = model.Note?.Trim(),
            SubTotal        = subTotal,
            ShippingFee     = 0,
            TotalAmount     = totalAmount,
            Status          = OrderStatus.PendingPayment, // Chờ thanh toán
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

        // 6. Lưu đơn hàng vào cơ sở dữ liệu để lấy OrderId
        await context.Orders.AddAsync(order);
        await context.SaveChangesAsync();

        // Xử lý theo phương thức thanh toán khách hàng lựa chọn
        if (model.PaymentMethod == "BankTransfer")
        {
            // Xóa sạch giỏ hàng và session
            HttpContext.Session.Remove(CartSessionKey);
            HttpContext.Session.Remove("CouponCode");
            HttpContext.Session.Remove("DiscountAmount");
            HttpContext.Session.Remove("DiscountPercentage");

            TempData["PaymentMethod"] = "Chuyển khoản ngân hàng";
            logger.LogInformation("[Order] Đặt hàng thành công bằng hình thức Chuyển khoản ngân hàng: {OrderCode}", order.OrderCode);
            return RedirectToAction(nameof(OrderSuccess), new { orderCode = order.OrderCode });
        }

        if (model.PaymentMethod == "MoMo")
        {
            // Xóa sạch giỏ hàng và session
            HttpContext.Session.Remove(CartSessionKey);
            HttpContext.Session.Remove("CouponCode");
            HttpContext.Session.Remove("DiscountAmount");
            HttpContext.Session.Remove("DiscountPercentage");

            TempData["PaymentMethod"] = "Ví MoMo";
            logger.LogInformation("[Order] Đặt hàng thành công bằng hình thức Quét mã MoMo: {OrderCode}", order.OrderCode);
            return RedirectToAction(nameof(OrderSuccess), new { orderCode = order.OrderCode });
        }

        // Mặc định hoặc chọn "VnPay": Chuyển hướng sang cổng thanh toán VnPay Sandbox
        // 7. Đọc cấu hình VnPay từ IConfiguration theo quy chuẩn 02-security-vnpay.md
        var vnpTmnCode = configuration["VnPay:TmnCode"];
        var vnpHashSecret = configuration["VnPay:HashSecret"];
        var vnpBaseUrl = configuration["VnPay:BaseUrl"] ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
        var vnpReturnUrl = configuration["VnPay:ReturnUrl"] ?? "http://localhost:5131/Cart/VnPayReturn";

        // Fallback sang thông tin test sandbox thông dụng của VnPay nếu chưa cấu hình
        if (string.IsNullOrWhiteSpace(vnpTmnCode))
        {
            vnpTmnCode = "CGXZLS0Z";
        }
        if (string.IsNullOrWhiteSpace(vnpHashSecret))
        {
            vnpHashSecret = "XNBCJFAKAZQSGUTUYACICJUBWFLRUTIH";
        }

        // 8. Khởi tạo VnPayLibrary và nạp các tham số bắt buộc gửi sang VnPay
        var vnpay = new VnPayLibrary();

        vnpay.AddRequestData("vnp_Version", "2.1.0");
        vnpay.AddRequestData("vnp_Command", "pay");
        vnpay.AddRequestData("vnp_TmnCode", vnpTmnCode);
        // Số tiền thanh toán sau giảm giá x 100 theo quy chuẩn VnPay
        vnpay.AddRequestData("vnp_Amount", ((long)(totalAmount * 100)).ToString());
        vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
        vnpay.AddRequestData("vnp_CurrCode", "VND");
        vnpay.AddRequestData("vnp_IpAddr", VnPayLibrary.GetIpAddress(HttpContext));
        vnpay.AddRequestData("vnp_Locale", "vn");
        vnpay.AddRequestData("vnp_OrderInfo", $"Thanh toan don hang {order.OrderCode} tai Dac San Hue");
        vnpay.AddRequestData("vnp_OrderType", "other");
        vnpay.AddRequestData("vnp_ReturnUrl", vnpReturnUrl);
        vnpay.AddRequestData("vnp_TxnRef", order.OrderCode);

        // 9. Sinh chuỗi URL thanh toán chứa chữ ký bảo mật HMACSHA512
        var paymentUrl = vnpay.CreateRequestUrl(vnpBaseUrl, vnpHashSecret);

        logger.LogInformation("[VnPay] Tạo yêu cầu thanh toán thành công cho đơn hàng: {OrderCode}, Tổng tiền: {Amount} VND",
            order.OrderCode, totalAmount);

        // 10. Chuyển hướng người dùng sang trang nhập thẻ test VnPay Sandbox
        return Redirect(paymentUrl);
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
        var vnpay = new VnPayLibrary();

        // 1. Đọc toàn bộ tham số do VnPay gửi về qua Query String
        foreach (var (key, value) in Request.Query)
        {
            if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
            {
                vnpay.AddResponseData(key, value.ToString());
            }
        }

        var vnpTxnRef         = vnpay.GetResponseData("vnp_TxnRef");
        var vnpTransactionNo  = vnpay.GetResponseData("vnp_TransactionNo");
        var vnpResponseCode   = vnpay.GetResponseData("vnp_ResponseCode");
        var vnpSecureHash     = Request.Query["vnp_SecureHash"].ToString();
        var vnpBankCode       = vnpay.GetResponseData("vnp_BankCode");
        var vnpCardType       = vnpay.GetResponseData("vnp_CardType");
        var vnpPayDate        = vnpay.GetResponseData("vnp_PayDate");
        var vnpOrderInfo      = vnpay.GetResponseData("vnp_OrderInfo");
        var vnpAmountStr      = vnpay.GetResponseData("vnp_Amount");
        long.TryParse(vnpAmountStr, out var vnpAmount);

        // 2. Lấy khóa bí mật HashSecret từ IConfiguration
        var vnpHashSecret = configuration["VnPay:HashSecret"];
        if (string.IsNullOrWhiteSpace(vnpHashSecret))
        {
            vnpHashSecret = "XNBCJFAKAZQSGUTUYACICJUBWFLRUTIH";
        }

        // 3. Thực hiện kiểm tra chữ ký an toàn (Checksum Validation)
        var isSignatureValid = vnpay.ValidateSignature(vnpSecureHash, vnpHashSecret);

        // 4. Tìm đơn hàng tương ứng trong CSDL
        var order = await context.Orders
            .FirstOrDefaultAsync(o => o.OrderCode == vnpTxnRef);

        // 5. Khởi tạo bản ghi PaymentTransaction để lưu vết kiểm toán
        var transaction = new PaymentTransaction
        {
            OrderId            = order?.Id ?? 0,
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
            transaction.AuditNote = $"CẢNH BÁO BẢO MẬT: Chữ ký HMACSHA512 không khớp từ IP {VnPayLibrary.GetIpAddress(HttpContext)}! Nghi vấn giả mạo dữ liệu.";

            logger.LogCritical("[SECURITY AUDIT] Checksum validation thất bại cho đơn hàng {OrderCode}! Hash nhận được: {Hash}, IP: {IP}",
                vnpTxnRef, vnpSecureHash, VnPayLibrary.GetIpAddress(HttpContext));

            await context.PaymentTransactions.AddAsync(transaction);
            await context.SaveChangesAsync();

            TempData["CheckoutError"] = "⚠️ Chữ ký bảo mật không hợp lệ (Nghi vấn giả mạo dữ liệu). Giao dịch bị từ chối.";
            return RedirectToAction(nameof(Checkout));
        }

        // --- TRƯỜNG HỢP 2: CHỮ KÝ HỢP LỆ VÀ GIAO DỊCH THÀNH CÔNG (vnp_ResponseCode == "00") ---
        if (vnpResponseCode == "00")
        {
            transaction.Status    = PaymentStatus.Success;
            transaction.AuditNote = "Giao dịch thanh toán trực tuyến qua VnPay thành công.";

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
            logger.LogInformation("[VnPay] Giao dịch thành công cho đơn hàng: {OrderCode}, Mã GD VnPay: {TransNo}",
                vnpTxnRef, vnpTransactionNo);

            return RedirectToAction(nameof(OrderSuccess), new { orderCode = vnpTxnRef });
        }

        // --- TRƯỜNG HỢP 3: KHÁCH HÀNG HỦY GIAO DỊCH HOẶC THANH TOÁN THẤT BẠI ---
        transaction.Status    = PaymentStatus.Failed;
        transaction.AuditNote = $"Thanh toán thất bại hoặc khách hủy với mã phản hồi VnPay: {vnpResponseCode}";

        await context.PaymentTransactions.AddAsync(transaction);
        await context.SaveChangesAsync();

        logger.LogWarning("[VnPay] Thanh toán không thành công. Mã lỗi: {Code} cho đơn hàng: {OrderCode}",
            vnpResponseCode, vnpTxnRef);

        TempData["CheckoutError"] = $"Giao dịch qua VnPay không thành công (Mã lỗi: {vnpResponseCode}). Vui lòng thử lại.";
        return RedirectToAction(nameof(Checkout));
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
    public async Task<IActionResult> OrderSuccess(string orderCode)
    {
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

        return View(order);
    }
}
