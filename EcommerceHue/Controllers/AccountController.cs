using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Extensions;
using EcommerceHue.Models;
using EcommerceHue.Models.ViewModels;
using EcommerceHue.Services;

namespace EcommerceHue.Controllers;

/// <summary>
/// Xử lý luồng xác thực thành viên: Đăng ký, Đăng nhập, Đăng xuất.
/// Mật khẩu luôn được băm bằng BCrypt trước khi lưu (quy tắc 02-security-vnpay.md).
/// </summary>
public class AccountController(
    EcommerceContext context,
    ILogger<AccountController> logger,
    IEmailService emailService) : Controller
{
    private readonly IEmailService _emailService = emailService;

    // =========================================================================
    // ĐĂNG KÝ – GET /Account/Register
    // =========================================================================

    [HttpGet]
    public IActionResult Register()
    {
        // Nếu đã đăng nhập thì không cần đăng ký nữa
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View();
    }

    // =========================================================================
    // ĐĂNG KÝ – POST /Account/Register
    // =========================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var email = model.Email.Trim().ToLowerInvariant();
        var phone = model.PhoneNumber.Trim();

        // Kiểm tra email đã tồn tại chưa
        var emailExists = await context.Customers
            .AnyAsync(c => c.Email == email);

        if (emailExists)
        {
            ModelState.AddModelError(nameof(model.Email), "Email này đã được sử dụng. Vui lòng đăng nhập hoặc dùng email khác.");
            return View(model);
        }

        // Kiểm tra số điện thoại đã tồn tại chưa
        var phoneExists = await context.Customers
            .AnyAsync(c => c.PhoneNumber == phone);

        if (phoneExists)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Số điện thoại này đã được sử dụng.");
            return View(model);
        }

        // Tự động lấy phần chuỗi trước ký tự '@' của Email làm Username ngầm định
        var atIndex = email.IndexOf('@');
        var baseUsername = atIndex > 0 ? email[..atIndex] : email;
        if (string.IsNullOrWhiteSpace(baseUsername))
        {
            baseUsername = "user";
        }
        if (baseUsername.Length > 40)
        {
            baseUsername = baseUsername[..40];
        }

        // Đảm bảo Username không trùng lặp trong cơ sở dữ liệu
        var uniqueUsername = baseUsername;
        if (await context.Customers.AnyAsync(c => c.Username == uniqueUsername))
        {
            uniqueUsername = $"{baseUsername}_{Random.Shared.Next(1000, 9999)}";
        }

        // Băm mật khẩu bằng BCrypt – TUYỆT ĐỐI KHÔNG lưu plaintext (quy tắc 02-security-vnpay.md)
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);

        var customer = new Customer
        {
            Username     = uniqueUsername,       // Username ngầm định phát sinh từ Email
            PasswordHash = passwordHash,
            FullName     = model.FullName.Trim(),
            Email        = email,
            PhoneNumber  = phone,
            Address      = model.Address.Trim(),
            CreatedAt    = DateTime.UtcNow
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Khách hàng mới đăng ký thành công: {Username} (Email: {Email}, SĐT: {Phone}, Id: {CustomerId})",
            customer.Username, customer.Email, customer.PhoneNumber, customer.Id);

        // Tự động đăng nhập sau khi đăng ký thành công
        await SignInCustomerAsync(customer, isPersistent: false);

        TempData["Success"] = $"🎉 Chào mừng {customer.FullName} đến với Đặc sản Huế!";
        return RedirectToAction("Index", "Home");
    }

    // =========================================================================
    // ĐĂNG NHẬP – GET /Account/Login
    // =========================================================================

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        // Nếu đã đăng nhập thì chuyển về trang chủ
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    // =========================================================================
    // ĐĂNG NHẬP – POST /Account/Login
    // =========================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        // Chuẩn hóa input đầu vào (Email hoặc Số điện thoại) theo phong cách Shopee/Lazada
        var rawInput = model.EmailOrPhone?.Trim() ?? string.Empty;
        var normalizedInput = rawInput.ToLowerInvariant();

        // =====================================================================
        // HÀNG RÀO BẢO MẬT (Security Interceptor):
        // Chặn đăng nhập bằng mật khẩu đối với tài khoản Admin tối cao.
        // Cưỡng chế chỉ đăng nhập độc quyền qua Google Login.
        // =====================================================================
        if (!string.IsNullOrEmpty(rawInput) && (normalizedInput == "sadiomanehello@gmail.com" || normalizedInput == "admin"))
        {
            ModelState.AddModelError(string.Empty, "Tài khoản Quản trị viên (Admin) đã được kích hoạt chế độ bảo mật tối cao: Không chấp nhận đăng nhập bằng mật khẩu thường. Vui lòng nhấn nút 'Đăng nhập bằng Google' ở phía dưới!");
            return View(model);
        }

        // Tìm khách hàng trong cơ sở dữ liệu theo Email HOẶC Số điện thoại
        var customer = await context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Email == normalizedInput || c.PhoneNumber == rawInput);

        // Debug log: phân biệt rõ "không tìm thấy user" với "đúng user nhưng sai mật khẩu"
        if (customer is null)
        {
            logger.LogWarning(
                "[Login] Không tìm thấy tài khoản với Email/SĐT: '{Input}'",
                rawInput);

            ModelState.AddModelError(string.Empty, "Email/Số điện thoại hoặc mật khẩu không chính xác.");
            return View(model);
        }

        // Xác thực mật khẩu bằng BCrypt.Verify – KHÔNG so sánh == trực tiếp
        var passwordValid = BCrypt.Net.BCrypt.Verify(model.Password, customer.PasswordHash);

        if (!passwordValid)
        {
            logger.LogWarning(
                "[Login] Sai mật khẩu cho tài khoản: '{Email}' (Id: {CustomerId})",
                customer.Email, customer.Id);

            ModelState.AddModelError(string.Empty, "Email/Số điện thoại hoặc mật khẩu không chính xác.");
            return View(model);
        }

        // Đăng nhập thành công – tạo Cookie Authentication
        await SignInCustomerAsync(customer, model.RememberMe);

        logger.LogInformation(
            "Khách hàng đăng nhập thành công: {Email} (Username: {Username}, Id: {CustomerId})",
            customer.Email, customer.Username, customer.Id);

        // Chuyển hướng về ReturnUrl (nếu có) hoặc trang chủ
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Index", "Home");
    }

    // =========================================================================
    // ĐĂNG NHẬP GOOGLE OAUTH – GET /Account/GoogleLogin
    // Kích hoạt luồng ChallengeAsync chuyển hướng sang cổng Google OAuth 2.0
    // =========================================================================

    [HttpGet]
    public async Task GoogleLogin(string? returnUrl = null)
    {
        // Nhận diện cấu trúc scheme bảo mật (ưu tiên https từ Forwarded Headers của ngrok / proxy)
        var scheme = Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && !string.IsNullOrEmpty(proto)
            ? proto.ToString()
            : Request.Scheme;

        var redirectUrl = Url.Action(nameof(GoogleResponse), "Account", new { returnUrl }, scheme);
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        await HttpContext.ChallengeAsync(GoogleDefaults.AuthenticationScheme, properties);
    }

    // =========================================================================
    // TIẾP NHẬN PHẢN HỒI GOOGLE – GET /Account/GoogleResponse
    // Trích xuất Email, Họ tên và tự động đồng bộ tài khoản khách hàng
    // =========================================================================

    [HttpGet]
    public async Task<IActionResult> GoogleResponse(string? returnUrl = null)
    {
        // 1. Tiếp nhận kết quả xác thực từ Google
        var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = authResult.Principal ?? User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            var googleResult = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
            principal = googleResult.Principal;
        }

        if (principal?.Identity?.IsAuthenticated != true)
        {
            logger.LogWarning("[GoogleAuth] Không nhận được thông tin xác thực hợp lệ từ Google.");
            TempData["Error"] = "Đăng nhập bằng Google không thành công. Vui lòng thử lại.";
            return RedirectToAction(nameof(Login));
        }

        // 2. Trích xuất thông tin: Email, FullName
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var fullName = principal.FindFirstValue(ClaimTypes.Name)
                    ?? principal.FindFirstValue("name")
                    ?? email;

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning("[GoogleAuth] Google không cung cấp trường Email của người dùng.");
            TempData["Error"] = "Không thể lấy thông tin Email từ tài khoản Google của bạn.";
            return RedirectToAction(nameof(Login));
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();

        // 3. Kiểm tra trong database bảng Customers
        var customer = await context.Customers
            .FirstOrDefaultAsync(c => c.Email == normalizedEmail);

        if (customer is null)
        {
            // Nếu Email chưa tồn tại: Tự động đăng ký tài khoản khách hàng mới ngầm định
            // (Username lấy theo Email, mật khẩu để trống vì xác thực hoàn toàn qua Google)
            var baseUsername = normalizedEmail.Length > 50 ? normalizedEmail[..50] : normalizedEmail;
            var uniqueUsername = baseUsername;

            // Đảm bảo không trùng Username với tài khoản tạo thủ công
            if (await context.Customers.AnyAsync(c => c.Username == uniqueUsername))
            {
                var suffix = Random.Shared.Next(1000, 9999).ToString();
                var prefixLength = Math.Min(uniqueUsername.Length, 45);
                uniqueUsername = $"{uniqueUsername[..prefixLength]}_{suffix}";
            }

            customer = new Customer
            {
                Username     = uniqueUsername,
                PasswordHash = string.Empty, // Mật khẩu để trống vì xác thực qua Google
                FullName     = string.IsNullOrWhiteSpace(fullName) ? uniqueUsername : fullName.Trim(),
                Email        = normalizedEmail,
                PhoneNumber  = string.Empty,
                Address      = string.Empty,
                CreatedAt    = DateTime.UtcNow
            };

            context.Customers.Add(customer);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "[GoogleAuth] Tự động tạo khách hàng mới qua Google: {Username} (Email: {Email}, Id: {CustomerId})",
                customer.Username, customer.Email, customer.Id);
        }
        else
        {
            logger.LogInformation(
                "[GoogleAuth] Khách hàng Google đã tồn tại trong hệ thống: {Username} (Email: {Email}, Id: {CustomerId})",
                customer.Username, customer.Email, customer.Id);
        }

        // 4. Xóa session/cookie Google thô và cấp Cookie hệ thống chuẩn hóa
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await SignInCustomerAsync(customer, isPersistent: true);

        // 5. Nếu là tài khoản Quản trị viên tối cao sadiomanehello@gmail.com: Vào thẳng /Admin/Orders
        if (normalizedEmail == "sadiomanehello@gmail.com")
        {
            TempData["Success"] = "🛡️ Chào mừng Quản trị viên tối cao đã xác thực thành công qua Google OAuth!";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return Redirect("/Admin/Orders");
        }

        TempData["Success"] = $"🎉 Chào mừng {customer.FullName} đã đăng nhập thành công bằng Google!";

        // 5. Điều hướng người dùng quay trở lại trang Giỏ hàng hoặc Checkout mượt mà
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        // Nếu giỏ hàng đang có sản phẩm thì ưu tiên chuyển thẳng đến trang Checkout
        var cart = HttpContext.Session.GetObject<List<CartItem>>("ShoppingCart") ?? [];
        if (cart.Count > 0)
        {
            return RedirectToAction("Checkout", "Cart");
        }

        return RedirectToAction("Index", "Home");
    }

    // =========================================================================
    // ĐĂNG XUẤT – POST /Account/Logout
    // =========================================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name;

        // Xóa Cookie xác thực
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Xóa sạch trạng thái phiên Session phân quyền
        HttpContext.Session.Remove("IsAdmin");
        HttpContext.Session.Remove("IsShipper");
        HttpContext.Session.Remove("Role");

        logger.LogInformation("Khách hàng đã đăng xuất: {Username}", username);

        TempData["Success"] = "Bạn đã đăng xuất thành công. Hẹn gặp lại!";
        return RedirectToAction("Index", "Home");
    }

    // =========================================================================
    // TRUY CẬP BỊ TỪ CHỐI – GET /Account/AccessDenied
    // =========================================================================

    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    // =========================================================================
    // HELPER: Tạo ClaimsIdentity và ghi Cookie xác thực
    // =========================================================================

    /// <summary>
    /// Tạo danh sách Claims chứa thông tin cá nhân và gọi SignInAsync
    /// để ghi Cookie xác thực cho khách hàng, đồng thời cấp Role phân quyền RBAC.
    /// </summary>
    private async Task SignInCustomerAsync(Customer customer, bool isPersistent)
    {
        // Xác định Role phân quyền hệ thống (RBAC):
        // 1. Admin: Nếu email hoặc username chứa 'admin' hoặc email chủ cửa hàng
        // 2. Shipper: Nếu email hoặc username chứa 'shipper'
        // 3. Customer: Khách hàng mua sắm thông thường
        string role = "Customer";
        var lowerEmail = (customer.Email ?? string.Empty).ToLowerInvariant();
        var lowerUsername = (customer.Username ?? string.Empty).ToLowerInvariant();

        if (lowerEmail.Contains("admin") || lowerUsername.Contains("admin") || lowerEmail == "sadiomanehello@gmail.com")
        {
            role = "Admin";
        }
        else if (lowerEmail.Contains("shipper") || lowerUsername.Contains("shipper"))
        {
            role = "Shipper";
        }

        var claims = new List<Claim>
        {
            // Claims chuẩn
            new(ClaimTypes.NameIdentifier, customer.Id.ToString()),
            new(ClaimTypes.Name,           customer.Username ?? string.Empty),

            // Claim phân quyền bảo mật quan trọng (Role-Based Access Control)
            new(ClaimTypes.Role,           role),

            // Claims thông tin cá nhân – dùng để tự động điền form Checkout
            new("FullName",    customer.FullName ?? string.Empty),
            new(ClaimTypes.Email,        customer.Email ?? string.Empty),
            new("PhoneNumber", customer.PhoneNumber ?? string.Empty),
            new("Address",     customer.Address ?? string.Empty)
        };

        // Lưu thông tin Role vào Session dự phòng
        if (role == "Admin")
        {
            HttpContext.Session.SetString("IsAdmin", "true");
            HttpContext.Session.SetString("Role", "Admin");
        }
        else if (role == "Shipper")
        {
            HttpContext.Session.SetString("IsShipper", "true");
            HttpContext.Session.SetString("Role", "Shipper");
        }
        else
        {
            HttpContext.Session.Remove("IsAdmin");
            HttpContext.Session.Remove("IsShipper");
            HttpContext.Session.SetString("Role", "Customer");
        }

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                // Ghi nhớ đăng nhập nếu user chọn checkbox "Ghi nhớ"
                IsPersistent = isPersistent,
                // Cookie hết hạn sau 30 ngày nếu persistent, hoặc theo session nếu không
                ExpiresUtc = isPersistent
                    ? DateTimeOffset.UtcNow.AddDays(30)
                    : null
            });
    }

    // =========================================================================
    // NEWSLETTER: Đăng ký nhận bản tin & Gửi mã giảm giá – POST /Account/SubscribeNewsletter
    // =========================================================================

    /// <summary>
    /// Tiếp nhận email đăng ký nhận bản tin, gửi email quà tặng chào mừng kèm mã giảm giá 10% (HUEDACSAN10).
    /// </summary>
    /// <param name="email">Địa chỉ email của khách hàng</param>
    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SubscribeNewsletter(string email)
    {
        // 1. Kiểm tra nếu chuỗi email rỗng hoặc sai định dạng
        if (string.IsNullOrWhiteSpace(email))
        {
            return Json(new { success = false, message = "Vui lòng nhập địa chỉ email của bạn." });
        }

        var cleanEmail = email.Trim();
        var emailChecker = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();
        if (!emailChecker.IsValid(cleanEmail) || !cleanEmail.Contains('@') || !cleanEmail.Contains('.'))
        {
            return Json(new { success = false, message = "Địa chỉ email không đúng định dạng. Vui lòng kiểm tra lại." });
        }

        // 2. Định nghĩa mã giảm giá cố định dành riêng cho đơn đầu tiên
        string couponCode = "HUEDACSAN10";

        // Tự động đảm bảo mã coupon tồn tại trong CSDL và đang hoạt động
        try
        {
            var existingCoupon = await context.Coupons.FirstOrDefaultAsync(c => c.Code == couponCode);
            if (existingCoupon == null)
            {
                await context.Coupons.AddAsync(new Coupon
                {
                    Code = couponCode,
                    DiscountPercentage = 10,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Newsletter] Không thể kiểm tra hoặc tạo coupon {CouponCode} trong CSDL", couponCode);
        }

        // 3. Biên soạn mẫu thư HTML cực kỳ trang trọng và ấm áp
        string subject = "[EcommerceHue] Quà tặng chào mừng - Nhận ngay mã giảm giá 10% cho đơn hàng đầu tiên";
        string htmlContent = $"""
        <!DOCTYPE html>
        <html lang="vi">
        <head>
            <meta charset="UTF-8">
            <title>{subject}</title>
        </head>
        <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f7f5f0; margin: 0; padding: 25px; color: #333; line-height: 1.6;">
            <div style="max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 18px rgba(0,0,0,0.08); border: 1px solid #ebdccb;">
                
                <!-- HEADER PHONG CÁCH CỐ ĐÔ HUẾ -->
                <div style="background: linear-gradient(135deg, #6b200c 0%, #9e2a2b 100%); padding: 32px 25px; text-align: center; color: #ffffff;">
                    <div style="font-size: 38px; margin-bottom: 8px;">🏮</div>
                    <h1 style="margin: 0; font-size: 22px; font-weight: bold; letter-spacing: 0.5px; text-transform: uppercase;">Quà Tặng Tri Ân Đăng Ký Bản Tin</h1>
                    <p style="margin: 8px 0 0 0; font-size: 14px; color: #f5d6ba;">Đặc Sản Cố Đô Huế - EcommerceHue</p>
                </div>

                <!-- NỘI DUNG CHÍNH -->
                <div style="padding: 30px 28px;">
                    <h2 style="color: #6b200c; font-size: 18px; margin-top: 0; margin-bottom: 14px;">Kính chào Quý khách,</h2>
                    
                    <p style="margin: 0 0 16px 0; font-size: 15px; color: #444;">
                        Chào mừng Quý khách đã đăng ký nhận bản tin tinh hoa ẩm thực xứ Huế từ <strong>EcommerceHue</strong>. Chúng tôi rất vinh hạnh được đồng hành cùng Quý khách trên hành trình khám phá những hương vị truyền thống đậm đà bản sắc Cố Đô.
                    </p>

                    <p style="margin: 0 0 20px 0; font-size: 15px; color: #444;">
                        Như một lời chào mừng nồng ấm, chúng tôi trân trọng gửi tặng riêng Quý khách mã ưu đãi đặc quyền cho đơn hàng đầu tiên:
                    </p>

                    <!-- HỘP HIỂN THỊ MÃ GIẢM GIÁ -->
                    <div style="background: linear-gradient(135deg, #fff8f0 0%, #fef3e2 100%); border: 2px dashed #d97706; border-radius: 10px; padding: 22px; text-align: center; margin: 25px 0;">
                        <div style="font-size: 13px; text-transform: uppercase; color: #92400e; font-weight: 600; letter-spacing: 1px;">Mã giảm giá ưu đãi dành riêng cho Quý khách</div>
                        <div style="font-size: 34px; font-weight: 800; color: #b45309; letter-spacing: 3px; margin: 12px 0; font-family: monospace;">
                            {couponCode}
                        </div>
                        <div style="font-size: 14px; color: #78350f; font-weight: 600;">
                            🎉 Giảm ngay <strong>10%</strong> tổng giá trị hóa đơn khi đặt mua bất kỳ đặc sản Huế nào!
                        </div>
                    </div>

                    <!-- HƯỚNG DẪN NHẬP MÃ -->
                    <div style="background-color: #fdfbf7; border-left: 4px solid #b45309; border-radius: 6px; padding: 16px 18px; margin-bottom: 24px;">
                        <h3 style="margin: 0 0 10px 0; font-size: 15px; color: #78350f; font-weight: 700;">📌 Hướng dẫn sử dụng mã tại giỏ hàng:</h3>
                        <ol style="margin: 0; padding-left: 20px; font-size: 14px; color: #555; line-height: 1.8;">
                            <li>Lựa chọn các thức quà tinh hoa xứ Huế: <em>kẹo Mè xửng giòn/dẻo, Tôm chua truyền thống, Trà sen Cung đình, Hạt sen tươi, Nem tré Đông Ba...</em></li>
                            <li>Bấm vào trang <strong>Giỏ hàng</strong> hoặc <strong>Đặt hàng & Thanh toán</strong>.</li>
                            <li>Nhấp vào ô <em>"Bạn có mã giảm giá?"</em>, nhập chính xác mã: <strong style="color: #b45309;">{couponCode}</strong> và chọn <strong>Áp dụng</strong>.</li>
                            <li>Số tiền giảm 10% sẽ được khấu trừ ngay lập tức vào tổng thanh toán của Quý khách.</li>
                        </ol>
                    </div>

                    <p style="margin: 0 0 10px 0; font-size: 14px; color: #666;">
                        Kính chúc Quý khách có những trải nghiệm ẩm thực Cố Đô thật trọn vẹn và ngon miệng!
                    </p>
                </div>

                <!-- FOOTER -->
                <div style="background-color: #f5f0e8; padding: 18px 25px; text-align: center; font-size: 13px; color: #777; border-top: 1px solid #ebdccb;">
                    <div><strong>EcommerceHue - Tinh Hoa Đặc Sản Cố Đô</strong></div>
                    <div style="margin-top: 4px;">Hotline: 0901 234 567 | Cổng chính Chợ Đông Ba, Đường Trần Hưng Đạo, TP. Huế</div>
                </div>

            </div>
        </body>
        </html>
        """;

        // 4. Gọi dịch vụ gửi thư bọc trong khối try-catch an toàn
        try
        {
            await _emailService.SendOrderEmailAsync(cleanEmail, subject, htmlContent);
            logger.LogInformation("[Newsletter] Đã gửi mã giảm giá {CouponCode} thành công tới {Email}", couponCode, cleanEmail);
        }
        catch (Exception ex)
        {
            Console.WriteLine("=== LỖI SMTP GỬI MAIL NEWSLETTER: " + ex.Message);
            logger.LogError(ex, "[Newsletter] Lỗi khi gửi email bản tin tới {Email}", cleanEmail);
        }

        // 5. Trả về kết quả JSON thành công
        return Json(new { success = true, message = "Đăng ký thành công! Vui lòng kiểm tra hộp thư Gmail để nhận mã giảm giá 10%." });
    }

    // =========================================================================
    // MÀN HÌNH KHÓA STAGING NỘI BỘ – GET /Account/StagingLogin
    // =========================================================================

    [HttpGet]
    [AllowAnonymous]
    public IActionResult StagingLogin()
    {
        // Nếu đã xác thực Staging rồi thì chuyển hướng về Trang chủ
        if (HttpContext.Session.GetString("Staging_Authenticated") == "true")
        {
            return RedirectToAction("Index", "Home");
        }

        return View();
    }

    // =========================================================================
    // XÁC THỰC MÃ PIN / MẬT KHẨU STAGING – POST /Account/StagingLogin
    // =========================================================================

    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public IActionResult StagingLogin(string accessPassword)
    {
        if (!string.IsNullOrEmpty(accessPassword) && accessPassword.Trim() == "DoAnDiem10")
        {
            HttpContext.Session.SetString("Staging_Authenticated", "true");
            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError("", "Mật khẩu truy cập nội bộ chưa chính xác!");
        ViewBag.ErrorMessage = "Mật khẩu truy cập nội bộ chưa chính xác!";
        return View();
    }
}

