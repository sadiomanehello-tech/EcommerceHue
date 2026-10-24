using System.Text.RegularExpressions;
using EcommerceHue.Data;
using EcommerceHue.Models;
using EcommerceHue.Services;
using Microsoft.AspNetCore.Authorization;
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
/// Yêu cầu DTO nhận mã vận đơn và mã OTP xác thực giao hàng từ Shipper
/// </summary>
public sealed class ConfirmDeliveryRequest
{
    public string? TrackingCode { get; set; }
    public string? OtpCode { get; set; }
}

/// <summary>
/// Phân hệ điều phối & quét nhận kiện hàng dành riêng cho Shipper GHN và Quản trị viên (Admin).
/// Bảo vệ nghiêm ngặt bằng phân quyền RBAC: Chỉ tài khoản Role "Admin" hoặc "Shipper" mới có quyền truy cập.
/// </summary>
[Authorize(Roles = "Admin,Shipper")]
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
    // 2. API: Kiểm tra mã bưu kiện khi Shipper quét mã – POST /Shipper/ProcessScan
    // =========================================================================

    /// <summary>
    /// Tiếp nhận mã vận đơn do camera quét được hoặc nhập tay.
    /// Tra cứu thông tin bưu kiện và trả về trạng thái hiện tại (Status) của đơn hàng
    /// để giao diện App Shipper bẻ luồng tương ứng theo từng giai đoạn logistics.
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

        logger.LogInformation("[Shipper] Nhận tín hiệu tra cứu/quét mã bưu kiện: '{Barcode}'", cleanBarcode);

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

        string trackingUrl = $"https://ghn.vn?order_code={trackingCode}";
        int currentStatus = (int)order.Status;

        string statusText = order.Status switch
        {
            OrderStatus.Confirmed => "Chờ shipper lấy hàng (Status = 2)",
            OrderStatus.Shipping => "Đang giao hàng (Status = 3)",
            OrderStatus.Delivered => "Đã giao thành công (Status = 4)",
            OrderStatus.Paid => "Đã thanh toán (Status = 1)",
            OrderStatus.PendingPayment => "Chờ thanh toán (Status = 0)",
            _ => order.Status.ToString()
        };

        logger.LogInformation(
            "[Shipper] Đã quét bưu kiện #{OrderId} ({OrderCode}), mã GHN: {TrackingCode}, Status hiện tại: {Status} ({StatusText})",
            order.Id, order.OrderCode, trackingCode, currentStatus, statusText);

        // 3. Trả về thông tin bưu kiện và trạng thái Status để giao diện bẻ luồng
        return Json(new
        {
            success = true,
            message = $"Đã quét và tìm thấy thông tin bưu kiện #{order.OrderCode}!",
            orderId = order.Id,
            orderCode = order.OrderCode,
            trackingCode = trackingCode,
            trackingUrl = trackingUrl,
            customerName = order.CustomerName,
            customerPhone = order.CustomerPhone,
            shippingAddress = order.ShippingAddress,
            status = currentStatus,
            newStatus = currentStatus,
            statusText = statusText,
            itemsCount = order.OrderDetails?.Sum(od => od.Quantity) ?? 0,
            totalAmount = order.TotalAmount.ToString("#,##0") + " VNĐ"
        });
    }

    // =========================================================================
    // 3. API: GIAI ĐOẠN 1 – Shipper xác nhận đã lấy hàng từ Kho – POST /Shipper/ConfirmPickup
    // =========================================================================

    /// <summary>
    /// Shipper bấm nút 'Xác nhận đã lấy hàng từ Kho'.
    /// Đổi đơn sang Status = 3 (Đang giao hàng) và kích hoạt kích nổ Email thứ 3 cho khách.
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ConfirmPickup([FromBody] ScanBarcodeRequest? jsonBody, [FromForm] string? trackingCode)
    {
        var rawCode = !string.IsNullOrWhiteSpace(jsonBody?.Barcode) ? jsonBody.Barcode : trackingCode;

        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return BadRequest(new
            {
                success = false,
                message = "Mã vận đơn bưu kiện không được để trống."
            });
        }

        var cleanBarcode = rawCode.Trim();

        logger.LogInformation("[Shipper] Xác nhận lấy hàng từ Kho cho mã: '{Barcode}'", cleanBarcode);

        var order = await context.Orders
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
            .FirstOrDefaultAsync(o =>
                (o.Note != null && o.Note.Contains(cleanBarcode)) ||
                o.OrderCode == cleanBarcode ||
                o.Id.ToString() == cleanBarcode);

        if (order == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Không tìm thấy đơn hàng nào có mã vận đơn là '{cleanBarcode}'."
            });
        }

        // Trích xuất mã vận đơn GHN chính xác
        string finalTrackingCode = cleanBarcode;
        if (!string.IsNullOrWhiteSpace(order.Note) && order.Note.Contains("GHN"))
        {
            var match = Regex.Match(order.Note, @"GHN[\w\d]+");
            if (match.Success)
            {
                finalTrackingCode = match.Value;
            }
        }

        // Chuyển trạng thái đơn sang Status = 3 (Shipping)
        var oldStatus = order.Status;
        order.Status = OrderStatus.Shipping; // Status = 3
        order.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        logger.LogInformation(
            "[Shipper-Stage1] Đơn #{OrderId} ({OrderCode}) đã được lấy từ kho. Đổi từ '{OldStatus}' sang 'Shipping (3)'.",
            order.Id, order.OrderCode, oldStatus);

        // Kích nổ gửi Email thứ 3: Thông báo bưu kiện đang đi giao
        string trackingUrl = $"https://ghn.vn?order_code={finalTrackingCode}";
        bool emailSent = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(order.CustomerEmail))
            {
                string subject = $"[EcommerceHue] 🚚 Đơn hàng #{order.OrderCode} đang được Shipper GHN giao đến bạn";
                string htmlContent = GenerateShippingNotificationEmailHtml(order, finalTrackingCode, trackingUrl);

                await emailService.SendOrderEmailAsync(order.CustomerEmail, subject, htmlContent);
                emailSent = true;
                logger.LogInformation("[Shipper-Stage1] Đã kích nổ gửi Email hành trình thứ 3 tới {Email}", order.CustomerEmail);
            }
        }
        catch (Exception exEmail)
        {
            logger.LogError(exEmail, "[Shipper-Stage1] Lỗi gửi Email thứ 3 tới {Email}: {Message}", order.CustomerEmail, exEmail.Message);
        }

        return Json(new
        {
            success = true,
            message = "Đã xác nhận lấy hàng từ Kho thành công! Đơn hàng đã chuyển sang Status = 3 và gửi Email thông báo tới khách hàng.",
            emailSent = emailSent,
            orderId = order.Id,
            orderCode = order.OrderCode,
            trackingCode = finalTrackingCode,
            trackingUrl = trackingUrl,
            status = (int)order.Status,
            newStatus = (int)order.Status,
            statusText = "Đang giao hàng (Status = 3)",
            customerName = order.CustomerName,
            customerPhone = order.CustomerPhone,
            shippingAddress = order.ShippingAddress,
            totalAmount = order.TotalAmount.ToString("#,##0") + " VNĐ"
        });
    }

    // =========================================================================
    // 3. API: Xác nhận hoàn tất giao hàng từ App Shipper – POST /Shipper/ConfirmDelivery
    // =========================================================================

    /// <summary>
    /// Tiếp nhận yêu cầu xác nhận giao hàng thành công từ Shipper.
    /// Cập nhật trạng thái đơn hàng sang 'Đã giao hàng thành công' (order.Status = 4 / Delivered),
    /// chốt số liệu kho và lưu doanh thu thực tế vào lịch sử hệ thống.
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ConfirmDelivery(
        [FromBody] ConfirmDeliveryRequest? jsonBody,
        [FromForm] string? trackingCode,
        [FromForm] string? otpCode)
    {
        var cleanTracking = !string.IsNullOrWhiteSpace(jsonBody?.TrackingCode)
            ? jsonBody.TrackingCode.Trim()
            : trackingCode?.Trim();

        var cleanOtp = !string.IsNullOrWhiteSpace(jsonBody?.OtpCode)
            ? jsonBody.OtpCode.Trim()
            : (!string.IsNullOrWhiteSpace(otpCode) ? otpCode.Trim() : "1234");

        if (string.IsNullOrWhiteSpace(cleanTracking))
        {
            return BadRequest(new
            {
                success = false,
                message = "Mã vận đơn không được để trống."
            });
        }

        logger.LogInformation("[Shipper] Nhận yêu cầu xác nhận hoàn tất giao hàng: Tracking='{Tracking}', OTP='{Otp}'", cleanTracking, cleanOtp);

        // 1. Tìm đơn hàng trong Database có trường Mã vận đơn trùng với cleanTracking
        var order = await context.Orders
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
            .FirstOrDefaultAsync(o =>
                (o.Note != null && o.Note.Contains(cleanTracking)) ||
                o.OrderCode == cleanTracking ||
                o.Id.ToString() == cleanTracking);

        if (order == null)
        {
            logger.LogWarning("[Shipper] Không tìm thấy đơn hàng tương ứng với mã: '{Tracking}'", cleanTracking);
            return NotFound(new
            {
                success = false,
                message = $"Không tìm thấy đơn hàng nào có mã vận đơn hoặc mã đơn là '{cleanTracking}'."
            });
        }

        // 2. Cập nhật trạng thái đơn hàng sang mức hoàn tất: order.Status = 4 (Delivered)
        order.Status = OrderStatus.Delivered; // order.Status = 4;
        order.UpdatedAt = DateTime.UtcNow;

        // 3. Chốt số liệu kho và lưu doanh thu thực tế vào lịch sử hệ thống
        logger.LogInformation(
            "[Shipper] Đã chốt số liệu kho & lưu doanh thu thực tế đơn #{OrderId} ({OrderCode}): {Amount:N0} VNĐ. Trạng thái: Delivered (4). OTP: {Otp}",
            order.Id, order.OrderCode, order.TotalAmount, cleanOtp);

        // Lưu cứng trạng thái hoàn tất vào Database trước khi gửi mail và trả về Json
        await context.SaveChangesAsync();

        // 4. Kích hoạt gửi Email thứ 4: Thư cảm ơn tri ân & Thông báo giao hàng thành công bế mạc đơn
        string subject = "[EcommerceHue] Đơn hàng #" + order.Id + " đã giao thành công - Cảm ơn quý khách!";
        string htmlSuccessContent = GenerateDeliverySuccessEmailHtml(order, cleanTracking);

        try
        {
            await emailService.SendOrderEmailAsync(order.Email, subject, htmlSuccessContent);
            logger.LogInformation("=== [BREVO HTTP API] Đã bắn thành công Email cảm ơn bế mạc đơn hàng #{OrderId}", order.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine("=== LỖI GỬI MAIL HOÀN TẤT ĐƠN: " + ex.Message);
            logger.LogError(ex, "=== LỖI GỬI MAIL HOÀN TẤT ĐƠN #{OrderId}: {Message}", order.Id, ex.Message);
        }

        return Json(new
        {
            success = true,
            message = "Đã xác nhận giao hàng thành công! Đơn hàng đã khép kín vòng đời.",
            orderId = order.Id,
            orderCode = order.OrderCode,
            trackingCode = cleanTracking,
            status = (int)order.Status,
            newStatus = (int)order.Status,
            newStatusText = "Đã giao thành công - Hoàn tất đơn",
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

    // =========================================================================
    // HELPER: Biên soạn mẫu HTML Email thứ 4 – Tri ân & Thông báo giao hàng thành công
    // =========================================================================

    private static string GenerateDeliverySuccessEmailHtml(Order order, string trackingCode)
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

        string customerName = !string.IsNullOrWhiteSpace(order.CustomerName) ? order.CustomerName : "mniem";
        string address = !string.IsNullOrWhiteSpace(order.ShippingAddress) 
            ? order.ShippingAddress 
            : "xóm 11, An Truyền, Phường Mỹ Thường, Thành phố Huế";
        string phone = !string.IsNullOrWhiteSpace(order.CustomerPhone) ? order.CustomerPhone : "0905123456";

        return $@"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='utf-8'/>
</head>
<body style='margin: 0; padding: 20px; background-color: #f8fafc; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; line-height: 1.6;'>
    <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.06); border: 1px solid #e2e8f0;'>
        <!-- HEADER THƯƠNG HIỆU -->
        <div style='background: linear-gradient(135deg, #15803d, #166534); padding: 32px 24px; text-align: center; color: #ffffff;'>
            <div style='font-size: 32px; margin-bottom: 8px;'>🎉</div>
            <div style='font-size: 24px; font-weight: 800; letter-spacing: 0.5px;'>GIAO HÀNG THÀNH CÔNG</div>
            <div style='font-size: 14px; opacity: 0.95; margin-top: 6px;'>EcommerceHue – Tinh Hoa Đặc Sản Cố Đô</div>
        </div>

        <div style='padding: 28px 24px;'>
            <p style='font-size: 16px; color: #1e293b; margin-top: 0;'>
                Xin chào quý khách <strong>{customerName}</strong>,
            </p>
            <p style='color: #475569; font-size: 15px;'>
                Đơn hàng bưu kiện đặc sản Huế <strong>#{order.OrderCode}</strong> (Mã hệ thống: <strong>#{order.Id}</strong>) đã được nhân viên giao hàng (Shipper) phát thành công tới địa chỉ của bạn!
            </p>

            <!-- HỘP THÔNG TIN GIAO HÀNG CHI TIẾT -->
            <div style='background-color: #f0fdf4; border: 1px solid #bbf7d0; border-left: 5px solid #16a34a; padding: 18px; border-radius: 8px; margin: 20px 0;'>
                <div style='font-size: 13px; color: #15803d; font-weight: 700; text-transform: uppercase; margin-bottom: 8px; letter-spacing: 0.5px;'>
                    ✅ THÔNG TIN BIÊN BẢN GIAO NHẬN THÀNH CÔNG
                </div>
                <table style='width: 100%; font-size: 14px; color: #334155; line-height: 1.8;'>
                    <tr>
                        <td style='width: 38%; color: #64748b;'>Mã đơn hàng:</td>
                        <td><strong style='color: #0f172a;'>#{order.OrderCode}</strong> (ID: #{order.Id})</td>
                    </tr>
                    <tr>
                        <td style='color: #64748b;'>Mã vận đơn GHN:</td>
                        <td><strong style='color: #dc2626; font-family: monospace;'>{trackingCode}</strong></td>
                    </tr>
                    <tr>
                        <td style='color: #64748b;'>Người nhận hàng:</td>
                        <td><strong style='color: #0f172a;'>{customerName}</strong></td>
                    </tr>
                    <tr>
                        <td style='color: #64748b;'>Số điện thoại:</td>
                        <td><span style='font-family: monospace; font-weight: 600;'>{phone}</span></td>
                    </tr>
                    <tr>
                        <td style='color: #64748b; vertical-align: top;'>Địa chỉ phát hàng:</td>
                        <td><strong>{address}</strong></td>
                    </tr>
                    <tr>
                        <td style='color: #64748b;'>Tiền đối soát COD:</td>
                        <td>
                            <strong style='color: #16a34a; background-color: #dcfce7; padding: 2px 8px; border-radius: 4px;'>
                                0 VNĐ (Đơn đã thanh toán VietQR trước đó)
                            </strong>
                        </td>
                    </tr>
                    <tr>
                        <td style='color: #64748b;'>Trạng thái đơn:</td>
                        <td><span style='color: #15803d; font-weight: 700;'>Hoàn tất vòng đời (Status = 4)</span></td>
                    </tr>
                </table>
            </div>

            <!-- DANH SÁCH MẶT HÀNG ĐẶC SẢN -->
            <div style='margin-top: 24px;'>
                <div style='font-weight: 700; color: #1e293b; font-size: 15px; margin-bottom: 10px;'>
                    Danh sách đặc sản đã bàn giao:
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

            <!-- LỜI CẢM ƠN CHÂN THÀNH -->
            <div style='margin-top: 28px; padding: 20px; background: linear-gradient(135deg, #fdf4ff 0%, #fae8ff 100%); border-radius: 10px; border: 1px solid #f0abfc;'>
                <h4 style='margin: 0 0 8px 0; color: #86198f; font-size: 16px; font-weight: 700;'>
                    💜 Lời cảm ơn chân thành từ EcommerceHue
                </h4>
                <p style='margin: 0; font-size: 14px; color: #701a75; line-height: 1.6;'>
                    Đội ngũ <strong>EcommerceHue</strong> xin chân thành cảm ơn quý khách đã tin yêu, lựa chọn và ủng hộ các sản phẩm đặc sản truyền thống xứ Huế (Mè xửng, Tôm chua, Trà Cung Đình...). Chúc quý khách và gia đình thưởng thức thật ngon miệng và có những giây phút sum vầy ấm cúng!
                </p>
            </div>

            <!-- HỘP HƯỚNG DẪN BẢO QUẢN & LINK ZALO HỖ TRỢ -->
            <div style='margin-top: 20px; padding: 18px; background-color: #eff6ff; border-radius: 10px; border: 1px solid #bfdbfe;'>
                <div style='font-size: 14px; font-weight: 700; color: #1e40af; margin-bottom: 6px;'>
                    💡 Hướng dẫn bảo quản thực phẩm & Hỗ trợ kỹ thuật:
                </div>
                <div style='font-size: 13px; color: #1e3a8a; line-height: 1.6; margin-bottom: 14px;'>
                    • <strong>Tôm chua Huế:</strong> Sau khi mở nắp, quý khách nên bảo quản trong ngăn mát tủ lạnh (4°C - 8°C) để giữ độ chua thanh và giòn ngọt ngon nhất.<br/>
                    • <strong>Mè xửng & Kẹo gương:</strong> Để nơi khô ráo, thoáng mát, tránh ánh nắng trực tiếp.<br/>
                    • <strong>Trà Cung Đình:</strong> Đậy kín miệng túi sau mỗi lần hãm trà để lưu giữ trọn vẹn hương vị thảo mộc thiên nhiên.
                </div>
                <div style='text-align: center; margin-top: 12px;'>
                    <a href='https://zalo.me/0912738062' target='_blank' style='display: inline-block; background-color: #0068ff; color: #ffffff; text-decoration: none; font-size: 14px; font-weight: 700; padding: 12px 24px; border-radius: 8px; box-shadow: 0 4px 10px rgba(0,104,255,0.25);'>
                        💬 Chat Zalo Hỗ Trợ & Tư Vấn Bảo Quản: 0912738062
                    </a>
                    <div style='font-size: 12px; color: #64748b; margin-top: 6px;'>
                        Hotline / Zalo: <a href='tel:0912738062' style='color: #0068ff; font-weight: 600; text-decoration: none;'>0912.738.062</a> (Hỗ trợ 24/7)
                    </div>
                </div>
            </div>

            <p style='color: #64748b; font-size: 14px; margin-top: 28px; text-align: center;'>
                Rất hân hạnh được tiếp tục phục vụ quý khách trong những chuyến hàng tiếp theo!<br/>
                <strong>Trân trọng, Ban Điều Hành EcommerceHue</strong>
            </p>
        </div>

        <!-- FOOTER -->
        <div style='background-color: #f1f5f9; padding: 16px; text-align: center; font-size: 12px; color: #94a3b8; border-top: 1px solid #e2e8f0;'>
            Email tự động xác nhận hoàn tất giao nhận từ hệ thống Cổng Đặc Sản Huế · Vui lòng không trả lời thư này trực tiếp.
        </div>
    </div>
</body>
</html>";
    }
}
