using EcommerceHue.Models;
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

        // 1. Đảm bảo cấu trúc các bảng đã được tạo sẵn
        if (context.Database.IsSqlite())
        {
            await context.Database.EnsureCreatedAsync();
        }
        else if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
        }

        // 2. Khởi tạo danh mục đặc sản Huế nếu chưa có
        if (!await context.Categories.AnyAsync())
        {
            var categories = new List<Category>
            {
                new()
                {
                    Name         = "Mè xửng",
                    Description  = "Mè xửng Huế – đặc sản truyền thống ngọt thanh, thơm bùi mè rang.",
                    ImageUrl     = CategoryImages["Mè xửng"],
                    DisplayOrder = 1,
                    IsActive     = true,
                    CreatedAt    = DateTime.UtcNow
                },
                new()
                {
                    Name         = "Tôm chua",
                    Description  = "Tôm chua Huế – hương vị cung đình lên men tự nhiên, đậm đà bản sắc cố đô.",
                    ImageUrl     = CategoryImages["Tôm chua"],
                    DisplayOrder = 2,
                    IsActive     = true,
                    CreatedAt    = DateTime.UtcNow
                },
                new()
                {
                    Name         = "Trà cung đình Huế",
                    Description  = "Trà cung đình Huế – thảo mộc hoàng gia quý phái, thanh nhiệt dưỡng sinh.",
                    ImageUrl     = CategoryImages["Trà cung đình Huế"],
                    DisplayOrder = 3,
                    IsActive     = true,
                    CreatedAt    = DateTime.UtcNow
                }
            };

            await context.Categories.AddRangeAsync(categories);
            await context.SaveChangesAsync();
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

        // 4. Khởi tạo tài khoản Khách hàng và Quản trị Admin mẫu nếu chưa có
        // Mật khẩu mặc định: 123456 (được băm bằng BCrypt theo quy tắc bảo mật)
        var defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword("123456");

        // Tài khoản Quản trị tối cao (Admin)
        if (!await context.Customers.AnyAsync(c => c.Email == "sadiomanehello@gmail.com"))
        {
            var adminCustomer = new Customer
            {
                Username     = "admin",
                PasswordHash = defaultPasswordHash,
                FullName     = "Quản Trị Viên (Admin)",
                Email        = "sadiomanehello@gmail.com",
                PhoneNumber  = "0901234567",
                Address      = "Thành phố Huế, Tỉnh Thừa Thiên Huế",
                CreatedAt    = DateTime.UtcNow
            };
            await context.Customers.AddAsync(adminCustomer);
        }

        // Tài khoản Khách hàng mua sắm
        if (!await context.Customers.AnyAsync(c => c.Email == "tminhniem@gmail.com"))
        {
            var testCustomer = new Customer
            {
                Username     = "tminhniem",
                PasswordHash = defaultPasswordHash,
                FullName     = "Thầy Thích Minh Niệm",
                Email        = "tminhniem@gmail.com",
                PhoneNumber  = "0909888999",
                Address      = "Cố Đô Huế, Việt Nam",
                CreatedAt    = DateTime.UtcNow
            };
            await context.Customers.AddAsync(testCustomer);
        }

        await context.SaveChangesAsync();

        // 5. Khởi tạo mã giảm giá mẫu nếu chưa có
        if (!await context.Coupons.AnyAsync(c => c.Code == "HUEWELCOME10"))
        {
            await context.Coupons.AddAsync(new Coupon
            {
                Code = "HUEWELCOME10",
                DiscountPercentage = 10,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
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
}
