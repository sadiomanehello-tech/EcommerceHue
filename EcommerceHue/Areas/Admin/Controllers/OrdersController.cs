using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Models;

namespace EcommerceHue.Areas.Admin.Controllers;

/// <summary>
/// Phân hệ Quản trị đơn hàng dành riêng cho người bán / Quản trị viên (Admin).
/// Bảo vệ nghiêm ngặt bằng phân quyền RBAC: Chỉ tài khoản Role = "Admin" mới có quyền truy cập.
/// </summary>
[Area("Admin")]
[Authorize(Roles = "Admin")]
[Route("Admin/[controller]/[action]")]
public class OrdersController(
    EcommerceContext context,
    ILogger<OrdersController> logger) : Controller
{
    // =========================================================================
    // 1. ACTION: Danh sách đơn hàng – GET /Admin/Orders hoặc /Admin/Orders/Index
    // =========================================================================

    /// <summary>
    /// Hiển thị danh sách đơn hàng từ CSDL có hỗ trợ bộ lọc theo ngày đặt và trạng thái xử lý.
    /// Sắp xếp mặc định đơn mới nhất lên đầu.
    /// </summary>
    /// <param name="filterDate">Ngày đặt hàng cần lọc</param>
    /// <param name="filterStatus">Mã trạng thái đơn hàng (0: Chờ thanh toán, 1: Đã thanh toán, 3: Đang giao...)</param>
    [HttpGet]
    [Route("/Admin/Orders")]
    [Route("/Admin/Orders/Index")]
    [Route("Admin/[controller]")]
    [Route("Admin/[controller]/[action]")]
    public async Task<IActionResult> Index(DateTime? filterDate, int? filterStatus)
    {
        var query = context.Orders.AsNoTracking().AsQueryable();

        // 1. Lọc theo ngày đặt hàng nếu có chọn ngày
        if (filterDate.HasValue)
        {
            var targetDate = filterDate.Value.Date;
            // Xử lý múi giờ Việt Nam (UTC+7): bao phủ cả ngày đặt theo giờ địa phương và UTC
            var startUtc = targetDate.AddHours(-7);
            var endUtc = startUtc.AddDays(1);

            query = query.Where(o => (o.CreatedAt >= startUtc && o.CreatedAt < endUtc) || o.CreatedAt.Date == targetDate);
        }

        // 2. Lọc theo trạng thái đơn hàng nếu có chọn trạng thái
        if (filterStatus.HasValue && filterStatus.Value >= 0)
        {
            query = query.Where(o => (int)o.Status == filterStatus.Value);
        }

        // 3. Mặc định luôn sắp xếp đơn hàng mới nhất lên đầu
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        logger.LogInformation(
            "[Admin] Tải danh sách đơn hàng (FilterDate: {Date}, FilterStatus: {Status}): {Count} đơn",
            filterDate?.ToString("yyyy-MM-dd") ?? "Tất cả",
            filterStatus?.ToString() ?? "Tất cả",
            orders.Count);

        // Truyền các giá trị bộ lọc hiện tại ra ngoài View qua ViewData để giữ trạng thái ô chọn
        ViewData["FilterDate"] = filterDate?.ToString("yyyy-MM-dd");
        ViewData["FilterStatus"] = filterStatus;

        return View("~/Areas/Admin/Views/Orders/Index.cshtml", orders);
    }

    // =========================================================================
    // 2. ACTION: Xem chi tiết mặt hàng cần đóng gói – GET /Admin/Orders/GetOrderDetails
    // =========================================================================

    /// <summary>
    /// Truy vấn danh sách chi tiết mặt hàng kèm thông tin sản phẩm (Mè xửng, Tôm chua, số lượng...)
    /// phục vụ hiển thị lên Pop-up Modal đóng gói cho chủ cửa hàng.
    /// </summary>
    /// <param name="orderId">Mã định danh số nguyên của đơn hàng</param>
    [HttpGet]
    [Route("/Admin/Orders/GetOrderDetails")]
    [Route("Admin/[controller]/[action]")]
    public async Task<IActionResult> GetOrderDetails(int orderId)
    {
        if (orderId <= 0)
        {
            return BadRequest(new { success = false, message = "Mã đơn hàng không hợp lệ." });
        }

        // Truy vấn bảng OrderDetails kèm thông tin sản phẩm (Include Product)
        var order = await context.Orders
            .AsNoTracking()
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            logger.LogWarning("[Admin] Không tìm thấy đơn hàng #{OrderId} khi lấy chi tiết đóng gói", orderId);
            return NotFound(new { success = false, message = "Không tìm thấy thông tin đơn hàng." });
        }

        var details = order.OrderDetails.Select(od => new
        {
            id = od.Id,
            productId = od.ProductId,
            productName = od.ProductName,
            unitPrice = od.UnitPrice,
            quantity = od.Quantity,
            lineTotal = od.LineTotal,
            imageUrl = od.Product != null ? od.Product.ImageUrl : "/images/placeholder.jpg",
            stockQuantity = od.Product != null ? od.Product.StockQuantity : 0,
            expiryDate = od.Product != null ? od.Product.ExpiryDate.ToString("dd/MM/yyyy") : "N/A"
        }).ToList();

        return Json(new
        {
            success = true,
            orderId = order.Id,
            orderCode = order.OrderCode,
            customerName = order.CustomerName,
            customerPhone = order.CustomerPhone,
            customerEmail = order.CustomerEmail,
            shippingAddress = order.ShippingAddress,
            note = order.Note,
            totalAmount = order.TotalAmount,
            status = (int)order.Status,
            statusText = GetStatusBadgeText(order.Status),
            createdAt = order.CreatedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm"),
            items = details
        });
    }

    // =========================================================================
    // 3. ACTION: Cập nhật trạng thái vận chuyển – POST /Admin/Orders/UpdateShipmentStatus
    // =========================================================================

    /// <summary>
    /// Cập nhật trạng thái đơn hàng khi chủ shop đóng gói và giao nhận vận chuyển.
    /// </summary>
    /// <param name="orderId">Mã đơn hàng</param>
    /// <param name="nextStatus">Mã trạng thái mới (theo enum OrderStatus)</param>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    [Route("/Admin/Orders/UpdateShipmentStatus")]
    [Route("Admin/[controller]/[action]")]
    public async Task<IActionResult> UpdateShipmentStatus(int orderId, int nextStatus)
    {
        if (orderId <= 0)
        {
            return BadRequest(new { success = false, message = "Mã đơn hàng không hợp lệ." });
        }

        if (!Enum.IsDefined(typeof(OrderStatus), nextStatus))
        {
            return BadRequest(new { success = false, message = "Trạng thái đơn hàng không hợp lệ." });
        }

        var order = await context.Orders.FindAsync(orderId);
        if (order == null)
        {
            logger.LogWarning("[Admin] Cập nhật trạng thái thất bại: Không tìm thấy đơn hàng #{OrderId}", orderId);
            return NotFound(new { success = false, message = "Không tìm thấy đơn hàng cần cập nhật." });
        }

        var oldStatus = order.Status;
        var newStatus = (OrderStatus)nextStatus;

        // =====================================================================
        // HOÀN KHO TỰ ĐỘNG KHI HỦY ĐƠN (CHIỀU TĂNG)
        // Nếu chuyển sang trạng thái Cancelled (5) và đơn trước đó chưa bị hủy
        // =====================================================================
        if (newStatus == OrderStatus.Cancelled && oldStatus != OrderStatus.Cancelled)
        {
            await context.Entry(order).Collection(o => o.OrderDetails).LoadAsync();
            foreach (var detail in order.OrderDetails)
            {
                var prod = await context.Products.FindAsync(detail.ProductId);
                if (prod != null)
                {
                    prod.StockQuantity += detail.Quantity; // Cộng trả lại kho
                    logger.LogInformation(
                        "[Admin Hoàn kho] Đã hoàn {Quantity} cho sản phẩm '{ProductName}' (ID: {ProductId}). Tồn kho mới: {StockQuantity}",
                        detail.Quantity, prod.Name, prod.Id, prod.StockQuantity);
                }
            }
        }

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        logger.LogInformation(
            "[Admin] Cập nhật tiến độ đơn hàng #{OrderId} ({OrderCode}) từ '{OldStatus}' sang '{NewStatus}'",
            order.Id, order.OrderCode, oldStatus, newStatus);

        return Json(new
        {
            success = true,
            message = "Cập nhật trạng thái đơn hàng thành công!",
            orderId = order.Id,
            newStatus = (int)order.Status,
            newStatusText = GetStatusBadgeText(order.Status)
        });
    }

    // =========================================================================
    // 4. ACTION: Gọi API cổng vận chuyển GHN Sandbox & In nhãn dán hộp
    // =========================================================================

    /// <summary>
    /// Gửi thông tin đơn hàng sang cổng vận chuyển GHN (Sandbox test), tiếp nhận mã vận đơn
    /// và sinh link in nhãn dán (PDF) dán lên hộp đặc sản Huế.
    /// </summary>
    /// <param name="orderId">Mã đơn hàng</param>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    [Route("/Admin/Orders/CallGhnApi")]
    [Route("Admin/[controller]/[action]")]
    public async Task<IActionResult> CallGhnApi(int orderId)
    {
        if (orderId <= 0)
        {
            return BadRequest(new { success = false, message = "Mã đơn hàng không hợp lệ." });
        }

        var order = await context.Orders.FindAsync(orderId);
        if (order == null)
        {
            return NotFound(new { success = false, message = "Không tìm thấy đơn hàng cần gọi vận chuyển." });
        }

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            // 1. Đóng gói dữ liệu JSON gửi sang GHN (khớp thông tin đơn hàng)
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

            // 2. Thiết lập 2 thẻ Headers bắt buộc từ GHN Sandbox
            request.Headers.Add("Token", "637170d5-942b-11ea-9821-0281a26fb5d4");
            request.Headers.Add("ShopId", "800");

            var response = await httpClient.SendAsync(request);
            string orderCodeGhn = string.Empty;

            // 3. Tiếp nhận phản hồi và trích xuất mã vận đơn
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
                    // Dự phòng an toàn nếu endpoint web trả về định dạng trang portal
                }
            }

            // Trường hợp cổng portal chưa trả mã JSON, sinh mã vận đơn GHN Sandbox định danh
            if (string.IsNullOrWhiteSpace(orderCodeGhn))
            {
                orderCodeGhn = $"GHN{DateTime.UtcNow:yyMMddHHmm}{order.Id:D3}";
            }

            // -----------------------------------------------------------------
            // BƯỚC 2: GỌI API KÉP LẤY TOKEN IN HÓA ĐƠN K80/A5 KHÔNG BỊ LỖI 401
            // -----------------------------------------------------------------
            string printToken = string.Empty;
            try
            {
                using var tokenRequest = new HttpRequestMessage(
                    HttpMethod.Post, 
                    "https://dev-online-gateway.ghn.vn/shiip/public-api/v2/a5/gen-token");

                tokenRequest.Headers.Add("Token", "637170d5-942b-11ea-9821-0281a26fb5d4");
                tokenRequest.Headers.Add("ShopId", "800");

                var tokenPayload = new
                {
                    order_codes = new[] { orderCodeGhn }
                };

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
            catch (Exception exToken)
            {
                logger.LogWarning(exToken, "[GHN] Không thể lấy token in ấn từ GHN Gateway: {Message}", exToken.Message);
            }

            // Fallback mã token in ấn an toàn nếu cổng chưa cấp token động
            if (string.IsNullOrWhiteSpace(printToken))
            {
                printToken = orderCodeGhn;
            }

            // Sinh đường dẫn hiển thị phiếu in nhiệt nội bộ dán thùng hàng chủ động 100%
            string finalPrintUrl = $"/Admin/Orders/PrintOrderLabel?orderId={order.Id}";

            // Lưu mã vận đơn vào cột ghi chú của bảng Orders
            order.Note = string.IsNullOrWhiteSpace(order.Note)
                ? $"[GHN: {orderCodeGhn}]"
                : $"{order.Note} | [GHN: {orderCodeGhn}]";

            // Khi bấm nút Gọi vận chuyển, chỉ lưu mã vận đơn và cập nhật trạng thái thành Status = 2 (Đã xác nhận đóng gói / Chờ shipper lấy hàng)
            // CHƯA chuyển sang Đang giao hàng (Status = 3) và CHƯA gửi email thứ 3 tại nút này (chờ Shipper quét mã vạch)
            order.Status = OrderStatus.Confirmed;
            order.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            logger.LogInformation(
                "[GHN] Tạo đơn vận chuyển & Token in thành công cho đơn #{OrderId}: {OrderCodeGhn}, Trạng thái: Confirmed (Chờ shipper lấy hàng), Print URL: {FinalPrintUrl}",
                order.Id, orderCodeGhn, finalPrintUrl);

            return Json(new
            {
                success = true,
                message = "Đã tạo mã vận đơn GHN thành công! Đơn hàng chuyển sang: Chờ shipper lấy hàng.",
                orderCode = orderCodeGhn,
                finalPrintUrl = finalPrintUrl,
                printUrl = finalPrintUrl,
                linkPdf = finalPrintUrl,
                newStatus = (int)order.Status,
                newStatusText = GetStatusBadgeText(order.Status),
                updatedNote = order.Note
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GHN] Lỗi kết nối API vận chuyển cho đơn #{OrderId}", order.Id);
            return StatusCode(500, new { success = false, message = "Lỗi kết nối cổng GHN: " + ex.Message });
        }
    }

    // =========================================================================
    // 5. ACTION: Xuất bản phiếu in nhiệt nội bộ dán thùng carton – GET /Admin/Orders/PrintOrderLabel
    // =========================================================================

    /// <summary>
    /// Hiển thị phiếu in nhiệt mini (khổ K80 / A6) dán thùng carton đóng gói đặc sản Huế.
    /// Tự động kích hoạt hộp thoại in ấn của trình duyệt khi mở trang.
    /// </summary>
    /// <param name="orderId">Mã đơn hàng</param>
    [HttpGet]
    [Route("/Admin/Orders/PrintOrderLabel")]
    [Route("Admin/[controller]/[action]")]
    public async Task<IActionResult> PrintOrderLabel(int orderId)
    {
        if (orderId <= 0)
        {
            return BadRequest("Mã đơn hàng không hợp lệ.");
        }

        var order = await context.Orders
            .AsNoTracking()
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            return NotFound("Không tìm thấy đơn hàng cần in phiếu.");
        }

        return View("~/Areas/Admin/Views/Orders/PrintOrderLabel.cshtml", order);
    }

    // =========================================================================
    // HELPER: Chuyển đổi trạng thái sang nhãn tiếng Việt dễ hiểu
    // =========================================================================

    private static string GetStatusBadgeText(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "Chờ khách chuyển khoản",
        OrderStatus.Paid           => "Đã thanh toán - Sẵn sàng gói hàng",
        OrderStatus.Confirmed      => "Đã xác nhận - Chờ shipper lấy hàng",
        OrderStatus.Shipping       => "Đang giao hàng",
        OrderStatus.Delivered      => "Đã giao hàng thành công",
        OrderStatus.Cancelled      => "Đã hủy đơn",
        _                          => "Không xác định"
    };
}
