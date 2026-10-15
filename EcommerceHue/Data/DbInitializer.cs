using EcommerceHue.Models;
using Microsoft.EntityFrameworkCore;

namespace EcommerceHue.Data;

/// <summary>
/// Lớp khởi tạo dữ liệu mẫu (Seed Data) cho website đặc sản Huế.
/// Chỉ chèn dữ liệu khi bảng Categories và Products chưa có bản ghi nào.
/// Tuân thủ quy tắc 01-coding-style.md: mọi Product phải có StockQuantity và ExpiryDate.
/// </summary>
public static class DbInitializer
{
    // =========================================================================
    // BẢNG ÁNH XẠ URL ẢNH ONLINE
    // Nguồn: Unsplash (ảnh miễn phí) & Wikimedia Commons (ảnh thực tế xứ Huế)
    // Cập nhật: 2026-10-13 – thay toàn bộ photo ID cũ sang URL ổn định mới.
    // =========================================================================

    private static readonly Dictionary<string, string> ProductImages = new()
    {
        // Mè xửng lạc Phú Hương – ảnh kẹo hạt lạc vừng truyền thống (Unsplash)
        ["me-xung-lac-phu-huong"]        = "https://images.unsplash.com/photo-1772984960196-b387b4f2f524?w=400&h=300&fit=crop&q=80",

        // Mè xửng Thiên Hương – ảnh kẹo khối mè vừng đặc trưng (Unsplash)
        ["me-xung-thien-huong"]          = "https://images.unsplash.com/photo-1594100889583-a421e2b8265c?w=400&h=300&fit=crop&q=80",

        // Tôm chua Bà Đặng hũ 500g – ảnh đĩa tôm chua xứ Huế chuẩn vị (Wikimedia Commons)
        ["tom-chua-ba-dang-500g"]        = "https://upload.wikimedia.org/wikipedia/commons/c/c0/Mam_tom_chua_di_Can.jpg",

        // Tôm chua Trọng Đức – ảnh tôm muối chua mộc mạc (Unsplash)
        ["tom-chua-trong-duc"]           = "https://plus.unsplash.com/premium_photo-1709146097755-f5f9ba107de8?w=400&h=300&fit=crop&q=80",

        // Trà cung đình Huế hộp 100g – ảnh bình trà thảo mộc cung đình quý phái (Unsplash)
        ["tra-cung-dinh-hue-100g"]       = "https://images.unsplash.com/photo-1758899058841-d70f58437fe5?w=400&h=300&fit=crop&q=80",

        // Trà sen cung đình hộp 50 túi lọc – ảnh tách trà sen thanh mát (Unsplash)
        ["tra-sen-cung-dinh-50-tui-loc"] = "https://plus.unsplash.com/premium_photo-1674406481284-43eba097a291?w=400&h=300&fit=crop&q=80",
    };

    private static readonly Dictionary<string, string> CategoryImages = new()
    {
        // Danh mục Mè xửng – dùng ảnh kẹo lạc vừng đặc trưng
        ["Mè xửng"]           = "https://images.unsplash.com/photo-1772984960196-b387b4f2f524?w=400&h=300&fit=crop&q=80",

        // Danh mục Tôm chua – dùng ảnh đĩa tôm chua Huế từ Wikimedia
        ["Tôm chua"]          = "https://upload.wikimedia.org/wikipedia/commons/c/c0/Mam_tom_chua_di_Can.jpg",

        // Danh mục Trà cung đình Huế – dùng ảnh bình trà thảo mộc quý phái
        ["Trà cung đình Huế"] = "https://images.unsplash.com/photo-1758899058841-d70f58437fe5?w=400&h=300&fit=crop&q=80",
    };

    // =========================================================================
    // PHƯƠNG THỨC CÔNG KHAI
    // =========================================================================

