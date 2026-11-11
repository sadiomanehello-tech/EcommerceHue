using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EcommerceHue.Data;
using EcommerceHue.Models;

namespace EcommerceHue.Services
{
    /// <summary>
    /// Giao diện dịch vụ tự động hủy đơn hàng quá hạn thanh toán và hoàn trả tồn kho.
    /// </summary>
    public interface IOrderExpirationService
    {
        /// <summary>
        /// Quét toàn bộ đơn hàng thanh toán online quá hạn 15 phút,
        /// đổi trạng thái sang Cancelled và hoàn trả số lượng vào tồn kho sản phẩm.
        /// </summary>
        /// <returns>Số lượng đơn hàng đã được hủy và hoàn trả kho.</returns>
        Task<int> ReleaseExpiredOrdersAsync();
    }

    /// <summary>
    /// Triển khai dịch vụ giải phóng tồn kho cho đơn hàng quá hạn thanh toán (Inventory Reservation Timeout).
    /// </summary>
    public class OrderExpirationService : IOrderExpirationService
    {
        private readonly EcommerceContext _context;
        private readonly DatabaseBackupService _backupService;
        private readonly ILogger<OrderExpirationService> _logger;

        public OrderExpirationService(
            EcommerceContext context,
            DatabaseBackupService backupService,
            ILogger<OrderExpirationService> logger)
        {
            _context = context;
            _backupService = backupService;
            _logger = logger;
        }

        public async Task<int> ReleaseExpiredOrdersAsync()
        {
            try
            {
                // Mốc thời gian quá hạn: Quá 15 phút so với thời điểm hiện tại (UTC)
                var threshold = DateTime.UtcNow.AddMinutes(-15);

                // Tải danh sách đơn hàng đang Chờ thanh toán (PendingPayment - 0) về bộ nhớ RAM
                // để tránh các hạn chế của SQLite khi so sánh ngày tháng phức tạp
                var pendingOrders = await _context.Orders
                    .Include(o => o.OrderDetails)
                    .Where(o => o.Status == OrderStatus.PendingPayment)
                    .ToListAsync();

                // Lọc trên RAM những đơn có thời gian tạo đơn nhỏ hơn hoặc bằng mốc 15 phút
                var expiredOrders = pendingOrders
                    .Where(o => o.CreatedAt <= threshold)
                    .ToList();

                if (expiredOrders.Count == 0)
                {
                    return 0;
                }

                _logger.LogInformation("[AUTO RELEASE STOCK] Phát hiện {Count} đơn hàng quá hạn 15 phút cần giải phóng kho.", expiredOrders.Count);

                int releasedCount = 0;

                foreach (var order in expiredOrders)
                {
                    // 1. Đổi trạng thái đơn hàng thành Cancelled (Đã hủy)
                    order.Status = OrderStatus.Cancelled;
                    order.UpdatedAt = DateTime.UtcNow;

                    var cancelNote = "[Tự động hủy: Quá hạn thanh toán 15 phút]";
                    order.Note = string.IsNullOrWhiteSpace(order.Note)
                        ? cancelNote
                        : $"{order.Note} | {cancelNote}";

                    // 2. Duyệt qua từng OrderDetail để cộng trả lại số lượng tồn kho cho sản phẩm
                    foreach (var detail in order.OrderDetails)
                    {
                        var product = await _context.Products.FindAsync(detail.ProductId);
                        if (product != null)
                        {
                            product.StockQuantity += detail.Quantity;
                            product.IsAvailable = true; // Mở lại bán nếu trước đó hết hàng
                            product.UpdatedAt = DateTime.UtcNow;

                            _logger.LogInformation(
                                "[AUTO RELEASE STOCK] Hoàn trả {Quantity} '{ProductName}' (ID: {ProductId}) từ đơn #{OrderId}. Tồn kho mới: {Stock}",
                                detail.Quantity, product.Name, product.Id, order.Id, product.StockQuantity);
                        }
                    }

                    // In rõ nhật ký ra Console theo yêu cầu
                    Console.WriteLine($"=== [AUTO RELEASE STOCK] Đã hủy đơn #{order.Id} ({order.OrderCode}) quá hạn và hoàn trả tồn kho!");
                    releasedCount++;
                }

                // 3. Lưu toàn bộ thay đổi trạng thái đơn và tồn kho vào cơ sở dữ liệu
                await _context.SaveChangesAsync();

                // 4. Kích hoạt sao lưu: Đồng bộ ngay lập tức tệp SQLite lên Supabase Storage
                try
                {
                    await _backupService.UploadDatabaseAsync();
                    _logger.LogInformation("[AUTO RELEASE STOCK] Đã đồng bộ dữ liệu hoàn kho #{Count} đơn lên Supabase Storage.", releasedCount);
                }
                catch (Exception backupEx)
                {
                    _logger.LogError(backupEx, "[AUTO RELEASE STOCK] Lỗi khi sao lưu DB sau khi hoàn kho: {Message}", backupEx.Message);
                }

                return releasedCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine("=== [AUTO RELEASE STOCK ERROR]: " + ex.ToString());
                _logger.LogError(ex, "=== [AUTO RELEASE STOCK ERROR] Lỗi trong quá trình quét đơn quá hạn: {Message}", ex.Message);
                return 0;
            }
        }
    }

    /// <summary>
    /// Background Service chạy nền định kỳ mỗi 2 phút để tự động quét và giải phóng đơn hàng quá hạn thanh toán.
    /// </summary>
    public class OrderExpirationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OrderExpirationBackgroundService> _logger;

        public OrderExpirationBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<OrderExpirationBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[OrderExpirationBackgroundService] Bắt đầu dịch vụ ngầm quét đơn quá hạn thanh toán.");

            // Chờ 15 giây sau khi server khởi động xong và tải xong DB từ Supabase Storage
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var expirationService = scope.ServiceProvider.GetRequiredService<IOrderExpirationService>();
                    await expirationService.ReleaseExpiredOrdersAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[OrderExpirationBackgroundService] Lỗi trong chu kỳ quét đơn quá hạn: {Message}", ex.Message);
                }

                try
                {
                    // Lặp lại mỗi 2 phút một lần
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("[OrderExpirationBackgroundService] Đã dừng dịch vụ ngầm quét đơn quá hạn.");
        }
    }
}

