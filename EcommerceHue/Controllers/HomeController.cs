using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EcommerceHue.Data;
using EcommerceHue.Models;
using EcommerceHue.Models.ViewModels;

namespace EcommerceHue.Controllers;

public class HomeController(EcommerceContext context) : Controller
{
    // =========================================================================
    // Action: Trang chủ – danh sách đặc sản Huế có tìm kiếm & lọc danh mục
    // =========================================================================

    /// <summary>
    /// Truy vấn danh mục và sản phẩm đặc sản Huế theo từ khóa tìm kiếm và ID danh mục.
    /// Dùng AsNoTracking để tối ưu hiệu năng đọc dữ liệu.
    /// </summary>
    /// <param name="searchString">Từ khóa tìm kiếm theo tên hoặc mô tả sản phẩm</param>
    /// <param name="categoryId">ID danh mục được chọn từ Sidebar để lọc</param>
    public async Task<IActionResult> Index(string? searchString, int? categoryId)
    {
        // 1. Lấy toàn bộ danh mục đang hoạt động để hiển thị Sidebar bên trái
        var categories = await context.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        // 2. Thống kê số lượng sản phẩm theo từng danh mục
        var categoryCounts = await context.Products
            .AsNoTracking()
            .Where(p => p.IsAvailable)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        var totalAllCount = await context.Products
            .AsNoTracking()
            .CountAsync(p => p.IsAvailable);

        // 3. Khởi tạo truy vấn lọc sản phẩm
        var query = context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsAvailable);

        // Lọc theo từ khóa tìm kiếm (nếu có)
        if (!string.IsNullOrWhiteSpace(searchString))
        {
            var search = searchString.Trim();
            query = query.Where(p => p.Name.Contains(search) 
                                  || (p.Description != null && p.Description.Contains(search)));
        }

        // Lọc theo ID danh mục (nếu có)
        if (categoryId.HasValue && categoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        var products = await query
            .OrderBy(p => p.Category!.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync();

        // 4. Thiết lập giá gốc khuyến mãi mẫu (OriginalPrice) cho một số sản phẩm
        // để kích hoạt hiển thị gạch ngang giá gốc kèm giá mới màu đỏ
        foreach (var product in products)
        {
            if (!product.OriginalPrice.HasValue && product.Id % 2 != 0)
            {
                // Làm tròn lên hàng nghìn (ví dụ giá 45k -> giá gốc 55k)
                product.OriginalPrice = Math.Round((product.Price * 1.25m) / 1000m) * 1000m;
            }
        }

        // Tên danh mục đang chọn (nếu có)
        var selectedCategoryName = categoryId.HasValue && categoryId.Value > 0
            ? categories.FirstOrDefault(c => c.Id == categoryId.Value)?.Name
            : null;

        var viewModel = new HomeIndexViewModel
        {
            Products               = products,
            Categories             = categories,
            CategoryProductCounts  = categoryCounts,
            SearchString           = searchString?.Trim(),
            SelectedCategoryId     = categoryId,
            SelectedCategoryName   = selectedCategoryName,
            TotalAllProductsCount  = totalAllCount
        };

        return View(viewModel);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    // =========================================================================
    // HƯỚNG DẪN & CHÍNH SÁCH (CÁC TRANG TĨNH CHÂN TRANG)
    // =========================================================================

    /// <summary>
    /// Hướng dẫn quy trình đặt mua đặc sản Huế trực tuyến.
    /// </summary>
    [HttpGet]
    public IActionResult HuongDanDatHang()
    {
        return View();
    }

    /// <summary>
    /// Hướng dẫn các hình thức thanh toán: cổng VnPay và COD khi nhận hàng.
    /// </summary>
    [HttpGet]
    public IActionResult HuongDanThanhToan()
    {
        return View();
    }

    /// <summary>
    /// Chính sách bảo mật thông tin cá nhân và giao dịch của khách hàng.
    /// </summary>
    [HttpGet]
    public IActionResult ChinhSachBaoMat()
    {
        return View();
    }

    /// <summary>
    /// Chính sách và quy định đổi trả, hoàn tiền đối với các mặt hàng đặc sản, thực phẩm Huế.
    /// </summary>
    [HttpGet]
    public IActionResult ChinhSachDoiTra()
    {
        return View();
    }

    // =========================================================================
    // NEWSLETTER: Đăng ký nhận bản tin ẩm thực & ưu đãi – POST /Home/RegisterNewsletter
    // =========================================================================

    /// <summary>
    /// Tiếp nhận email đăng ký nhận bản tin khuyến mãi & tin tức đặc sản Huế qua AJAX.
    /// Kiểm tra định dạng hợp lệ (validation) và trả về thông báo JSON.
    /// </summary>
    /// <param name="email">Địa chỉ email người dùng nhập</param>
    [HttpPost]
    public IActionResult RegisterNewsletter(string email)
    {
        // 1. Kiểm tra chuỗi email có trống hay không
        if (string.IsNullOrWhiteSpace(email))
        {
            return Json(new { success = false, message = "Vui lòng nhập địa chỉ email của bạn." });
        }

        var trimmedEmail = email.Trim();

        // 2. Kiểm tra định dạng email hợp lệ
        var emailChecker = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();
        if (!emailChecker.IsValid(trimmedEmail) || !trimmedEmail.Contains('@') || !trimmedEmail.Contains('.'))
        {
            return Json(new { success = false, message = "Địa chỉ email không đúng định dạng. Vui lòng kiểm tra lại." });
        }

        // 3. Trả về kết quả JSON thành công
        return Json(new
        {
            success = true,
            message = "Đăng ký nhận ưu đãi giảm 10% thành công! Vui lòng kiểm tra hộp thư Email của bạn."
        });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