    /// <summary>
    /// Phương thức khởi tạo chính – gọi từ Program.cs khi môi trường là Development.
    /// Nếu dữ liệu đã tồn tại, tự động vá URL ảnh cũ (đường dẫn nội bộ → URL online).
    /// </summary>
    /// <param name="context">DbContext được inject qua DI</param>
    public static async Task SeedAsync(EcommerceContext context)
    {
        // Đảm bảo database tồn tại và migration mới nhất đã được áp dụng
        await context.Database.MigrateAsync();

        // Khởi tạo bài viết Cẩm nang ẩm thực Huế (Blog Module)
        await SeedPostsAsync(context);

        // Khởi tạo mã giảm giá mẫu (HUEWELCOME10)
        await SeedCouponsAsync(context);

        // Nếu đã có dữ liệu danh mục & sản phẩm → chỉ vá URL ảnh nội bộ, không chèn lại toàn bộ
        if (await context.Categories.AnyAsync())
        {
            await PatchLocalImageUrlsAsync(context);
            return;
        }

        // ====================================================================
        // BƯỚC 1: Tạo danh mục đặc sản Huế
        // ====================================================================

        var categories = new List<Category>
        {
            new()
            {
                Name         = "Mè xửng",
                Description  = "Mè xửng Huế – đặc sản truyền thống nổi tiếng với vị ngọt thanh, thơm mùi mè rang.",
                ImageUrl     = CategoryImages["Mè xửng"],
                DisplayOrder = 1,
                IsActive     = true,
                CreatedAt    = DateTime.UtcNow
            },
            new()
            {
                Name         = "Tôm chua",
                Description  = "Tôm chua Huế – món ăn cung đình đặc trưng, lên men tự nhiên, đậm đà hương vị xứ Huế.",
                ImageUrl     = CategoryImages["Tôm chua"],
                DisplayOrder = 2,
                IsActive     = true,
                CreatedAt    = DateTime.UtcNow
            },
            new()
            {
                Name         = "Trà cung đình Huế",
                Description  = "Trà cung đình Huế – hương vị hoàng gia độc đáo, pha chế theo công thức bí truyền từ triều Nguyễn.",
                ImageUrl     = CategoryImages["Trà cung đình Huế"],
                DisplayOrder = 3,
                IsActive     = true,
                CreatedAt    = DateTime.UtcNow
            }
        };

        await context.Categories.AddRangeAsync(categories);
        // Lưu để lấy Id tự sinh của từng Category trước khi tạo Product
        await context.SaveChangesAsync();

        // Lấy Id sau khi lưu để đảm bảo FK hợp lệ
        var cateMe  = categories[0]; // Mè xửng
        var cateTom = categories[1]; // Tôm chua
        var cateTra = categories[2]; // Trà cung đình

        // ====================================================================
        // BƯỚC 2: Tạo sản phẩm mẫu
        // Bắt buộc StockQuantity và ExpiryDate (quy chuẩn 01-coding-style.md)
        // ====================================================================

        var products = new List<Product>
        {
            // ----------------------------------------------------------------
            // Nhóm: Mè xửng
            // ----------------------------------------------------------------
            new()
            {
                Name          = "Mè xửng Thiên Hương",
                Description   = "Mè xửng Thiên Hương – thương hiệu lâu đời tại Huế. Kẹo mè giòn tan, vị ngọt vừa phải, hương thơm đặc trưng của mè rang vàng.",
                Price         = 45_000m,
                ImageUrl      = ProductImages["me-xung-thien-huong"],
                Slug          = "me-xung-thien-huong",
                StockQuantity = 200,
                ExpiryDate    = new DateOnly(2027, 4, 13),
                IsAvailable   = true,
                CategoryId    = cateMe.Id,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            },
            new()
            {
                Name          = "Mè xửng lạc Phú Hương",
                Description   = "Mè xửng lạc Phú Hương – biến thể thêm lạc rang thơm béo, giữ nguyên công thức truyền thống Huế. Đóng hộp quà tiện lợi.",
                Price         = 55_000m,
                ImageUrl      = ProductImages["me-xung-lac-phu-huong"],
                Slug          = "me-xung-lac-phu-huong",
                StockQuantity = 150,
                ExpiryDate    = new DateOnly(2027, 3, 31),
                IsAvailable   = true,
                CategoryId    = cateMe.Id,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            },

            // ----------------------------------------------------------------
            // Nhóm: Tôm chua
            // ----------------------------------------------------------------
            new()
            {
                Name          = "Tôm chua Trọng Đức",
                Description   = "Tôm chua Trọng Đức – tôm sú tươi lên men 5–7 ngày cùng gia vị cung đình, màu đỏ tươi hấp dẫn. Ăn kèm thịt heo luộc và bánh tráng.",
                Price         = 120_000m,
                ImageUrl      = ProductImages["tom-chua-trong-duc"],
                Slug          = "tom-chua-trong-duc",
                StockQuantity = 80,
                ExpiryDate    = new DateOnly(2026, 11, 12),
                IsAvailable   = true,
                CategoryId    = cateTom.Id,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            },
            new()
            {
                Name          = "Tôm chua Bà Đặng hũ 500g",
                Description   = "Tôm chua Bà Đặng – thương hiệu gia đình 3 đời tại chợ Đông Ba. Hũ 500g phù hợp làm quà biếu, bảo quản lạnh dùng trong 3 tuần.",
                Price         = 155_000m,
                ImageUrl      = ProductImages["tom-chua-ba-dang-500g"],
                Slug          = "tom-chua-ba-dang-500g",
                StockQuantity = 60,
                ExpiryDate    = new DateOnly(2026, 11, 30),
                IsAvailable   = true,
                CategoryId    = cateTom.Id,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            },

            // ----------------------------------------------------------------
            // Nhóm: Trà cung đình Huế
            // ----------------------------------------------------------------
            new()
            {
                Name          = "Trà cung đình Huế hộp 100g",
                Description   = "Trà cung đình Huế pha chế từ búp trà xanh Bạch Mã, nhài, sâm, và các thảo dược bí truyền. Hộp giấy cao cấp 100g, phù hợp làm quà tặng.",
                Price         = 180_000m,
                ImageUrl      = ProductImages["tra-cung-dinh-hue-100g"],
                Slug          = "tra-cung-dinh-hue-100g",
                StockQuantity = 120,
                ExpiryDate    = new DateOnly(2028, 10, 13),
                IsAvailable   = true,
                CategoryId    = cateTra.Id,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            },
            new()
            {
                Name          = "Trà sen cung đình hộp 50 túi lọc",
                Description   = "Trà sen cung đình – ướp từ sen đầm hồ Tịnh Tâm, hương thơm thanh tao. Tiện dụng với 50 túi lọc, phù hợp dùng hàng ngày.",
                Price         = 95_000m,
                ImageUrl      = ProductImages["tra-sen-cung-dinh-50-tui-loc"],
                Slug          = "tra-sen-cung-dinh-50-tui-loc",
                StockQuantity = 90,
                ExpiryDate    = new DateOnly(2028, 6, 30),
                IsAvailable   = true,
                CategoryId    = cateTra.Id,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            }
        };

        await context.Products.AddRangeAsync(products);
        await context.SaveChangesAsync();
    }

