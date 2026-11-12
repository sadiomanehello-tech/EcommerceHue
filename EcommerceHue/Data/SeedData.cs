using EcommerceHue.Models;
using EcommerceHue.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EcommerceHue.Data;

/// <summary>
/// Cơ chế tự động nạp dữ liệu mẫu ban đầu (Seed Data Automation) cho SQLite và môi trường Cloud.
/// Tự động sinh danh mục, 6 sản phẩm đặc sản Huế và 2 tài khoản quản trị/khách hàng.
/// </summary>
public static class SeedData
{
    private static readonly Dictionary<string, string> ProductImages = new()
    {
        ["me-xung-thien-huong"]          = "https://images.unsplash.com/photo-1594100889583-a421e2b8265c?w=400&h=300&fit=crop&q=80",
        ["me-xung-lac-phu-huong"]        = "https://images.unsplash.com/photo-1772984960196-b387b4f2f524?w=400&h=300&fit=crop&q=80",
        ["tom-chua-trong-duc"]           = "https://plus.unsplash.com/premium_photo-1709146097755-f5f9ba107de8?w=400&h=300&fit=crop&q=80",
        ["tom-chua-ba-dang-500g"]        = "https://upload.wikimedia.org/wikipedia/commons/c/c0/Mam_tom_chua_di_Can.jpg",
        ["tra-cung-dinh-hue-100g"]       = "https://images.unsplash.com/photo-1758899058841-d70f58437fe5?w=400&h=300&fit=crop&q=80",
        ["tra-sen-cung-dinh-50-tui-loc"] = "https://plus.unsplash.com/premium_photo-1674406481284-43eba097a291?w=400&h=300&fit=crop&q=80"
    };

    private static readonly Dictionary<string, string> CategoryImages = new()
    {
        ["Mè xửng"]           = "https://images.unsplash.com/photo-1772984960196-b387b4f2f524?w=400&h=300&fit=crop&q=80",
        ["Tôm chua"]          = "https://upload.wikimedia.org/wikipedia/commons/c/c0/Mam_tom_chua_di_Can.jpg",
        ["Trà cung đình Huế"] = "https://images.unsplash.com/photo-1758899058841-d70f58437fe5?w=400&h=300&fit=crop&q=80"
    };

    /// <summary>
    /// Hàm khởi tạo tự động toàn bộ dữ liệu mẫu vào cơ sở dữ liệu.
    /// </summary>
    public static async Task Initialize(IServiceProvider serviceProvider)
    {
        var context = serviceProvider.GetRequiredService<EcommerceContext>();
        var backupService = serviceProvider.GetService<DatabaseBackupService>();

        // 1. Đảm bảo cấu trúc các bảng đã được tạo sẵn
        if (context.Database.IsSqlite())
        {
            await context.Database.EnsureCreatedAsync();
        }
        else if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
        }

        // Đảm bảo bảng Coupons có đầy đủ các cột mới mở rộng (ALTER TABLE cho SQLite cũ tải từ Supabase)
        await EnsureCouponColumnsAsync(context);

        // Đảm bảo bảng Products có đầy đủ 3 cột mới mở rộng cho sản phẩm thực phẩm / thủ công
        await EnsureProductColumnsAsync(context);

