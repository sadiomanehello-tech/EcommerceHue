using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace EcommerceHue.Extensions;

/// <summary>
/// Thư viện hỗ trợ tích hợp cổng thanh toán trực tuyến VnPay.
/// Tuân thủ quy chuẩn bảo mật 02-security-vnpay.md:
/// - Sử dụng thuật toán HMACSHA512 để sinh và kiểm định chữ ký bảo mật.
/// - Sắp xếp tham số theo thứ tự ASCII Ordinal chuẩn tài liệu kỹ thuật VnPay.
/// </summary>
public sealed class VnPayLibrary
{
    private readonly SortedList<string, string> _requestData = new(new VnPayCompare());
    private readonly SortedList<string, string> _responseData = new(new VnPayCompare());

    /// <summary>
    /// Thêm tham số gửi sang VnPay (bỏ qua nếu giá trị null hoặc khoảng trắng).
    /// </summary>
    public void AddRequestData(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _requestData[key] = value;
        }
    }

    /// <summary>
    /// Thêm tham số nhận về từ VnPay phản hồi (Query parameters).
    /// </summary>
    public void AddResponseData(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _responseData[key] = value;
        }
    }

    /// <summary>
    /// Lấy giá trị tham số nhận về từ VnPay theo key.
    /// </summary>
    public string GetResponseData(string key)
    {
        return _responseData.TryGetValue(key, out var retValue) ? retValue : string.Empty;
    }

    /// <summary>
    /// Sinh chuỗi liên kết thanh toán sang VnPay Sandbox kèm chữ ký bảo mật HMACSHA512.
    /// </summary>
    /// <param name="baseUrl">Địa chỉ cổng thanh toán VnPay</param>
    /// <param name="vnpHashSecret">Khóa bí mật tạo mã băm HMACSHA512</param>
    public string CreateRequestUrl(string baseUrl, string vnpHashSecret)
    {
        var data = new StringBuilder();
        foreach (var (key, value) in _requestData)
        {
            if (!string.IsNullOrEmpty(value))
            {
                data.Append(WebUtility.UrlEncode(key) + "=" + WebUtility.UrlEncode(value) + "&");
            }
        }

        var queryString = data.ToString();
        baseUrl += "?" + queryString;

        var signData = queryString;
        if (signData.Length > 0)
        {
            signData = signData.Remove(signData.Length - 1, 1);
        }

        // Sinh chữ ký bảo mật HMACSHA512 theo quy chuẩn 02-security-vnpay.md
        var vnpSecureHash = HmacSha512(vnpHashSecret, signData);
        baseUrl += "vnp_SecureHash=" + vnpSecureHash;

        return baseUrl;
    }

    /// <summary>
    /// Xác thực chữ ký phản hồi từ VnPay để phòng ngừa tấn công làm sai lệch dữ liệu (Data Tampering).
    /// </summary>
    /// <param name="inputHash">Mã vnp_SecureHash nhận được từ VnPay</param>
    /// <param name="secretKey">Khóa bí mật vnp_HashSecret</param>
    public bool ValidateSignature(string inputHash, string secretKey)
    {
        var rspRaw = GetResponseDataRaw();
        var myChecksum = HmacSha512(secretKey, rspRaw);
        return string.Equals(myChecksum, inputHash, StringComparison.InvariantCultureIgnoreCase);
    }

    /// <summary>
    /// Nối chuỗi dữ liệu phản hồi để tính toán Checksum Validation (loại bỏ vnp_SecureHash & vnp_SecureHashType).
    /// </summary>
    private string GetResponseDataRaw()
    {
        var data = new StringBuilder();

        // Bỏ qua các trường chữ ký khi kiểm định
        if (_responseData.ContainsKey("vnp_SecureHashType"))
        {
            _responseData.Remove("vnp_SecureHashType");
        }
        if (_responseData.ContainsKey("vnp_SecureHash"))
        {
            _responseData.Remove("vnp_SecureHash");
        }

        foreach (var (key, value) in _responseData)
        {
            if (!string.IsNullOrEmpty(value))
            {
                data.Append(WebUtility.UrlEncode(key) + "=" + WebUtility.UrlEncode(value) + "&");
            }
        }

        if (data.Length > 0)
        {
            data.Remove(data.Length - 1, 1);
        }

        return data.ToString();
    }

    /// <summary>
    /// Thuật toán tạo mã băm HMACSHA512.
    /// </summary>
    public static string HmacSha512(string key, string inputData)
    {
        var hash = new StringBuilder();
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var inputBytes = Encoding.UTF8.GetBytes(inputData);
        using (var hmac = new HMACSHA512(keyBytes))
        {
            var hashValue = hmac.ComputeHash(inputBytes);
            foreach (var b in hashValue)
            {
                hash.Append(b.ToString("x2"));
            }
        }
        return hash.ToString();
    }

    /// <summary>
    /// Lấy địa chỉ IP của Client gửi yêu cầu, hỗ trợ cả Proxy / Reverse Proxy.
    /// </summary>
    public static string GetIpAddress(HttpContext context)
    {
        var ipAddress = string.Empty;
        try
        {
            // Kiểm tra header X-Forwarded-For nếu qua Reverse Proxy / Nginx
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                ipAddress = forwardedFor.Split(',')[0].Trim();
            }

            if (string.IsNullOrWhiteSpace(ipAddress) || ipAddress.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                var remoteIp = context.Connection.RemoteIpAddress;
                if (remoteIp is not null)
                {
                    if (remoteIp.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        remoteIp = Dns.GetHostEntry(remoteIp).AddressList
                            .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
                    }
                    ipAddress = remoteIp?.ToString() ?? "127.0.0.1";
                }
            }
        }
        catch
        {
            ipAddress = "127.0.0.1";
        }

        if (string.IsNullOrWhiteSpace(ipAddress) || ipAddress == "::1")
        {
            ipAddress = "127.0.0.1";
        }

        return ipAddress;
    }
}

/// <summary>
/// Bộ so sánh chuỗi theo chuẩn ASCII Ordinal để sắp xếp tham số VnPay.
/// </summary>
internal sealed class VnPayCompare : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        var compareInfo = CompareInfo.GetCompareInfo("en-US");
        return compareInfo.Compare(x, y, CompareOptions.Ordinal);
    }
}