    // =========================================================================
    // PHƯƠNG THỨC VÁ URL ẢNH NỘI BỘ → URL ONLINE
    // Chạy mỗi lần app khởi động nếu DB đã có dữ liệu.
    // Chỉ UPDATE những bản ghi có ImageUrl bắt đầu bằng "/images/" (đường dẫn cũ).
    // =========================================================================

    private static async Task PatchLocalImageUrlsAsync(EcommerceContext context)
    {
        var hasChanges = false;

        // --- Vá ảnh sản phẩm ---
        // Điều kiện: URL hiện tại là đường dẫn nội bộ (/images/) HOẶC là URL Unsplash/Wikimedia cũ
        // (khác với URL mới trong bảng ProductImages). Dùng Slug làm khóa tra cứu.
        var productsToFix = await context.Products
            .Where(p => p.Slug != null)
            .ToListAsync();

        foreach (var product in productsToFix)
        {
            if (product.Slug is null) continue;
            if (!ProductImages.TryGetValue(product.Slug, out var newUrl)) continue;

            // Chỉ cập nhật khi URL hiện tại khác URL mới (tránh SaveChanges thừa)
            if (product.ImageUrl == newUrl) continue;

            product.ImageUrl  = newUrl;
            product.UpdatedAt = DateTime.UtcNow;
            hasChanges        = true;
        }

        // --- Vá ảnh danh mục ---
        var categoriesToFix = await context.Categories.ToListAsync();

        foreach (var category in categoriesToFix)
        {
            if (!CategoryImages.TryGetValue(category.Name, out var newUrl)) continue;

            // Chỉ cập nhật khi URL hiện tại khác URL mới
            if (category.ImageUrl == newUrl) continue;

            category.ImageUrl = newUrl;
            hasChanges        = true;
        }

        if (hasChanges)
        {
            await context.SaveChangesAsync();
        }
    }