        // 2. Khởi tạo danh mục đặc sản Huế nếu chưa có hoặc bổ sung các danh mục mẫu mới
        var defaultCategories = new (string Name, string Description, string ImageUrl, int Order)[]
        {
            ("Mè xửng", "Mè xửng Huế – đặc sản truyền thống ngọt thanh, thơm bùi mè rang.",
             CategoryImages.GetValueOrDefault("Mè xửng", "https://images.unsplash.com/photo-1772984960196-b387b4f2f524?w=400&h=300&fit=crop&q=80"), 1),
            ("Tôm chua", "Tôm chua Huế – hương vị cung đình lên men tự nhiên, đậm đà bản sắc cố đô.",
             CategoryImages.GetValueOrDefault("Tôm chua", "https://upload.wikimedia.org/wikipedia/commons/c/c0/Mam_tom_chua_di_Can.jpg"), 2),
            ("Trà cung đình Huế", "Trà cung đình Huế – thảo mộc hoàng gia quý phái, thanh nhiệt dưỡng sinh.",
             CategoryImages.GetValueOrDefault("Trà cung đình Huế", "https://images.unsplash.com/photo-1758899058841-d70f58437fe5?w=400&h=300&fit=crop&q=80"), 3),
            ("Nem & Tré Huế", "Nem chua, tré gia truyền chuẩn vị Cố Đô",
             "https://images.unsplash.com/photo-1544025162-d76694265947?w=400&h=300&fit=crop&q=80", 4),
            ("Bánh & Kẹo truyền thống", "Bánh ép, bánh bột lọc, kẹo cau xứ Huế",
             "https://images.unsplash.com/photo-1509440159596-0249088772ff?w=400&h=300&fit=crop&q=80", 5),
            ("Combo Quà Biếu Huế", "Các bộ quà tặng đặc sản đóng hộp sang trọng",
             "https://images.unsplash.com/photo-1549465220-1a8b9238cd48?w=400&h=300&fit=crop&q=80", 6)
        };

