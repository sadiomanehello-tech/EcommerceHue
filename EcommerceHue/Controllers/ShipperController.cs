using System.Text.RegularExpressions;
using EcommerceHue.Data;
using EcommerceHue.Models;
using EcommerceHue.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcommerceHue.Controllers;

/// <summary>
/// Yêu cầu DTO nhận mã quét từ camera / ô nhập liệu Shipper
/// </summary>
public sealed class ScanBarcodeRequest
{
    public string? Barcode { get; set; }
}

/// <summary>
/// Phân hệ điều phối & quét nhận kiện hàng dành cho Shipper Giao Hàng Nhanh (GHN)
/// </summary>
public sealed class ShipperController(
    EcommerceContext context,
    IEmailService emailService,
    ILogger<ShipperController> logger) : Controller
{
    // =========================================================================
    // 1. ACTION: Giao diện ứng dụng Shipper quét mã bưu kiện – GET /Shipper/Scan
    // =========================================================================

    /// <summary>
    /// Hiển thị giao diện mobile app giả lập cho Shipper thực hiện quét barcode / QR code.
    /// </summary>
    [HttpGet]
    public IActionResult Scan()
    {
        return View();
    }

    // =========================================================================
    // 2. API: Tiếp nhận kết quả quét mã vạch – POST /Shipper/ProcessScan
    // =========================================================================

    /// <summary>
    /// Tiếp nhận mã vận đơn do camera quét được hoặc nhập tay.
    /// Cập nhật trạng thái đơn hàng sang 'Đang giao hàng' (Status = 3)
    /// và kích hoạt gửi Email hành trình vận chuyển thứ 3 tới khách hàng.
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ProcessScan([FromBody] ScanBarcodeRequest? jsonBody, [FromForm] string? barcode)
    {
        var rawCode = !string.IsNullOrWhiteSpace(jsonBody?.Barcode) ? jsonBody.Barcode : barcode;

        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return BadRequest(new
            {
                success = false,
                message = "Vui lòng nhập hoặc quét mã bưu kiện hợp lệ."
            });
        }

        var cleanBarcode = rawCode.Trim();

        logger.LogInformation("[Shipper] Nhận tín hiệu quét mã vận đơn: '{Barcode}'", cleanBarcode);

        // 1. Tìm đơn hàng theo mã vận đơn trong Note, theo OrderCode, hoặc theo ID
        var order = await context.Orders
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
            .FirstOrDefaultAsync(o =>
                (o.Note != null && o.Note.Contains(cleanBarcode)) ||
                o.OrderCode == cleanBarcode ||
                o.Id.ToString() == cleanBarcode);

        if (order == null)
        {
            logger.LogWarning("[Shipper] Không tìm thấy đơn hàng khớp với barcode: '{Barcode}'", cleanBarcode);
            return NotFound(new
            {
                success = false,
                message = $"Không tìm thấy đơn hàng nào có mã vận đơn hoặc mã đơn là '{cleanBarcode}'."
            });
        }

        // 2. Trích xuất mã vận đơn GHN chính xác để tạo link hành trình
        string trackingCode = cleanBarcode;
        if (!string.IsNullOrWhiteSpace(order.Note) && order.Note.Contains("GHN"))
        {
            var match = Regex.Match(order.Note, @"GHN[\w\d]+");
            if (match.Success)
            {
                trackingCode = match.Value;
            }
        }

        // 3. Chính thức chuyển trạng thái đơn hàng sang 'Đang giao hàng' (Status = 3)
        var oldStatus = order.Status;
        order.Status = OrderStatus.Shipping; // Status = 3
        order.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        logger.LogInformation(
            "[Shipper] Đã quét nhận thành công đơn #{OrderId} ({OrderCode}), chuyển từ '{OldStatus}' sang '{NewStatus}'. Mã GHN: {TrackingCode}",
            order.Id, order.OrderCode, oldStatus, order.Status, trackingCode);

        // 4. Kích hoạt gửi Email thứ 3: Thông báo bưu kiện đã xuất kho & đính kèm link tra cứu hành trình GHN
        string trackingUrl = $"https://ghn.vn?order_code={trackingCode}";
        bool emailSent = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(order.CustomerEmail))
            {
                string subject = $"[EcommerceHue] 🚚 Đơn hàng #{order.OrderCode} đang được Shipper GHN giao đến bạn";
                string htmlContent = GenerateShippingNotificationEmailHtml(order, trackingCode, trackingUrl);

                await emailService.SendOrderEmailAsync(order.CustomerEmail, subject, htmlContent);
                emailSent = true;
                logger.LogInformation("[Shipper] Đã gửi Email hành trình thứ 3 tới {Email}", order.CustomerEmail);
            }
            else
            {
                logger.LogWarning("[Shipper] Đơn #{OrderId} không có email khách hàng để gửi thông báo hành trình.", order.Id);
            }
        }
        catch (Exception exEmail)
        {
            logger.LogError(exEmail, "[Shipper] Lỗi khi gửi email hành trình tới {Email}: {Message}", order.CustomerEmail, exEmail.Message);
        }

        // 5. Trả về kết quả JSON chi tiết cho giao diện app Shipper
        return Json(new
        {
            success = true,
            message = "Shipper đã quét nhận đơn thành công! Đã cập nhật trạng thái ĐANG GIAO HÀNG và gửi Email hành trình cho khách hàng.",
            emailSent = emailSent,
            orderId = order.Id,
            orderCode = order.OrderCode,
            trackingCode = trackingCode,
            trackingUrl = trackingUrl,
            customerName = order.CustomerName,
            customerPhone = order.CustomerPhone,
            shippingAddress = order.ShippingAddress,
            newStatus = (int)order.Status,
            newStatusText = "Đang giao hàng (Shipper đã lấy)",
            itemsCount = order.OrderDetails?.Sum(od => od.Quantity) ?? 0,
            totalAmount = order.TotalAmount.ToString("#,##0") + " VNĐ"
        });
    }

    // =========================================================================
    // HELPER: Biên soạn mẫu HTML Email thứ 3 – Thông báo bưu kiện đang giao
    // =========================================================================

    private static string GenerateShippingNotificationEmailHtml(Order order, string trackingCode, string trackingUrl)
    {
        var itemsHtml = string.Empty;
        if (order.OrderDetails != null && order.OrderDetails.Any())
        {
            foreach (var item in order.OrderDetails)
            {
                var productName = item.Product?.Name ?? item.ProductName ?? "Đặc sản Huế";
                itemsHtml += $@"
                <tr style='border-bottom: 1px solid #e2e8f0;'>
                    <td style='padding: 10px 0; color: #1e293b; font-weight: 500;'>{productName}</td>
                    <td style='padding: 10px 0; text-align: center; color: #64748b; font-weight: 600;'>x{item.Quantity}</td>
                    <td style='padding: 10px 0; text-align: right; color: #1e293b; font-weight: 600;'>{item.UnitPrice:N0} ₫</td>
                </tr>";
            }
        }

        // Phân loại ghi chú tiền COD
        string codNoticeHtml;
        if ((int)order.Status == 1 || order.Status == OrderStatus.Paid || order.TotalAmount <= 0)
        {
            codNoticeHtml = "<span style='color: #16a34a; font-weight: bold;'>0 VNĐ (ĐÃ THANH TOÁN TRƯỚC - KHÔNG THU TIỀN)</span>";
        }
        else
        {
            codNoticeHtml = $"<span style='color: #dc2626; font-weight: bold;'>{order.TotalAmount:N0} VNĐ (Tiền thu hộ COD khi nhận hàng)</span>";
        }

        return $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='utf-8'/>
</head>
<body style='margin: 0; padding: 20px; background-color: #f8fafc; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; line-height: 1.6;'>
    <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.06); border: 1px solid #e2e8f0;'>
        <!-- HEADER THƯƠNG HIỆU -->
        <div style='background: linear-gradient(135deg, #581c87, #7e22ce); padding: 30px 24px; text-align: center; color: #ffffff;'>
            <div style='font-size: 26px; font-weight: 800; letter-spacing: 0.5px;'>🚚 BƯU KIỆN ĐANG ĐƯỢC GIAO</div>
            <div style='font-size: 14px; opacity: 0.9; margin-top: 6px;'>EcommerceHue – Tinh Hoa Đặc Sản Cố Đô</div>
        </div>

        <div style='padding: 28px 24px;'>
            <p style='font-size: 16px; color: #1e293b; margin-top: 0;'>
                Xin chào <strong>{order.CustomerName}</strong>,
            </p>
            <p style='color: #475569; font-size: 15px;'>
                Kiện hàng đặc sản Huế của bạn cho đơn hàng <strong>#{order.OrderCode}</strong> đã được <strong>Shipper Giao Hàng Nhanh (GHN)</strong> quét mã tiếp nhận thành công và đang trên đường vận chuyển tới địa chỉ của bạn!
            </p>

            <!-- HỘP THÔNG TIN VẬN CHUYỂN -->
            <div style='background-color: #f1f5f9; border-left: 4px solid #7e22ce; padding: 16px; border-radius: 8px; margin: 20px 0;'>
                <div style='font-size: 14px; color: #64748b; margin-bottom: 4px;'>Mã vận đơn bưu cục (GHN):</div>
                <div style='font-size: 20px; font-weight: 800; color: #581c87; font-family: monospace; letter-spacing: 1px;'>
                    {trackingCode}
                </div>
                <div style='font-size: 13px; color: #475569; margin-top: 8px;'>
                    📦 <strong>Đơn vị vận chuyển:</strong> Giao Hàng Nhanh (GHN Express)<br/>
                    📍 <strong>Địa chỉ giao:</strong> {order.ShippingAddress}<br/>
                    📞 <strong>Số điện thoại:</strong> {order.CustomerPhone}<br/>
                    💰 <strong>Tiền cần thanh toán khi nhận:</strong> {codNoticeHtml}
                </div>
            </div>

            <!-- NÚT TRA CỨU HÀNH TRÌNH BƯU CỤC -->
            <div style='text-align: center; margin: 28px 0;'>
                <a href='{trackingUrl}' target='_blank' style='display: inline-block; background: linear-gradient(135deg, #2563eb, #1d4ed8); color: #ffffff; text-decoration: none; font-size: 15px; font-weight: bold; padding: 14px 28px; border-radius: 8px; box-shadow: 0 4px 10px rgba(37,99,235,0.3);'>
                    🔍 Bấm Vào Đây Để Tra Cứu Hành Trình GHN
                </a>
                <div style='font-size: 12px; color: #94a3b8; margin-top: 8px;'>
                    Hoặc truy cập: <a href='{trackingUrl}' style='color: #2563eb;'>{trackingUrl}</a>
                </div>
            </div>

            <!-- DANH SÁCH MẶT HÀNG -->
            <div style='margin-top: 24px;'>
                <div style='font-weight: 700; color: #1e293b; font-size: 15px; margin-bottom: 10px;'>
                    Chi tiết sản phẩm đóng gói:
                </div>
                <table style='width: 100%; border-collapse: collapse; font-size: 14px;'>
                    <thead>
                        <tr style='border-bottom: 2px solid #cbd5e1; color: #64748b; font-size: 12px; text-transform: uppercase;'>
                            <th style='padding: 8px 0; text-align: left;'>Món đặc sản</th>
                            <th style='padding: 8px 0; text-align: center;'>SL</th>
                            <th style='padding: 8px 0; text-align: right;'>Đơn giá</th>
                        </tr>
                    </thead>
                    <tbody>
                        {itemsHtml}
                    </tbody>
                </table>
            </div>

            <!-- LỜI DẶN DÒ -->
            <div style='margin-top: 24px; padding: 14px; background-color: #fffbeb; border: 1px dashed #f59e0b; border-radius: 8px; font-size: 13px; color: #92400e;'>
                💡 <strong>Lưu ý nhận hàng:</strong> Quý khách vui lòng giữ máy điện thoại để Shipper tiện liên hệ. Kiểm tra tình trạng kiện hàng trước khi ký nhận.
            </div>

            <p style='color: #64748b; font-size: 14px; margin-top: 24px;'>
                Cảm ơn quý khách đã tin tưởng và ủng hộ hương vị quê hương xứ Huế!<br/>
                <strong>Ban Quản Trị EcommerceHue</strong>
            </p>
        </div>

        <div style='background-color: #f1f5f9; padding: 16px; text-align: center; font-size: 12px; color: #94a3b8;'>
            Email tự động từ hệ thống Cổng Đặc Sản Huế · Vui lòng không trả lời thư này trực tiếp.
        </div>
    </div>
</body>
</html>";
    }
}
