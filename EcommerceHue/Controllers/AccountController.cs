using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Extensions;
using EcommerceHue.Models;
using EcommerceHue.Models.ViewModels;

namespace EcommerceHue.Controllers;

/// <summary>
/// Xử lý luồng xác thực thành viên: Đăng ký, Đăng nhập, Đăng xuất.
/// Mật khẩu luôn được băm bằng BCrypt trước khi lưu (quy tắc 02-security-vnpay.md).
/// </summary>
public class AccountController(EcommerceContext context, ILogger<AccountController> logger) : Controller
{
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
        var rawInput = model.EmailOrPhone.Trim();
        var normalizedInput = rawInput.ToLowerInvariant();

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
        var redirectUrl = Url.Action(nameof(GoogleResponse), "Account", new { returnUrl });
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
    /// để ghi Cookie xác thực cho khách hàng.
    /// </summary>
    private async Task SignInCustomerAsync(Customer customer, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            // Claims chuẩn
            new(ClaimTypes.NameIdentifier, customer.Id.ToString()),
            new(ClaimTypes.Name,           customer.Username),

            // Claims thông tin cá nhân – dùng để tự động điền form Checkout
            new("FullName",    customer.FullName),
            new(ClaimTypes.Email,        customer.Email),
            new("PhoneNumber", customer.PhoneNumber),
            new("Address",     customer.Address)
        };

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
}
