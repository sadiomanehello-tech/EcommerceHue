namespace EcommerceHue.Models.ViewModels;

/// <summary>
/// ViewModel cho trang chủ: hiển thị danh sách sản phẩm, thanh tìm kiếm và sidebar danh mục đặc sản Huế.
/// </summary>
public sealed class HomeIndexViewModel
{
    /// <summary>Danh sách sản phẩm sau khi lọc</summary>
    public IEnumerable<Product> Products { get; set; } = [];

    /// <summary>Toàn bộ danh mục để hiển thị Sidebar bên trái</summary>
    public IEnumerable<Category> Categories { get; set; } = [];

    /// <summary>Số lượng sản phẩm theo từng danh mục (Key: CategoryId, Value: Count)</summary>
    public Dictionary<int, int> CategoryProductCounts { get; set; } = [];

    /// <summary>Từ khóa tìm kiếm hiện tại</summary>
    public string? SearchString { get; set; }

    /// <summary>Id danh mục đang chọn để lọc (nếu có)</summary>
    public int? SelectedCategoryId { get; set; }

    /// <summary>Tên danh mục đang chọn (nếu có)</summary>
    public string? SelectedCategoryName { get; set; }

    /// <summary>Tổng số lượng sản phẩm toàn bộ hệ thống</summary>
    public int TotalAllProductsCount { get; set; }
}
