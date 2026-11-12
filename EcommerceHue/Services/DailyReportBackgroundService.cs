using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EcommerceHue.Data;
using EcommerceHue.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EcommerceHue.Services;

/// <summary>
/// Dịch vụ chạy ngầm tự động tổng hợp và gửi Báo cáo doanh thu kinh doanh cuối ngày qua Telegram Bot.
/// Khung giờ kích hoạt: 21:00 - 21:30 hàng ngày (Giờ Việt Nam UTC+7).
/// </summary>
public class DailyReportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyReportBackgroundService> _logger;

    // Lưu vết ngày báo cáo gần nhất đã gửi thành công để tránh gửi lặp lại trong khung giờ 21:00 - 21:30
    private static DateTime? _lastReportDate;

    public DailyReportBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DailyReportBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[DailyReportBackgroundService] Đã kích hoạt tiến trình ngầm báo cáo doanh thu hàng ngày.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Quy đổi sang múi giờ Việt Nam (UTC+7)
                var nowVn = DateTime.UtcNow.AddHours(7);

                // Kiểm tra điều kiện: Khung giờ 21:00 đến 21:30 và chưa gửi báo cáo cho ngày hôm nay
                if (nowVn.Hour == 21 && nowVn.Minute <= 30)
                {
                    if (!_lastReportDate.HasValue || _lastReportDate.Value.Date != nowVn.Date)
                    {
                        _logger.LogInformation("[DailyReportBackgroundService] Bắt đầu tổng hợp số liệu báo cáo doanh thu ngày {Date:dd/MM/yyyy}...", nowVn.Date);
                        await ProcessAndSendDailyReportAsync(nowVn);
                        _lastReportDate = nowVn.Date;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DailyReportBackgroundService] Lỗi xử lý ngầm báo cáo doanh thu: {Message}", ex.Message);
            }

            // Chu kỳ kiểm tra định kỳ mỗi 15 phút
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("[DailyReportBackgroundService] Tiến trình báo cáo doanh thu ngầm đã dừng.");
    }

    /// <summary>
    /// Thực hiện truy vấn SQLite và tính toán các chỉ số kinh doanh trong ngày
    /// </summary>
    private async Task ProcessAndSendDailyReportAsync(DateTime vnNow)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EcommerceContext>();
        var telegramService = scope.ServiceProvider.GetRequiredService<TelegramService>();

        // Tính khoảng thời gian 00:00:00 đến 23:59:59 của ngày hôm nay theo giờ UTC tương ứng
        var targetDate = vnNow.Date;
        var startUtc = targetDate.AddHours(-7);
        var endUtc = startUtc.AddDays(1);

        // Nạp toàn bộ đơn hàng trong ngày về RAM trước khi tính toán để tránh lỗi SQLite decimal Sum
        var dayOrders = await context.Orders
            .Include(o => o.OrderDetails)
            .AsNoTracking()
            .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc)
            .ToListAsync();

        int totalOrders = dayOrders.Count;
        int completedOrders = dayOrders.Count(o => 
            o.Status == OrderStatus.Paid || 
            o.Status == OrderStatus.Confirmed || 
            o.Status == OrderStatus.Shipping || 
            o.Status == OrderStatus.Delivered);
        int cancelledOrders = dayOrders.Count(o => o.Status == OrderStatus.Cancelled);

        // Hàm kiểm tra phương thức COD dựa theo PaymentMethod và Note
        bool IsCod(Order o) => 
            (o.PaymentMethod != null && o.PaymentMethod.Contains("COD")) || 
            (o.Note != null && o.Note.Contains("COD"));

        // Doanh thu trực tuyến (PayOS) đã thu thực tế (đơn không phải COD và đã thanh toán)
        decimal payosRevenue = dayOrders
            .Where(o => !IsCod(o) && o.Status != OrderStatus.PendingPayment && o.Status != OrderStatus.Cancelled)
            .Sum(o => o.TotalAmount);

        // Doanh thu COD (đơn COD đang xử lý hoặc giao thành công, không tính đơn hủy)
        decimal codRevenue = dayOrders
            .Where(o => IsCod(o) && (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Shipping || o.Status == OrderStatus.Confirmed))
            .Sum(o => o.TotalAmount);

        // Tìm sản phẩm có tổng số lượng bán chạy nhất trong ngày
        var topProduct = dayOrders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.OrderDetails)
            .GroupBy(d => d.ProductName)
            .OrderByDescending(g => g.Sum(d => d.Quantity))
            .Select(g => $"{g.Key} ({g.Sum(d => d.Quantity)} sp)")
            .FirstOrDefault() ?? "Chưa có phát sinh";

        // Gửi thông báo tới Telegram Admin
        await telegramService.SendDailyRevenueReportAsync(
            targetDate, 
            totalOrders, 
            completedOrders, 
            cancelledOrders, 
            payosRevenue, 
            codRevenue, 
            topProduct);

        _logger.LogInformation("[DailyReportBackgroundService] Đã gửi báo cáo doanh thu ngày {Date:dd/MM/yyyy} thành công tới Telegram Bot.", targetDate);
    }
}