    // =========================================================================
    // KHỞI TẠO BÀI VIẾT CẨM NANG ẨM THỰC HUẾ (Blog Module)
    // =========================================================================

    /// <summary>
    /// Khởi tạo bài viết cẩm nang ẩm thực Huế mẫu nếu bảng Posts chưa có dữ liệu.
    /// </summary>
    private static async Task SeedPostsAsync(EcommerceContext context)
    {
        try
        {
            if (await context.Posts.AnyAsync())
                return;

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
                    Content = @"<p class=""lead text-muted"">Bún bò Huế từ lâu đã trở thành biểu tượng ẩm thực không thể thiếu khi nhắc về vùng đất cố đô cổ kính. Tô bún bò xứ Huế chuẩn vị luôn quyến rũ thực khách bởi làn khói nghi ngút, sắc đỏ óng ánh của sa tế ớt cay nồng, vị thơm thảo của củ sả tươi quyện cùng mắm ruốc truyền thống đậm đà khó quên.</p>
<hr class=""my-4"" />
<h3 class=""fw-bold text-dark mb-3"">1. Bún bò Mụ Rơi – Hương vị gia truyền hơn 30 năm</h3>
<p>Nằm nép mình trên con đường Nguyễn Du yên ả, quán bún bò Mụ Rơi chinh phục thực khách bằng nồi nước dùng ninh xương ống bò thanh ngọt tự nhiên, không lạm dụng bột ngọt. Thịt bắp hoa bò được thái lát mỏng, gân bò hầm vừa độ giòn dai sần sật ăn kèm rau sống hoa chuối tươi rói.</p>

<h3 class=""fw-bold text-dark mb-3"">2. Bún bò O Cương chú Điệp – Đậm chất phong vị Huế</h3>
<p>Tọa lạc tại đường Trần Thúc Nhẫn, quán nổi tiếng với sợi bún to mềm mượt đúng điệu Huế. Chả cua ở đây được quết tay tỉ mỉ từ thịt cua đồng tươi, cắn ngập răng cảm nhận trọn vẹn vị ngọt bùi và độ xốp tự nhiên.</p>

<h3 class=""fw-bold text-dark mb-3"">3. Bún bò Bà Tuyết – Quán ngon nức tiếng đường Nguyễn Công Trứ</h3>
<p>Một điểm hẹn quen thuộc của cả người dân địa phương lẫn du khách thập phương. Tô bún đầy đặn với tiết luộc mướt mềm, chả bò thơm giòn và miếng giò heo béo ngậy được chế biến kỹ lưỡng không hề có mùi tanh.</p>

<h3 class=""fw-bold text-dark mb-3"">4. Bún bò O Phượng – Điểm tâm sáng ấm lòng</h3>
<p>Tô bún tại quán O Phượng luôn giữ được độ nóng sốt đặc trưng, nước lèo trong veo nhưng vị lại vô cùng đằm thắm. Thêm chút chanh tươi và ớt ngâm nước mắm ruốc Huế là đủ đánh thức mọi giác quan.</p>

<h3 class=""fw-bold text-dark mb-3"">5. Bún bò Hẻm 17 Hùng Vương – Góc ẩm thực đêm khó quên</h3>
<p>Dành riêng cho những tâm hồn thích dạo chơi đêm Huế. Quán mở cửa đến tận khuya, là nơi lý tưởng để thưởng thức tô bún bò nóng hổi xua tan cái se lạnh của buổi tối xứ sông Hương núi Ngự.</p>"
                },
                new()
                {
                    Title = "Tré Huế – Thơm ngon ngất ngây, gợi nhớ hồi ức",
                    Slug = "tre-hue-thom-ngon-ngat-ngay-goi-nho-hoi-uc",
                    Summary = "Tré Huế mang hương vị cay nồng của ớt, thơm ấm của củ riềng và độ giòn sần sật của tai heo, là món mồi nhắm và đặc sản biếu tặng trứ danh xứ cố đô.",
                    ImageUrl = "https://images.unsplash.com/photo-1544025162-d76694265947?w=800&h=500&fit=crop&q=80",
                    CreatedDate = DateTime.UtcNow.AddDays(-2),
                    IsPublished = true,
                    Content = @"<p class=""lead text-muted"">Cùng với nem chua và chả lụa, tré Huế là món ăn truyền thống đặc sắc luôn xuất hiện trang trọng trong các mâm cỗ tiệc, ngày Tết và những dịp sum họp gia đình của người dân xứ Huế.</p>
<hr class=""my-4"" />
<h3 class=""fw-bold text-dark mb-3"">Đặc trưng độc đáo của món Tré xứ Huế</h3>
<p>Khác với tré miền Trung ở các vùng khác, tré Huế được làm chủ yếu từ thịt đầu heo, tai heo, mũi heo luộc chín vừa tới rồi thái chỉ thật mỏng để giữ độ giòn sần sật. Điểm làm nên linh hồn của tré chính là củ riềng thái sợi, mè rang vàng thơm phức, tỏi băm nhuyễn và bột thính gạo rang vàng óng ả.</p>

<h3 class=""fw-bold text-dark mb-3"">Quy trình bọc rơm kỳ công lưu giữ hương vị</h3>
<p>Sau khi trộn đều các nguyên liệu cùng nước mắm cốt cá cơm và tiêu sọ Phú Lộc, tré được gói chặt bằng lá ổi tươi để tạo hương thơm dịu nhẹ, bên ngoài bọc một lớp rơm nếp vàng óng được bó chặt hai đầu. Quá trình lên men tự nhiên trong 2-3 ngày giúp tré đạt vị chua thanh nhẹ, hòa quyện tuyệt vời với vị cay xè của ớt chỉ thiên.</p>

<h3 class=""fw-bold text-dark mb-3"">Thưởng thức Tré đúng phong cách Cố Đô</h3>
<p>Khi ăn, người Huế thường dùng đũa đánh tơi từng miếng tré, rắc thêm chút lá đinh lăng, tỏi củ thái lát mỏng và vài hạt đậu phộng rang. Nhâm nhi miếng tré giòn rụm bên chén rượu nồng đượm, bạn sẽ cảm nhận trọn vẹn sự tinh tế và chiều sâu của văn hóa ẩm thực cung đình Huế.</p>"
                }
            };

            await context.Posts.AddRangeAsync(posts);
            await context.SaveChangesAsync();
        }
        catch
        {
            // Bỏ qua nếu bảng chưa tồn tại trong CSDL khi chưa chạy migration
        }
    }

    /// <summary>
    /// Khởi tạo mã giảm giá mẫu (HUEWELCOME10 - Giảm 10% cho đơn hàng đầu tiên) nếu chưa có.
    /// </summary>
    private static async Task SeedCouponsAsync(EcommerceContext context)
    {
        try
        {
            if (!await context.Coupons.AnyAsync(c => c.Code == "HUEWELCOME10"))
            {
                await context.Coupons.AddAsync(new Coupon
                {
                    Code = "HUEWELCOME10",
                    DiscountPercentage = 10,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (!await context.Coupons.AnyAsync(c => c.Code == "HUEDACSAN10"))
            {
                await context.Coupons.AddAsync(new Coupon
                {
                    Code = "HUEDACSAN10",
                    DiscountPercentage = 10,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await context.SaveChangesAsync();
        }
        catch
        {
            // Bỏ qua nếu bảng chưa tồn tại trong CSDL khi chưa chạy migration
        }
    }
}
