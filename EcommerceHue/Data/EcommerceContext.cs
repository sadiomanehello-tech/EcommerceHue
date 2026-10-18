using Microsoft.EntityFrameworkCore;
using EcommerceHue.Models;

namespace EcommerceHue.Data;

/// <summary>
/// DbContext chính của website đặc sản Huế.
/// Cấu hình đầy đủ quan hệ giữa các bảng và các ràng buộc dữ liệu.
/// </summary>
public sealed class EcommerceContext(DbContextOptions<EcommerceContext> options)
    : DbContext(options)
{
    // =========================================================================
    // DbSet – đại diện mỗi bảng trong cơ sở dữ liệu
    // =========================================================================

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<PaymentTransaction> PaymentTransactions => Set<PaymentTransaction>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Coupon> Coupons => Set<Coupon>();

    // =========================================================================
    // Cấu hình Warning & Options
    // Bỏ qua cảnh báo PendingModelChangesWarning của EF Core 9 khi chạy trên Render
    // =========================================================================

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ConfigureWarnings(warnings => 
            warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }

    // =========================================================================
    // Cấu hình Fluent API – mối quan hệ và ràng buộc
    // =========================================================================

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Hỗ trợ linh hoạt cả SQLite (CURRENT_TIMESTAMP) và SQL Server (GETUTCDATE())
        var defaultDateSql = Database.IsSqlite() ? "CURRENT_TIMESTAMP" : "GETUTCDATE()";

        // --------------------------------------------------------------------
        // Category
        // --------------------------------------------------------------------
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");

            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(c => c.Description)
                  .HasMaxLength(500);

            entity.Property(c => c.ImageUrl)
                  .HasMaxLength(255);

            // Đánh index để tìm kiếm theo tên nhanh hơn
            entity.HasIndex(c => c.Name)
                  .HasDatabaseName("IX_Categories_Name");
        });

        // --------------------------------------------------------------------
        // Product
        // Bắt buộc có StockQuantity và ExpiryDate (quy chuẩn 01-coding-style.md)
        // --------------------------------------------------------------------
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");

            entity.HasKey(p => p.Id);

            entity.Property(p => p.Name)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(p => p.Description)
                  .HasMaxLength(2000);

            entity.Property(p => p.Price)
                  .HasColumnType("decimal(18,0)")
                  .IsRequired();

            entity.Property(p => p.ImageUrl)
                  .HasMaxLength(255);

            entity.Property(p => p.Slug)
                  .HasMaxLength(200);

            // Số lượng tồn kho – bắt buộc, không được âm
            entity.Property(p => p.StockQuantity)
                  .IsRequired()
                  .HasDefaultValue(0);

            // Hạn sử dụng – bắt buộc với thực phẩm đặc sản Huế
            entity.Property(p => p.ExpiryDate)
                  .IsRequired();

            entity.Property(p => p.CreatedAt)
                  .IsRequired()
                  .HasDefaultValueSql(defaultDateSql);

            entity.Property(p => p.UpdatedAt)
                  .IsRequired()
                  .HasDefaultValueSql(defaultDateSql);

            // Index slug để tìm kiếm URL thân thiện
            entity.HasIndex(p => p.Slug)
                  .IsUnique()
                  .HasDatabaseName("IX_Products_Slug");

            // Quan hệ: Product -------<N>----[Category]
            // Một danh mục có nhiều sản phẩm; xóa danh mục sẽ hạn chế nếu còn sản phẩm
            entity.HasOne(p => p.Category)
                  .WithMany(c => c.Products)
                  .HasForeignKey(p => p.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_Products_Categories");
        });

        // --------------------------------------------------------------------
        // Order
        // --------------------------------------------------------------------
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");

            entity.HasKey(o => o.Id);

            entity.Property(o => o.OrderCode)
                  .IsRequired()
                  .HasMaxLength(50);

            // Mã đơn hàng phải duy nhất để gửi sang VnPay
            entity.HasIndex(o => o.OrderCode)
                  .IsUnique()
                  .HasDatabaseName("IX_Orders_OrderCode");

            entity.Property(o => o.CustomerName)
                  .IsRequired()
                  .HasMaxLength(150);

            entity.Property(o => o.CustomerEmail)
                  .IsRequired()
                  .HasMaxLength(150);

            entity.Property(o => o.CustomerPhone)
                  .IsRequired()
                  .HasMaxLength(20);

            entity.Property(o => o.ShippingAddress)
                  .IsRequired()
                  .HasMaxLength(500);

            entity.Property(o => o.Note)
                  .HasMaxLength(1000);

            entity.Property(o => o.SubTotal)
                  .HasColumnType("decimal(18,0)");

            entity.Property(o => o.ShippingFee)
                  .HasColumnType("decimal(18,0)")
                  .HasDefaultValue(0m);

            entity.Property(o => o.TotalAmount)
                  .HasColumnType("decimal(18,0)");

            entity.Property(o => o.Status)
                  .HasConversion<int>()
                  .HasDefaultValue(OrderStatus.PendingPayment);

            entity.Property(o => o.CreatedAt)
                  .IsRequired()
                  .HasDefaultValueSql(defaultDateSql);

            entity.Property(o => o.UpdatedAt)
                  .IsRequired()
                  .HasDefaultValueSql(defaultDateSql);
        });

        // --------------------------------------------------------------------
        // OrderDetail
        // --------------------------------------------------------------------
        modelBuilder.Entity<OrderDetail>(entity =>
        {
            entity.ToTable("OrderDetails");

            entity.HasKey(od => od.Id);

            entity.Property(od => od.ProductName)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(od => od.UnitPrice)
                  .HasColumnType("decimal(18,0)")
                  .IsRequired();

            entity.Property(od => od.Quantity)
                  .IsRequired();

            // LineTotal là cột tính toán (computed) – không lưu vật lý
            entity.Ignore(od => od.LineTotal);

            // Quan hệ: OrderDetail >-------[Order]
            // Xóa đơn hàng → xóa cascade toàn bộ chi tiết đơn
            entity.HasOne(od => od.Order)
                  .WithMany(o => o.OrderDetails)
                  .HasForeignKey(od => od.OrderId)
                  .OnDelete(DeleteBehavior.Cascade)
                  .HasConstraintName("FK_OrderDetails_Orders");

            // Quan hệ: OrderDetail >-------[Product]
            // Không xóa cascade sản phẩm để giữ lịch sử đơn hàng
            entity.HasOne(od => od.Product)
                  .WithMany(p => p.OrderDetails)
                  .HasForeignKey(od => od.ProductId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_OrderDetails_Products");

            // Index phục vụ truy vấn nhanh chi tiết theo đơn hàng
            entity.HasIndex(od => od.OrderId)
                  .HasDatabaseName("IX_OrderDetails_OrderId");
        });

        // --------------------------------------------------------------------
        // PaymentTransaction – nhật ký giao dịch VnPay
        // Theo quy tắc 02-security-vnpay.md: lưu đầy đủ để kiểm toán bảo mật
        // --------------------------------------------------------------------
        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("PaymentTransactions");

            entity.HasKey(pt => pt.Id);

            entity.Property(pt => pt.VnpTransactionNo)
                  .HasMaxLength(50);

            entity.Property(pt => pt.VnpTxnRef)
                  .HasMaxLength(50);

            entity.Property(pt => pt.VnpResponseCode)
                  .HasMaxLength(10);

            entity.Property(pt => pt.VnpBankCode)
                  .HasMaxLength(20);

            entity.Property(pt => pt.VnpCardType)
                  .HasMaxLength(20);

            entity.Property(pt => pt.VnpPayDate)
                  .HasMaxLength(20);

            entity.Property(pt => pt.VnpOrderInfo)
                  .HasMaxLength(255);

            entity.Property(pt => pt.ReceivedSecureHash)
                  .HasMaxLength(256);

            entity.Property(pt => pt.AuditNote)
                  .HasMaxLength(1000);

            entity.Property(pt => pt.Status)
                  .HasConversion<int>()
                  .HasDefaultValue(PaymentStatus.Pending);

            entity.Property(pt => pt.ReceivedAt)
                  .IsRequired()
                  .HasDefaultValueSql(defaultDateSql);

            // Index giúp truy vấn nhanh theo mã giao dịch VnPay
            entity.HasIndex(pt => pt.VnpTransactionNo)
                  .HasDatabaseName("IX_PaymentTransactions_VnpTransactionNo");

            // Index theo đơn hàng để lọc lịch sử thanh toán
            entity.HasIndex(pt => pt.OrderId)
                  .HasDatabaseName("IX_PaymentTransactions_OrderId");

            // Quan hệ: PaymentTransaction >-------[Order]
            // Xóa đơn hàng → giữ lại log giao dịch để kiểm toán (Restrict)
            entity.HasOne(pt => pt.Order)
                  .WithMany(o => o.PaymentTransactions)
                  .HasForeignKey(pt => pt.OrderId)
                  .OnDelete(DeleteBehavior.Restrict)
                  .HasConstraintName("FK_PaymentTransactions_Orders");
        });

        // --------------------------------------------------------------------
        // Customer – tài khoản khách hàng (mật khẩu lưu BCrypt hash)
        // --------------------------------------------------------------------
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");

            entity.HasKey(c => c.Id);

            entity.Property(c => c.Username)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.Property(c => c.PasswordHash)
                  .IsRequired()
                  .HasMaxLength(255);

            entity.Property(c => c.FullName)
                  .IsRequired()
                  .HasMaxLength(150);

            entity.Property(c => c.Email)
                  .IsRequired()
                  .HasMaxLength(150);

            entity.Property(c => c.PhoneNumber)
                  .IsRequired()
                  .HasMaxLength(20);

            entity.Property(c => c.Address)
                  .IsRequired()
                  .HasMaxLength(500);

            entity.Property(c => c.CreatedAt)
                  .IsRequired()
                  .HasDefaultValueSql(defaultDateSql);

            // Username và Email phải duy nhất trong hệ thống
            entity.HasIndex(c => c.Username)
                  .IsUnique()
                  .HasDatabaseName("UQ_Customers_Username");

            entity.HasIndex(c => c.Email)
                  .IsUnique()
                  .HasDatabaseName("UQ_Customers_Email");

            // Một khách hàng có thể có nhiều đơn hàng
            entity.HasMany(c => c.Orders)
                  .WithOne()
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // --------------------------------------------------------------------
        // Post – bài viết cẩm nang ẩm thực Huế
        // --------------------------------------------------------------------
        modelBuilder.Entity<Post>(entity =>
        {
            entity.ToTable("Posts");

            entity.HasKey(p => p.Id);

            entity.Property(p => p.Title)
                  .IsRequired()
                  .HasMaxLength(250);

            entity.Property(p => p.Slug)
                  .IsRequired()
                  .HasMaxLength(250);

            entity.Property(p => p.Summary)
                  .IsRequired()
                  .HasMaxLength(500);

            entity.Property(p => p.Content)
                  .IsRequired();

            entity.Property(p => p.ImageUrl)
                  .HasMaxLength(500);

            entity.Property(p => p.CreatedDate)
                  .IsRequired()
                  .HasDefaultValueSql(defaultDateSql);

            entity.Property(p => p.IsPublished)
                  .IsRequired()
                  .HasDefaultValue(true);

            // Index slug bài viết để tăng tốc độ truy vấn chi tiết bài viết theo URL SEO
            entity.HasIndex(p => p.Slug)
                  .IsUnique()
                  .HasDatabaseName("IX_Posts_Slug");
        });

        // --------------------------------------------------------------------
        // Coupon – mã giảm giá cho đơn hàng đầu tiên / khuyến mãi
        // --------------------------------------------------------------------
        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.ToTable("Coupons");

            entity.HasKey(c => c.Id);

            entity.Property(c => c.Code)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.Property(c => c.DiscountPercentage)
                  .IsRequired();

            entity.Property(c => c.IsActive)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(c => c.CreatedAt)
                  .IsRequired()
                  .HasDefaultValueSql(defaultDateSql);

            // Đánh Index Unique cho Code để tra cứu nhanh và không trùng lặp mã
            entity.HasIndex(c => c.Code)
                  .IsUnique()
                  .HasDatabaseName("IX_Coupons_Code");
        });
    }
}
