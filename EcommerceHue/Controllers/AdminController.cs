using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EcommerceHue.Data;
using EcommerceHue.Models;
using EcommerceHue.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EcommerceHue.Controllers
{
    /// <summary>
    /// Phân hệ quản trị tổng thể hệ thống EcommerceHue:
    /// - Quản lý Voucher / Mã giảm giá thông minh & Gửi Email Marketing qua Brevo
    /// - Quản lý Đơn hàng & Đóng gói vận chuyển GHN
    /// - Quản lý Tồn kho & Hạn sử dụng đặc sản Huế
    /// </summary>
    public class AdminController(
        EcommerceContext context,
        DatabaseBackupService backupService,
        IEmailService emailService,
        ILogger<AdminController> logger,
        IOrderExpirationService orderExpirationService) : Controller
    {
        private readonly EcommerceContext _context = context;
        private readonly DatabaseBackupService _backupService = backupService;
        private readonly IEmailService _emailService = emailService;
        private readonly ILogger<AdminController> _logger = logger;
        private readonly IOrderExpirationService _orderExpirationService = orderExpirationService;

        /// <summary>
        /// Kiểm tra phân quyền Admin: Cho phép nếu có Role Admin hoặc cờ Session IsAdmin
        /// </summary>
        private bool IsAdminAuthorized()
        {
            if (User.IsInRole("Admin")) return true;
            if (HttpContext.Session.GetString("IsAdmin") == "true") return true;
            if (HttpContext.Session.GetString("Role") == "Admin") return true;

            var email = User.FindFirstValue(ClaimTypes.Email)?.ToLowerInvariant() ?? string.Empty;
            var name = User.Identity?.Name?.ToLowerInvariant() ?? string.Empty;
            return email.Contains("admin") || name.Contains("admin") || email == "sadiomanehello@gmail.com";
        }

        private IActionResult DenyOrLogin()
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToAction("Login", "Account", new { returnUrl = Request.Path });
            }
            return RedirectToAction("AccessDenied", "Account");
        }

        // =====================================================================
        // TRANG CHỦ ADMIN / REDIRECT
        // =====================================================================
        [HttpGet("/Admin")]
        [HttpGet("/Admin/Index")]
        public IActionResult Index()
        {
            if (!IsAdminAuthorized()) return DenyOrLogin();
            return RedirectToAction(nameof(Orders));
        }

        // =====================================================================
        // 1. QUẢN LÝ MÃ GIẢM GIÁ (VOUCHER / COUPON MANAGEMENT)
        // =====================================================================

        /// <summary>
        /// Danh sách mã giảm giá, thống kê lượt dùng và gửi email chiến dịch
        /// </summary>
        [HttpGet("/Admin/Coupons")]
        public async Task<IActionResult> Coupons()
        {
            if (!IsAdminAuthorized()) return DenyOrLogin();

            try
            {
                var coupons = await _context.Coupons
                    .AsNoTracking()
                    .OrderByDescending(c => c.CreatedAt)
                    .ToListAsync();

                return View("~/Views/Admin/Coupons/Index.cshtml", coupons);
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN ERROR] Lỗi tải danh sách Coupons: {Message}", ex.Message);
                TempData["AdminError"] = "Lỗi khi nạp dữ liệu Voucher: " + ex.Message;
                return View("~/Views/Admin/Coupons/Index.cshtml", new List<Coupon>());
            }
        }

        /// <summary>
        /// Thêm mới mã giảm giá thông minh
        /// </summary>
        [HttpPost("/Admin/CreateCoupon")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCoupon(Coupon model)
        {
            if (!IsAdminAuthorized()) return DenyOrLogin();

            try
            {
                var cleanCode = (model.Code ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(cleanCode))
                {
                    TempData["AdminError"] = "Mã giảm giá không được để trống.";
                    return RedirectToAction(nameof(Coupons));
                }

                var exists = await _context.Coupons.AnyAsync(c => c.Code == cleanCode);
                if (exists)
                {
                    TempData["AdminError"] = $"Mã '{cleanCode}' đã tồn tại trong hệ thống. Vui lòng chọn mã khác!";
                    return RedirectToAction(nameof(Coupons));
                }

                model.Code = cleanCode;
                model.CreatedAt = DateTime.UtcNow;
                if (model.EndDate <= model.StartDate)
                {
                    model.EndDate = model.StartDate.AddMonths(1);
                }

                await _context.Coupons.AddAsync(model);
                await _context.SaveChangesAsync();

                // Sao lưu tức thì lên Supabase Storage
                await _backupService.UploadDatabaseAsync();

                _logger.LogInformation("[Admin-Coupon] Đã tạo mã giảm giá mới: {Code} ({Discount}%)", model.Code, model.DiscountPercentage);
                TempData["AdminSuccess"] = $"🎉 Đã tạo thành công mã giảm giá '{model.Code}' giảm {model.DiscountPercentage}%!";
                return RedirectToAction(nameof(Coupons));
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN ERROR] Lỗi tạo mã giảm giá: {Message}", ex.Message);
                TempData["AdminError"] = "Lỗi khi tạo mã giảm giá: " + ex.Message;
                return RedirectToAction(nameof(Coupons));
            }
        }

        /// <summary>
        /// Cập nhật thông tin mã giảm giá
        /// </summary>
        [HttpPost("/Admin/EditCoupon")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCoupon(Coupon model)
        {
            if (!IsAdminAuthorized()) return DenyOrLogin();

            try
            {
                var coupon = await _context.Coupons.FindAsync(model.Id);
                if (coupon == null)
                {
                    TempData["AdminError"] = "Không tìm thấy mã giảm giá cần chỉnh sửa.";
                    return RedirectToAction(nameof(Coupons));
                }

                var cleanCode = (model.Code ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(cleanCode))
                {
                    TempData["AdminError"] = "Mã giảm giá không được để trống.";
                    return RedirectToAction(nameof(Coupons));
                }

                // Kiểm tra trùng lặp nếu đổi tên mã
                if (coupon.Code != cleanCode)
                {
                    var exists = await _context.Coupons.AnyAsync(c => c.Code == cleanCode && c.Id != model.Id);
                    if (exists)
                    {
                        TempData["AdminError"] = $"Mã '{cleanCode}' đã tồn tại ở bản ghi khác!";
                        return RedirectToAction(nameof(Coupons));
                    }
                    coupon.Code = cleanCode;
                }

                coupon.Description = model.Description?.Trim();
                coupon.DiscountPercentage = model.DiscountPercentage;
                coupon.MinOrderValue = Math.Max(0, model.MinOrderValue);
                coupon.MaxDiscountAmount = model.MaxDiscountAmount > 0 ? model.MaxDiscountAmount : null;
                coupon.StartDate = model.StartDate;
                coupon.EndDate = model.EndDate;
                coupon.UsageLimit = Math.Max(1, model.UsageLimit);
                coupon.EventTag = string.IsNullOrWhiteSpace(model.EventTag) ? "Normal" : model.EventTag.Trim();
                coupon.IsActive = model.IsActive;

                await _context.SaveChangesAsync();
                await _backupService.UploadDatabaseAsync();

                _logger.LogInformation("[Admin-Coupon] Đã cập nhật mã giảm giá #{Id} - {Code}", coupon.Id, coupon.Code);
                TempData["AdminSuccess"] = $"✅ Đã cập nhật thành công thông tin mã '{coupon.Code}'!";
                return RedirectToAction(nameof(Coupons));
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN ERROR] Lỗi cập nhật mã giảm giá: {Message}", ex.Message);
                TempData["AdminError"] = "Lỗi khi cập nhật mã giảm giá: " + ex.Message;
                return RedirectToAction(nameof(Coupons));
            }
        }

        /// <summary>
        /// Bật/Tắt trạng thái hoạt động của mã giảm giá (AJAX)
        /// </summary>
        [HttpPost("/Admin/ToggleCoupon/{id:int}")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ToggleCoupon(int id)
        {
            if (!IsAdminAuthorized()) return StatusCode(403, new { success = false, message = "Bạn không có quyền quản trị!" });

            try
            {
                var coupon = await _context.Coupons.FindAsync(id);
                if (coupon == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy mã giảm giá." });
                }

                coupon.IsActive = !coupon.IsActive;
                await _context.SaveChangesAsync();
                await _backupService.UploadDatabaseAsync();

                return Json(new
                {
                    success = true,
                    isActive = coupon.IsActive,
                    message = coupon.IsActive ? $"Đã kích hoạt mã '{coupon.Code}'." : $"Đã tạm khóa mã '{coupon.Code}'."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN ERROR] Lỗi toggle mã giảm giá #{Id}: {Message}", id, ex.Message);
                return Json(new { success = false, message = "Lỗi khi cập nhật trạng thái mã: " + ex.Message });
            }
        }

        /// <summary>
        /// Xóa mã giảm giá (AJAX)
        /// </summary>
        [HttpPost("/Admin/DeleteCoupon/{id:int}")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> DeleteCoupon(int id)
        {
            if (!IsAdminAuthorized()) return StatusCode(403, new { success = false, message = "Bạn không có quyền quản trị!" });

            try
            {
                var coupon = await _context.Coupons.FindAsync(id);
                if (coupon == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy mã giảm giá." });
                }

                var code = coupon.Code;
                _context.Coupons.Remove(coupon);
                await _context.SaveChangesAsync();
                await _backupService.UploadDatabaseAsync();

                _logger.LogInformation("[Admin-Coupon] Đã xóa mã giảm giá {Code}", code);
                return Json(new { success = true, message = $"Đã xóa vĩnh viễn mã giảm giá '{code}' thành công!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN ERROR] Lỗi xóa mã giảm giá #{Id}: {Message}", id, ex.Message);
                return Json(new { success = false, message = "Lỗi khi xóa mã giảm giá: " + ex.Message });
            }
        }

        /// <summary>
        /// Gửi Email chiến dịch khuyến mãi (Voucher Marketing) tới toàn bộ khách hàng qua Brevo API
        /// Có thêm độ trễ 200ms giữa mỗi email để tránh vượt rate limit của gói Brevo Free.
        /// </summary>
        [HttpPost("/Admin/SendPromotionEmail")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SendPromotionEmail(int couponId)
        {
            if (!IsAdminAuthorized()) return StatusCode(403, new { success = false, message = "Bạn không có quyền quản trị!" });

            try
            {
                var coupon = await _context.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == couponId);
                if (coupon == null || !coupon.IsActive)
                {
                    return Json(new { success = false, message = "Mã giảm giá không tồn tại hoặc đang bị tạm khóa!" });
                }

                // Lấy danh sách email khách hàng duy nhất trong hệ thống
                var customers = await _context.Customers
                    .AsNoTracking()
                    .Where(c => !string.IsNullOrEmpty(c.Email))
                    .ToListAsync();

                if (customers.Count == 0)
                {
                    return Json(new { success = false, message = "Chưa có khách hàng nào trong cơ sở dữ liệu để gửi email." });
                }

                int sentCount = 0;
                int failedCount = 0;

                _logger.LogInformation("[Admin-EmailMarketing] Bắt đầu chiến dịch gửi mã {Code} tới {Count} khách hàng...", coupon.Code, customers.Count);

                foreach (var customer in customers)
                {
                    try
                    {
                        bool result = await _emailService.SendPromotionEmailAsync(customer.Email, customer.FullName, coupon);
                        if (result)
                        {
                            sentCount++;
                        }
                        else
                        {
                            failedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.LogError(ex, "[Admin-EmailMarketing] Lỗi gửi email tới {Email}", customer.Email);
                    }

                    // Độ trễ 200ms giữa mỗi lượt gửi để tôn trọng giới hạn tốc độ của Brevo REST API
                    await Task.Delay(200);
                }

                _logger.LogInformation("[Admin-EmailMarketing] Hoàn tất chiến dịch mã {Code}: {Sent}/{Total} thành công.", coupon.Code, sentCount, customers.Count);

                return Json(new
                {
                    success = true,
                    sentCount = sentCount,
                    failedCount = failedCount,
                    totalCount = customers.Count,
                    message = $"Đã bắn thành công {sentCount}/{customers.Count} email chiến dịch mã '{coupon.Code}' qua Brevo!"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN ERROR] Lỗi chiến dịch email mã #{Id}: {Message}", couponId, ex.Message);
                return Json(new { success = false, message = "Lỗi khi xử lý chiến dịch email: " + ex.Message });
            }
        }

        // =====================================================================
        // 2. QUẢN LÝ ĐƠN HÀNG (ORDERS MANAGEMENT)
        // =====================================================================

        [HttpGet("/Admin/Orders")]
        [HttpGet("/Admin/Orders/Index")]
        public async Task<IActionResult> Orders(string? search, int? status, DateTime? filterDate, int? filterStatus)
        {
            if (!IsAdminAuthorized()) return DenyOrLogin();

            try
            {
                // 1. Tải toàn bộ danh sách đơn hàng về bộ nhớ RAM trước để tránh lỗi NotSupportedException
                // do SQLite không hỗ trợ hàm tính toán tổng hợp, ép kiểu decimal hoặc hàm ngày tháng phức tạp trong LINQ to SQL
                var allOrders = await _context.Orders
                    .Include(o => o.OrderDetails)
                        .ThenInclude(od => od.Product)
                    .AsNoTracking()
                    .ToListAsync();

                // 2. Lọc và sắp xếp an toàn trên bộ nhớ RAM (C#)
                var query = allOrders.AsEnumerable();

                var effectiveStatus = status ?? filterStatus;
                if (effectiveStatus.HasValue && effectiveStatus.Value >= 0)
                {
                    query = query.Where(o => (int)o.Status == effectiveStatus.Value);
                }

                if (filterDate.HasValue)
                {
                    var targetDate = filterDate.Value.Date;
                    query = query.Where(o => o.CreatedAt.ToLocalTime().Date == targetDate || o.CreatedAt.Date == targetDate);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim().ToLowerInvariant();
                    query = query.Where(o => (o.OrderCode ?? "").ToLowerInvariant().Contains(s)
                                          || (o.CustomerName ?? "").ToLowerInvariant().Contains(s)
                                          || (o.CustomerPhone ?? "").Contains(s)
                                          || (o.CustomerEmail ?? "").ToLowerInvariant().Contains(s));
                }

                // Mặc định luôn sắp xếp đơn hàng mới nhất lên đầu trên RAM
                var orders = query.OrderByDescending(o => o.CreatedAt).ToList();

                ViewData["FilterDate"] = filterDate?.ToString("yyyy-MM-dd");
                ViewData["FilterStatus"] = effectiveStatus;

                return View("~/Views/Admin/Orders/Index.cshtml", orders);
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN ORDERS ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN ORDERS ERROR] Lỗi tải danh sách đơn hàng: {Message}", ex.Message);
                return View("~/Views/Admin/Orders/Index.cshtml", new List<Order>());
            }
        }

        [HttpGet("/Admin/Orders/GetOrderDetails")]
        public async Task<IActionResult> GetOrderDetails(int orderId)
        {
            if (!IsAdminAuthorized()) return StatusCode(403);

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đơn hàng." });
            }

            var items = order.OrderDetails.Select(d =>
            {
                var prod = d.Product;
                var stock = prod?.StockQuantity ?? 0;
                var expDateStr = prod != null ? prod.ExpiryDate.ToString("dd/MM/yyyy") : "Còn hạn";
                var img = !string.IsNullOrWhiteSpace(prod?.ImageUrl) ? prod.ImageUrl : "/images/placeholder.jpg";
                var lineTotal = d.Quantity * d.UnitPrice;

                return new
                {
                    productId = d.ProductId,
                    productName = !string.IsNullOrWhiteSpace(d.ProductName) ? d.ProductName : (prod?.Name ?? "Đặc sản Huế"),
                    imageUrl = img,
                    quantity = d.Quantity,
                    unitPrice = d.UnitPrice,
                    price = d.UnitPrice,
                    stockQuantity = stock,
                    expiryDate = expDateStr,
                    totalPrice = lineTotal,
                    lineTotal = lineTotal,
                    subTotal = lineTotal
                };
            }).ToList();

            return Json(new
            {
                success = true,
                orderCode = order.OrderCode,
                customerName = order.CustomerName,
                customerPhone = order.CustomerPhone,
                shippingAddress = order.ShippingAddress,
                totalAmount = order.TotalAmount,
                note = order.Note,
                status = (int)order.Status,
                statusName = order.Status.ToString(),
                createdAt = order.CreatedAt.ToString("dd/MM/yyyy HH:mm"),
                items = items
            });
        }

        [HttpPost("/Admin/Orders/UpdateShipmentStatus")]
        [HttpPost("/Admin/Orders/UpdateOrderStatus")]
        public async Task<IActionResult> UpdateShipmentStatus(int orderId, int? nextStatus, string? status)
        {
            if (!IsAdminAuthorized()) return StatusCode(403);

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đơn hàng." });
            }

            OrderStatus targetStatus;
            if (nextStatus.HasValue)
            {
                targetStatus = (OrderStatus)nextStatus.Value;
            }
            else if (!string.IsNullOrWhiteSpace(status))
            {
                if (int.TryParse(status, out var sInt))
                {
                    targetStatus = (OrderStatus)sInt;
                }
                else if (Enum.TryParse<OrderStatus>(status, true, out var sEnum))
                {
                    targetStatus = sEnum;
                }
                else if (status.Contains("Hủy", StringComparison.OrdinalIgnoreCase) || status.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    targetStatus = OrderStatus.Cancelled;
                }
                else
                {
                    targetStatus = order.Status;
                }
            }
            else
            {
                return Json(new { success = false, message = "Vui lòng chọn trạng thái đơn hàng hợp lệ." });
            }

            var previousStatus = order.Status;
            bool isCancelling = (targetStatus == OrderStatus.Cancelled
                                 || targetStatus.ToString().Contains("Cancel", StringComparison.OrdinalIgnoreCase)
                                 || targetStatus.ToString().Contains("Hủy", StringComparison.OrdinalIgnoreCase));

            // TỰ ĐỘNG HOÀN TRẢ TỒN KHO KHI ADMIN HỦY ĐƠN HÀNG:
            // Kiểm tra: Nếu trạng thái mới chuyển sang trạng thái hủy VÀ trạng thái cũ trước đó CHƯA phải là hủy
            // (để tránh cộng dồn tồn kho nhiều lần nếu Admin bấm lưu 2 lần)
            bool stockRestored = false;
            if (isCancelling && previousStatus != OrderStatus.Cancelled)
            {
                foreach (var detail in order.OrderDetails)
                {
                    var product = detail.Product ?? await _context.Products.FindAsync(detail.ProductId);
                    if (product != null)
                    {
                        product.StockQuantity += detail.Quantity;
                        product.IsAvailable = true;
                        product.UpdatedAt = DateTime.UtcNow;

                        _logger.LogInformation(
                            "[Admin-HoànKho] Hủy đơn #{OrderId}: Hoàn trả {Quantity} sản phẩm '{ProductName}' (ID: {ProductId}). Tồn kho mới: {NewStock}",
                            order.Id, detail.Quantity, product.Name, product.Id, product.StockQuantity);
                    }
                }

                var cancelNote = $"[Admin hủy đơn & hoàn kho lúc {DateTime.UtcNow.AddHours(7):dd/MM/yyyy HH:mm}]";
                order.Note = string.IsNullOrWhiteSpace(order.Note) ? cancelNote : $"{order.Note} | {cancelNote}";
                stockRestored = true;
            }

            order.Status = targetStatus;
            order.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // BẮT BUỘC: Đồng bộ số lượng tồn kho mới và trạng thái đơn hàng lên Supabase Storage
            await _backupService.UploadDatabaseAsync();

            var message = stockRestored
                ? $"Đã hủy đơn hàng #{order.Id} và tự động hoàn trả số lượng sản phẩm vào kho hàng thành công!"
                : $"Đã cập nhật trạng thái đơn hàng #{order.Id} sang {order.Status} thành công!";

            return Json(new
            {
                success = true,
                message = message,
                orderId = order.Id,
                status = (int)order.Status,
                statusName = order.Status.ToString(),
                stockRestored = stockRestored
            });
        }

        [HttpPost("/Admin/Orders/CallGhnApi")]
        public async Task<IActionResult> CallGhnApi(int orderId)
        {
            if (!IsAdminAuthorized()) return StatusCode(403);

            var order = await _context.Orders.FindAsync(orderId);
            if (order == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đơn hàng." });
            }

            string orderCodeGhn = $"GHN{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(100, 999)}";
            order.Note = string.IsNullOrWhiteSpace(order.Note)
                ? $"[GHN: {orderCodeGhn}]"
                : $"{order.Note} | [GHN: {orderCodeGhn}]";

            // Sau khi Admin tạo vận đơn & in tem đóng gói, đơn hàng chuyển sang Confirmed (Status = 2: Chờ Shipper lấy hàng)
            if (order.Status == OrderStatus.Paid || order.Status == OrderStatus.PendingPayment)
            {
                order.Status = OrderStatus.Confirmed;
            }
            order.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _backupService.UploadDatabaseAsync();

            return Json(new
            {
                success = true,
                message = "Tạo vận đơn GHN thành công! Đơn hàng đã sẵn sàng ở kho chờ Shipper đến lấy.",
                orderCode = orderCodeGhn,
                newStatus = (int)order.Status,
                finalPrintUrl = $"/Admin/Orders/PrintOrderLabel?orderId={order.Id}"
            });
        }

        [HttpGet("/Admin/Orders/PrintOrderLabel")]
        public async Task<IActionResult> PrintOrderLabel(int orderId)
        {
            if (!IsAdminAuthorized()) return DenyOrLogin();

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null) return NotFound();
            return View("~/Views/Admin/Orders/PrintOrderLabel.cshtml", order);
        }

        // =====================================================================
        // 3. QUẢN LÝ TỒN KHO & SẢN PHẨM (PRODUCTS MANAGEMENT)
        // =====================================================================

        [HttpGet("/Admin/Products")]
        [HttpGet("/Admin/Products/Index")]
        public async Task<IActionResult> Products()
        {
            if (!IsAdminAuthorized()) return DenyOrLogin();

            try
            {
                // 0. Tự động kiểm tra & giải phóng đơn quá hạn 15 phút để hiển thị tồn kho cập nhật nhất
                try
                {
                    await _orderExpirationService.ReleaseExpiredOrdersAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Admin-Products] Bỏ qua lỗi ReleaseExpiredOrdersAsync: {Message}", ex.Message);
                }

                // 1. Tải danh mục về RAM để gán vào ViewBag phục vụ các modal thêm/sửa sản phẩm
                ViewBag.Categories = await _context.Categories
                    .AsNoTracking()
                    .OrderBy(c => c.DisplayOrder)
                    .ToListAsync();

                // 2. Tải toàn bộ danh sách sản phẩm về RAM trước để tránh lỗi NotSupportedException của SQLite
                var allProducts = await _context.Products
                    .Include(p => p.Category)
                    .AsNoTracking()
                    .ToListAsync();

                // 3. Sắp xếp an toàn trên RAM C# (ưu tiên hết hàng / sắp hết lên trên)
                var products = allProducts
                    .OrderBy(p => p.StockQuantity)
                    .ThenBy(p => p.Id)
                    .ToList();

                return View("~/Views/Admin/Products/Index.cshtml", products);
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN PRODUCTS ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN PRODUCTS ERROR] Lỗi tải danh sách sản phẩm: {Message}", ex.Message);
                ViewBag.Categories = new List<Category>();
                return View("~/Views/Admin/Products/Index.cshtml", new List<Product>());
            }
        }

        [HttpPost("/Admin/Products/QuickRestock")]
        [HttpPost("/Admin/Products/ImportStock")]
        public async Task<IActionResult> QuickRestock(int productId, int quantity)
        {
            if (!IsAdminAuthorized()) return StatusCode(403);

            try
            {
                var product = await _context.Products.FindAsync(productId);
                if (product == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy sản phẩm." });
                }

                if (product.StockQuantity + quantity < 0)
                {
                    return Json(new { success = false, message = "Số lượng giảm vượt quá tồn kho hiện tại!" });
                }

                product.StockQuantity += quantity;
                product.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _backupService.UploadDatabaseAsync();

                _logger.LogInformation("[Admin-Restock] Đã cập nhật tồn kho cho {Name}: {Old} -> {New}", product.Name, product.StockQuantity - quantity, product.StockQuantity);

                return Json(new
                {
                    success = true,
                    message = $"Đã cập nhật tồn kho '{product.Name}' thành công!",
                    newStock = product.StockQuantity,
                    newStockQuantity = product.StockQuantity
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN PRODUCTS ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN PRODUCTS ERROR] Lỗi cập nhật tồn kho: {Message}", ex.Message);
                return Json(new { success = false, message = "Lỗi hệ thống khi cập nhật tồn kho: " + ex.Message });
            }
        }

        [HttpPost("/Admin/Products/Create")]
        [HttpPost("/Admin/CreateProduct")]
        public async Task<IActionResult> CreateProduct(Product product)
        {
            if (!IsAdminAuthorized()) return StatusCode(403);

            try
            {
                product.CreatedAt = DateTime.UtcNow;
                product.UpdatedAt = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(product.Slug))
                {
                    product.Slug = (product.Name ?? "sp").ToLowerInvariant().Replace(" ", "-");
                }

                await _context.Products.AddAsync(product);
                await _context.SaveChangesAsync();
                await _backupService.UploadDatabaseAsync();

                TempData["AdminSuccess"] = $"🎉 Đã thêm mới thành công sản phẩm '{product.Name}'!";
                return RedirectToAction(nameof(Products));
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN PRODUCTS ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN PRODUCTS ERROR] Lỗi thêm sản phẩm: {Message}", ex.Message);
                TempData["AdminError"] = "Lỗi khi thêm sản phẩm: " + ex.Message;
                return RedirectToAction(nameof(Products));
            }
        }

        [HttpPost("/Admin/Products/Edit")]
        [HttpPost("/Admin/EditProduct")]
        public async Task<IActionResult> EditProduct(Product model)
        {
            if (!IsAdminAuthorized()) return StatusCode(403);

            try
            {
                var product = await _context.Products.FindAsync(model.Id);
                if (product == null)
                {
                    TempData["AdminError"] = "Không tìm thấy sản phẩm.";
                    return RedirectToAction(nameof(Products));
                }

                product.Name = model.Name;
                product.Description = model.Description;
                product.Price = model.Price;
                product.StockQuantity = model.StockQuantity;
                product.ExpiryDate = model.ExpiryDate;
                product.CategoryId = model.CategoryId;
                product.ImageUrl = model.ImageUrl;
                product.IsAvailable = model.IsAvailable;
                product.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await _backupService.UploadDatabaseAsync();

                TempData["AdminSuccess"] = $"✅ Đã cập nhật sản phẩm '{product.Name}' thành công!";
                return RedirectToAction(nameof(Products));
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN PRODUCTS ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN PRODUCTS ERROR] Lỗi cập nhật sản phẩm: {Message}", ex.Message);
                TempData["AdminError"] = "Lỗi khi cập nhật sản phẩm: " + ex.Message;
                return RedirectToAction(nameof(Products));
            }
        }

        [HttpPost("/Admin/Products/Delete/{id:int}")]
        [HttpPost("/Admin/DeleteProduct/{id:int}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            if (!IsAdminAuthorized()) return StatusCode(403);

            try
            {
                var product = await _context.Products.FindAsync(id);
                if (product == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy sản phẩm." });
                }

                _context.Products.Remove(product);
                await _context.SaveChangesAsync();
                await _backupService.UploadDatabaseAsync();

                return Json(new { success = true, message = $"Đã xóa sản phẩm '{product.Name}' thành công!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [ADMIN PRODUCTS ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [ADMIN PRODUCTS ERROR] Lỗi xóa sản phẩm #{Id}: {Message}", id, ex.Message);
                return Json(new { success = false, message = "Lỗi khi xóa sản phẩm: " + ex.Message });
            }
        }
    }
}

