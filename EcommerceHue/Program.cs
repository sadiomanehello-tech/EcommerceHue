using EcommerceHue.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// ĐĂNG KÝ DỊCH VỤ (Service Registration)
// ============================================================================

// --- Đăng ký EcommerceContext vào DI Container ---
// Connection string được đọc an toàn qua IConfiguration (quy tắc 02-security-vnpay.md).
// Giá trị thật lấy từ:
//   - appsettings.Development.json  khi ASPNETCORE_ENVIRONMENT=Development
//   - Biến môi trường               khi triển khai Production / Docker
//     (tên biến: ConnectionStrings__DefaultConnection)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Không tìm thấy connection string 'DefaultConnection'. " +
        "Vui lòng cấu hình trong appsettings.Development.json hoặc biến môi trường.");

builder.Services.AddDbContext<EcommerceContext>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        // Tự động retry khi kết nối SQL Server bị ngắt tạm thời (transient fault)
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
    });

    // Chỉ bật sensitive data logging ở môi trường Development
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

// --- MVC Controllers + Views ---
builder.Services.AddControllersWithViews();

// --- Đăng ký Dịch vụ gửi Email (MailKit / Gmail SMTP) ---
builder.Services.AddScoped<EcommerceHue.Services.IEmailService, EcommerceHue.Services.EmailService>();

// --- Cấu hình Cookie Authentication & Google OAuth (quy tắc 01-coding-style.md + 02-security-vnpay.md) ---
// Mọi thông tin nhạy cảm đọc từ IConfiguration, không hardcode.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Account/Login";         // Chuyển hướng khi chưa đăng nhập
        options.AccessDeniedPath = "/Account/AccessDenied";  // Chuyển hướng khi không đủ quyền
        options.LogoutPath       = "/Account/Logout";
        options.Cookie.Name      = ".EcommerceHue.Auth";
        options.Cookie.HttpOnly  = true;                     // Bảo vệ chống XSS
        options.Cookie.IsEssential = true;
        // Cookie persistent hết hạn sau 30 ngày (khi user chọn "Ghi nhớ đăng nhập")
        options.ExpireTimeSpan   = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;  // Tự gia hạn mỗi lần user hoạt động
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"]
            ?? throw new InvalidOperationException("Chưa cấu hình Authentication:Google:ClientId.");
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]
            ?? throw new InvalidOperationException("Chưa cấu hình Authentication:Google:ClientSecret.");
    });

// --- Bộ nhớ đệm phân tán (In-Memory) – cần thiết cho Session ---
// Dùng MemoryCache nội bộ cho môi trường Development.
// Trên Production có thể thay bằng Redis: builder.Services.AddStackExchangeRedisCache(...).
builder.Services.AddDistributedMemoryCache();

// --- Cấu hình Session (quy trình giỏ hàng) ---
builder.Services.AddSession(options =>
{
    // Session tự động hết hạn sau 30 phút không hoạt động
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    // Cookie chỉ đọc được từ server (bảo vệ XSS)
    options.Cookie.HttpOnly  = true;
    // Đánh dấu Essential để hoạt động ngay cả khi user chưa đồng ý cookie banner
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".EcommerceHue.Session";
});

// ============================================================================
// CẤU HÌNH PIPELINE HTTP (HTTP Request Pipeline)
// ============================================================================

var app = builder.Build();

// ============================================================================
// KHỞI TẠO DỮ LIỆU MẪU (Seed Data) – CHỈ CHẠY Ở MÔI TRƯỜNG DEVELOPMENT
// DbInitializer.SeedAsync kiểm tra DB trước khi chèn, an toàn khi restart nhiều lần.
// ============================================================================
if (app.Environment.IsDevelopment())
{
    // Tạo scope riêng để lấy Scoped service (EcommerceContext) đúng vòng đời DI
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<EcommerceContext>();
    await DbInitializer.SeedAsync(dbContext);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // HSTS mặc định 30 ngày – điều chỉnh cho Production khi cần
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// --- Kích hoạt Middleware Session ---
// Phải đặt SAU UseRouting() và TRƯỚC UseAuthorization() theo pipeline chuẩn ASP.NET Core.
app.UseSession();

// --- Kích hoạt Middleware Authentication ---
// PHẢI đặt TRƯỚC UseAuthorization() để [Authorize] hoạt động đúng.
app.UseAuthentication();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

