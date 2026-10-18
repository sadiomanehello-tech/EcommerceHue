using EcommerceHue.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
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
    ?? "Data Source=ecommercehue.db";

builder.Services.AddDbContext<EcommerceContext>(options =>
{
    options.UseSqlite(connectionString);
    options.ConfigureWarnings(warnings => 
        warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

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

// --- Đăng ký PayOS SDK (Open Banking) ---
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var clientId = config["PayOS:ClientId"] ?? throw new InvalidOperationException("Chưa cấu hình PayOS:ClientId");
    var apiKey = config["PayOS:ApiKey"] ?? throw new InvalidOperationException("Chưa cấu hình PayOS:ApiKey");
    var checksumKey = config["PayOS:ChecksumKey"] ?? throw new InvalidOperationException("Chưa cấu hình PayOS:ChecksumKey");
    return new Net.payOS.PayOS(clientId, apiKey, checksumKey);
});

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
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
    });

// --- Cấu hình nhận diện Header chuyển tiếp (Forwarded Headers) cho ngrok / Reverse Proxy ---
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
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

// --- Kích hoạt Forwarded Headers Middleware ở hàng đầu tiên (trước mọi Middleware khác) ---
app.UseForwardedHeaders();

// --- Bước 2: Kích hoạt Middleware Session ngay sau dòng builder.Build() ---
app.UseSession();

// --- Bước 3: Nhúng bộ lọc Middleware cưỡng chế khóa toàn trang, bẫy từ khóa ngoại lệ Webhook payOS ---
app.Use(async (context, next) =>
{
    string path = context.Request.Path.Value?.ToLower() ?? string.Empty;

    // ĐỤC LỖ NGOẠI LỆ: Cho phép payOS gọi API gạch nợ tự do không bị chặn mật khẩu
    if (path.Contains("/payos-webhook"))
    {
        await next();
        return;
    }

    // Cho phép đi tiếp nếu truy cập vào trang nhập Passcode hoặc các tệp tĩnh CSS/JS giao diện
    if (path.Contains("/account/staginglogin") || 
        path.StartsWith("/css") || 
        path.StartsWith("/js") || 
        path.StartsWith("/lib") || 
        path.Contains("/images") ||
        path.Contains("/favicon.ico"))
    {
        await next();
        return;
    }

    // Kiểm tra nếu chưa nhập Passcode "DoAnDiem10", lập tức đá văng về trang khóa lồng đèn
    if (context.Session.GetString("Staging_Authenticated") == null)
    {
        context.Response.Redirect("/Account/StagingLogin");
        return;
    }

    await next();
});

// ============================================================================
// TỰ ĐỘNG CẬP NHẬT DATABASE & SEED DỮ LIỆU (Migration Automation Check)
// Tự động kiểm tra và vẽ cấu trúc bảng (Products, Orders, Customers...) lên Cloud DB
// ============================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await SeedData.Initialize(services);
        Console.WriteLine("=== [SEED DATA] Đã nạp thành công 6 sản phẩm và tài khoản mẫu vào SQLite! ===");
    }
    catch (Exception ex)
    {
        Console.WriteLine("=== [SEED DATA LỖI]: " + ex.Message);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // HSTS mặc định 30 ngày – điều chỉnh cho Production khi cần
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

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