        bool categoriesAdded = false;
        foreach (var catInfo in defaultCategories)
        {
            if (!await context.Categories.AnyAsync(c => c.Name == catInfo.Name))
            {
                await context.Categories.AddAsync(new Category
                {
                    Name = catInfo.Name,
                    Description = catInfo.Description,
                    ImageUrl = catInfo.ImageUrl,
                    DisplayOrder = catInfo.Order,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                categoriesAdded = true;
                Console.WriteLine($"=== [SEED DATA] Đã thêm danh mục mới: '{catInfo.Name}' ===");
            }
        }

        if (categoriesAdded)
        {
            await context.SaveChangesAsync();
            try
            {
                if (backupService != null)
                {
                    await backupService.UploadDatabaseAsync();
                    Console.WriteLine("=== [SEED DATA] Đã đồng bộ danh mục mới lên Supabase Storage thành công! ===");
                }
            }
            catch (Exception exSync)
            {
                Console.WriteLine("=== [SEED DATA BACKUP ERROR]: " + exSync.Message);
            }
        }

        // Lấy danh mục để liên kết khóa ngoại Foreign Key
        var catMeXung = await context.Categories.FirstOrDefaultAsync(c => c.Name == "Mè xửng");
        var catTomChua = await context.Categories.FirstOrDefaultAsync(c => c.Name == "Tôm chua");
        var catTra = await context.Categories.FirstOrDefaultAsync(c => c.Name == "Trà cung đình Huế");

        // 3. Khởi tạo 6 sản phẩm đặc sản Huế mẫu nếu bảng Products trống
        if (!await context.Products.AnyAsync())
        {
            var products = new List<Product>
            {
                // Nhóm Mè xửng
                new()
                {
                    Name          = "Mè xửng Thiên Hương",
                    Description   = "Mè xửng Thiên Hương – thương hiệu lâu đời nức tiếng cố đô Huế. Vị kẹo dẻo thơm, ngọt dịu quyện cùng mè vừng rang bùi béo.",
                    Price         = 45_000m,
                    ImageUrl      = ProductImages["me-xung-thien-huong"],
                    Slug          = "me-xung-thien-huong",
                    StockQuantity = 200,
                    ExpiryDate    = new DateOnly(2027, 4, 13),
                    IsAvailable   = true,
                    CategoryId    = catMeXung?.Id ?? 1,
                    CreatedAt     = DateTime.UtcNow,
                    UpdatedAt     = DateTime.UtcNow
                },
                new()
                {
                    Name          = "Mè xửng lạc Phú Hương",
                    Description   = "Mè xửng lạc Phú Hương – kẹo mè giòn rụm kết hợp đậu phộng rang giòn thơm ngon, món quà biếu ý nghĩa đậm đà phong vị xứ Huế.",
                    Price         = 55_000m,
                    ImageUrl      = ProductImages["me-xung-lac-phu-huong"],
                    Slug          = "me-xung-lac-phu-huong",
                    StockQuantity = 200,
                    ExpiryDate    = new DateOnly(2027, 3, 31),
                    IsAvailable   = true,
                    CategoryId    = catMeXung?.Id ?? 1,
                    CreatedAt     = DateTime.UtcNow,
                    UpdatedAt     = DateTime.UtcNow
                },

                // Nhóm Tôm chua
                new()
                {
                    Name          = "Tôm chua Trọng Đức",
                    Description   = "Tôm chua Trọng Đức – tôm sú tươi ngon ủ men chua cay mặn ngọt chuẩn vị cung đình, ăn kèm thịt ba chỉ luộc và rau sống tuyệt hảo.",
                    Price         = 120_000m,
                    ImageUrl      = ProductImages["tom-chua-trong-duc"],
                    Slug          = "tom-chua-trong-duc",
                    StockQuantity = 200,
                    ExpiryDate    = new DateOnly(2026, 11, 12),
                    IsAvailable   = true,
                    CategoryId    = catTomChua?.Id ?? 2,
                    CreatedAt     = DateTime.UtcNow,
                    UpdatedAt     = DateTime.UtcNow
                },
                new()
                {
                    Name          = "Tôm chua Bà Đặng hũ 500g",
                    Description   = "Tôm chua Bà Đặng – gia truyền 3 đời tại chợ Đông Ba. Hũ 500g đầy đặn, tôm đỏ au ngâm riềng ớt cay nồng đậm đà khó quên.",
                    Price         = 155_000m,
                    ImageUrl      = ProductImages["tom-chua-ba-dang-500g"],
                    Slug          = "tom-chua-ba-dang-500g",
                    StockQuantity = 200,
                    ExpiryDate    = new DateOnly(2026, 11, 30),
                    IsAvailable   = true,
                    CategoryId    = catTomChua?.Id ?? 2,
                    CreatedAt     = DateTime.UtcNow,
                    UpdatedAt     = DateTime.UtcNow
                },

                // Nhóm Trà cung đình Huế
                new()
                {
                    Name          = "Trà cung đình Huế hộp 100g",
                    Description   = "Trà cung đình Huế hộp 100g – phối trộn 16 vị thảo mộc quý theo công thức bí truyền triều Nguyễn, giúp an thần ngủ ngon và dưỡng nhan.",
                    Price         = 180_000m,
                    ImageUrl      = ProductImages["tra-cung-dinh-hue-100g"],
                    Slug          = "tra-cung-dinh-hue-100g",
                    StockQuantity = 200,
                    ExpiryDate    = new DateOnly(2028, 10, 13),
                    IsAvailable   = true,
                    CategoryId    = catTra?.Id ?? 3,
                    CreatedAt     = DateTime.UtcNow,
                    UpdatedAt     = DateTime.UtcNow
                },
                new()
                {
                    Name          = "Trà sen cung đình hộp 50 túi lọc",
                    Description   = "Trà sen cung đình hộp 50 túi lọc – trà xanh ướp hoa sen đầm Tịnh Tâm thanh khiết, tiện lợi mang theo thưởng thức mỗi ngày.",
                    Price         = 95_000m,
                    ImageUrl      = ProductImages["tra-sen-cung-dinh-50-tui-loc"],
                    Slug          = "tra-sen-cung-dinh-50-tui-loc",
                    StockQuantity = 200,
                    ExpiryDate    = new DateOnly(2028, 6, 30),
                    IsAvailable   = true,
                    CategoryId    = catTra?.Id ?? 3,
                    CreatedAt     = DateTime.UtcNow,
                    UpdatedAt     = DateTime.UtcNow
                }
            };

            await context.Products.AddRangeAsync(products);
            await context.SaveChangesAsync();
        }

        // 4. BẢO TOÀN DỮ LIỆU ĐƠN HÀNG VÀ KHÁCH HÀNG
        // Đã xóa bỏ hoàn toàn khối lệnh RemoveRange để đảm bảo không xóa dữ liệu đơn hàng và khách hàng khi restart.

        // 5. Khởi tạo mã giảm giá mẫu nếu chưa có
        await EnsureCouponColumnsAsync(context);

        if (!await context.Coupons.AnyAsync(c => c.Code == "HUEWELCOME10"))
        {
            await context.Coupons.AddAsync(new Coupon
            {
                Code = "HUEWELCOME10",
                DiscountPercentage = 10,
                Description = "Mã ưu đãi 10% dành riêng cho đơn hàng đầu tiên của thành viên mới",
                MinOrderValue = 0m,
                MaxDiscountAmount = 50_000m,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddMonths(12),
                UsageLimit = 1000,
                UsedCount = 0,
                EventTag = "Normal",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Mã SALE1111: Giảm 15%, đơn tối thiểu 200.000đ, giảm tối đa 50.000đ, EventTag = 'DoubleDay'
        if (!await context.Coupons.AnyAsync(c => c.Code == "SALE1111"))
        {
            await context.Coupons.AddAsync(new Coupon
            {
                Code = "SALE1111",
                DiscountPercentage = 15,
                Description = "Siêu hội ngày đôi 11/11 - Giảm 15% cho đơn từ 200k (Tối đa 50k)",
                MinOrderValue = 200_000m,
                MaxDiscountAmount = 50_000m,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddMonths(1),
                UsageLimit = 200,
                UsedCount = 0,
                EventTag = "DoubleDay",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Mã CUOITUAN10: Giảm 10%, đơn tối thiểu 150.000đ, EventTag = 'Weekend'
        if (!await context.Coupons.AnyAsync(c => c.Code == "CUOITUAN10"))
        {
            await context.Coupons.AddAsync(new Coupon
            {
                Code = "CUOITUAN10",
                DiscountPercentage = 10,
                Description = "Rộn ràng cuối tuần - Giảm 10% cho đơn từ 150k",
                MinOrderValue = 150_000m,
                MaxDiscountAmount = null,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddMonths(2),
                UsageLimit = 150,
                UsedCount = 0,
                EventTag = "Weekend",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Mã HUELOVE20: Giảm 20%, đơn tối thiểu 350.000đ, giảm tối đa 80.000đ, EventTag = 'Holiday'
        if (!await context.Coupons.AnyAsync(c => c.Code == "HUELOVE20"))
        {
            await context.Coupons.AddAsync(new Coupon
            {
                Code = "HUELOVE20",
                DiscountPercentage = 20,
                Description = "Tri ân du khách - Giảm 20% cho đơn đặc sản từ 350k (Tối đa 80k)",
                MinOrderValue = 350_000m,
                MaxDiscountAmount = 80_000m,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddMonths(1),
                UsageLimit = 100,
                UsedCount = 0,
                EventTag = "Holiday",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        // Đóng và giải phóng connection pool SQLite để tránh khóa tệp DB trước khi upload
        SqliteConnection.ClearAllPools();

        // Đồng bộ cơ sở dữ liệu đã nâng cấp cấu trúc lên lại Supabase Storage
        if (backupService != null)
        {
            try
            {
                await backupService.UploadDatabaseAsync();
                Console.WriteLine("=== [SUPABASE BACKUP] Đã đồng bộ bản DB đã nâng cấp cột lên lại Supabase Storage! ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== [SUPABASE BACKUP WARNING] Lỗi khi upload DB sau khi seed: {ex.Message} ===");
            }
        }

        // 6. Khởi tạo bài viết Blog ẩm thực Huế mẫu nếu chưa có
        if (!await context.Posts.AnyAsync())
        {
            var posts = new List<Post>
            {
                new()
                {
                    Title = "Top 5 quán bún bò Huế ngon thưởng thức quên lối về",
                    Slug = "top-5-quan-bun-bo-hue-ngon-thuong-thuc-quen-loi-ve",
                    Summary = "Khám phá những quán bún bò Huế gia truyền nức tiếng cố đô với nước dùng thanh ngọt thơm mùi sả, mắm ruốc cùng sợi bún to dai ngon khó cưỡng.",
                    ImageUrl = "https://images.unsplash.com/photo-1582878826629-29b7ad1cdc43?w=800&h=500&fit=crop&q=80",
                    CreatedDate = DateTime.UtcNow.AddDays(-5),
                    IsPublished = true,
                    Content = @"<p class=""lead text-muted"">Bún bò Huế từ lâu đã trở thành biểu tượng ẩm thực không thể thiếu khi nhắc về vùng đất cố đô cổ kính. Tô bún bò xứ Huế chuẩn vị luôn quyến rũ thực khách bởi làn khói nghi ngút, sắc đỏ óng ánh của sa tế ớt cay nồng, vị thơm thảo của củ sả tươi quyện cùng mắm ruốc truyền thống đậm đà khó quên.</p>"
                },
                new()
                {
                    Title = "Tré Huế – Thơm ngon ngất ngây, gợi nhớ hồi ức",
                    Slug = "tre-hue-thom-ngon-ngat-ngay-goi-nho-hoi-uc",
                    Summary = "Tré Huế mang hương vị cay nồng của ớt, thơm ấm của củ riềng và độ giòn sần sật của tai heo, là món mồi nhắm và đặc sản biếu tặng trứ danh xứ cố đô.",
                    ImageUrl = "https://images.unsplash.com/photo-1544025162-d76694265947?w=800&h=500&fit=crop&q=80",
                    CreatedDate = DateTime.UtcNow.AddDays(-2),
                    IsPublished = true,
                    Content = @"<p class=""lead text-muted"">Cùng với nem chua và chả lụa, tré Huế là món ăn truyền thống đặc sắc luôn xuất hiện trang trọng trong các mâm cỗ tiệc, ngày Tết và những dịp sum họp gia đình của người dân xứ Huế.</p>"
                }
            };

            await context.Posts.AddRangeAsync(posts);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Đảm bảo bảng Products chắc chắn có đủ 3 cột mới mở rộng (IngredientsOrMaterials, PackagingSpecs, UsageAndStorage)
    /// bằng SqliteConnection độc lập và PRAGMA table_info('Products');
    /// Giúp bảo toàn dữ liệu sản phẩm cũ khi tải file SQLite từ Supabase Storage về.
    /// </summary>
    private static async Task EnsureProductColumnsAsync(EcommerceContext context)
    {
        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            // Giải phóng các pool trước để tránh database is locked
            SqliteConnection.ClearAllPools();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // 1. Kiểm tra bảng Products đã tồn tại chưa
            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Products';";
                var tableName = await checkCmd.ExecuteScalarAsync();
                if (tableName == null)
                {
                    Console.WriteLine("=== [SQLITE PRAGMA] Bảng Products chưa tồn tại trong SQLite, bỏ qua ALTER TABLE. ===");
                    return;
                }
            }

            // 2. Lấy danh sách các cột hiện có trong bảng Products bằng PRAGMA
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('Products');";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    existingColumns.Add(colName);
                }
            }

            Console.WriteLine($"=== [SQLITE PRAGMA] Bảng Products hiện có các cột: {string.Join(", ", existingColumns)} ===");

            // 3. Danh sách các cột cần kiểm tra và bổ sung bằng ALTER TABLE
            var requiredColumns = new (string Name, string Sql)[]
            {
                ("IngredientsOrMaterials", "ALTER TABLE Products ADD COLUMN IngredientsOrMaterials TEXT NULL;"),
                ("PackagingSpecs", "ALTER TABLE Products ADD COLUMN PackagingSpecs TEXT NULL;"),
                ("UsageAndStorage", "ALTER TABLE Products ADD COLUMN UsageAndStorage TEXT NULL;")
            };

            foreach (var col in requiredColumns)
            {
                if (!existingColumns.Contains(col.Name))
                {
                    try
                    {
                        using var alterCmd = connection.CreateCommand();
                        alterCmd.CommandText = col.Sql;
                        await alterCmd.ExecuteNonQueryAsync();
                        Console.WriteLine($"=== [SQLITE MIGRATION SUCCESS] Đã bổ sung thành công cột '{col.Name}' vào bảng Products! ===");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"=== [SQLITE MIGRATION ERROR] Lỗi khi thêm cột '{col.Name}' vào Products: {ex.Message} ===");
                    }
                }
                else
                {
                    Console.WriteLine($"=== [SQLITE CHECK] Cột '{col.Name}' đã tồn tại trong bảng Products. ===");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== [SQLITE CONNECTION ERROR] Lỗi khi kiểm tra cấu trúc bảng Products: {ex.Message} ===");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Đảm bảo bảng Coupons chắc chắn có đủ các cột mới mở rộng bằng SqliteConnection độc lập và PRAGMA table_info.
    /// Giúp tránh lỗi transaction EF Core và kiểu dữ liệu SQLite trên Render Cloud.
    /// </summary>
    private static async Task EnsureCouponColumnsAsync(EcommerceContext context)
    {
        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            // Giải phóng các pool trước để tránh database is locked
            SqliteConnection.ClearAllPools();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // 1. Kiểm tra bảng Coupons đã tồn tại chưa
            using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Coupons';";
                var tableName = await checkCmd.ExecuteScalarAsync();
                if (tableName == null)
                {
                    Console.WriteLine("=== [SQLITE PRAGMA] Bảng Coupons chưa tồn tại trong SQLite, bỏ qua ALTER TABLE. ===");
                    return;
                }
            }

            // 2. Lấy danh sách các cột hiện có trong bảng Coupons bằng PRAGMA
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('Coupons');";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    existingColumns.Add(colName);
                }
            }

            Console.WriteLine($"=== [SQLITE PRAGMA] Bảng Coupons hiện có các cột: {string.Join(", ", existingColumns)} ===");

            // 3. Danh sách các cột cần kiểm tra và bổ sung bằng ALTER TABLE (chuẩn kiểu dữ liệu SQLite)
            var requiredColumns = new (string Name, string Sql)[]
            {
                ("Description", "ALTER TABLE Coupons ADD COLUMN Description TEXT NULL;"),
                ("MinOrderValue", "ALTER TABLE Coupons ADD COLUMN MinOrderValue NUMERIC NOT NULL DEFAULT 0;"),
                ("MaxDiscountAmount", "ALTER TABLE Coupons ADD COLUMN MaxDiscountAmount NUMERIC NULL;"),
                ("StartDate", "ALTER TABLE Coupons ADD COLUMN StartDate TEXT NOT NULL DEFAULT '2026-01-01 00:00:00';"),
                ("EndDate", "ALTER TABLE Coupons ADD COLUMN EndDate TEXT NOT NULL DEFAULT '2027-01-01 00:00:00';"),
                ("UsageLimit", "ALTER TABLE Coupons ADD COLUMN UsageLimit INTEGER NOT NULL DEFAULT 100;"),
                ("UsedCount", "ALTER TABLE Coupons ADD COLUMN UsedCount INTEGER NOT NULL DEFAULT 0;"),
                ("EventTag", "ALTER TABLE Coupons ADD COLUMN EventTag TEXT NOT NULL DEFAULT 'Normal';")
            };

            foreach (var col in requiredColumns)
            {
                if (!existingColumns.Contains(col.Name))
                {
                    try
                    {
                        using var alterCmd = connection.CreateCommand();
                        alterCmd.CommandText = col.Sql;
                        await alterCmd.ExecuteNonQueryAsync();
                        Console.WriteLine($"=== [SQLITE MIGRATION SUCCESS] Đã bổ sung thành công cột '{col.Name}' vào bảng Coupons! ===");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"=== [SQLITE MIGRATION ERROR] Lỗi khi thêm cột '{col.Name}': {ex.Message} ===");
                    }
                }
                else
                {
                    Console.WriteLine($"=== [SQLITE CHECK] Cột '{col.Name}' đã tồn tại trong bảng Coupons. ===");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== [SQLITE CONNECTION ERROR] Lỗi khi kiểm tra cấu trúc bảng Coupons: {ex.Message} ===");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
