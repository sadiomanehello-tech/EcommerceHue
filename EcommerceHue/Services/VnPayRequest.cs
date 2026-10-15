using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace VnPay.NET;

/// <summary>
/// Đối tượng đóng gói yêu cầu thanh toán chuẩn sang cổng VnPay Sandbox.
/// Tự động định dạng, mã hóa URL và tạo chữ ký số HMACSHA512 an toàn theo 02-security-vnpay.md.
/// </summary>
public sealed class VnPayRequest
{
    public string vnp_Version { get; set; } = "2.1.0";
    public string vnp_Command { get; set; } = "pay";
    public string? vnp_TmnCode { get; set; }
    public long vnp_Amount { get; set; }
    public string? vnp_CreateDate { get; set; }
    public string vnp_CurrCode { get; set; } = "VND";
    public string vnp_IpAddr { get; set; } = "118.70.192.1";
    public string vnp_Locale { get; set; } = "vn";
    public string? vnp_OrderInfo { get; set; }
    public string vnp_OrderType { get; set; } = "other";
    public string? vnp_ReturnUrl { get; set; }
    public string? vnp_TxnRef { get; set; }

    /// <summary>
    /// Sinh đường dẫn URL thanh toán hoàn chỉnh bao gồm toàn bộ QueryString và chữ ký bảo mật vnp_SecureHash.
    /// </summary>
    /// <param name="baseUrl">Địa chỉ cổng thanh toán VnPay</param>
    /// <param name="hashSecret">Khóa bí mật HMACSHA512</param>
    public string GetPaymentUrl(string? baseUrl, string? hashSecret)
    {
        // Tự động chuẩn hóa endpoint Sandbox nếu cấu hình chỉ là "vnpayment.vn" hoặc thiếu vpcpay.html
        if (string.IsNullOrWhiteSpace(baseUrl) || !baseUrl.Contains("vpcpay.html", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
        }

        if (string.IsNullOrWhiteSpace(hashSecret))
        {
            throw new ArgumentException("Mã bí mật vnp_HashSecret không được để trống.", nameof(hashSecret));
        }

        // Tập hợp toàn bộ tham số vào danh sách sắp xếp theo thứ tự bảng chữ cái A-Z (Ordinal)
        var parameters = new SortedList<string, string>(new VnPayOrdinalComparer());

        AddIfNotNull(parameters, "vnp_Version", vnp_Version);
        AddIfNotNull(parameters, "vnp_Command", vnp_Command);
        AddIfNotNull(parameters, "vnp_TmnCode", vnp_TmnCode);
        AddIfNotNull(parameters, "vnp_Amount", vnp_Amount.ToString(CultureInfo.InvariantCulture));
        AddIfNotNull(parameters, "vnp_CreateDate", vnp_CreateDate);
        AddIfNotNull(parameters, "vnp_CurrCode", vnp_CurrCode);
        AddIfNotNull(parameters, "vnp_IpAddr", vnp_IpAddr);
        AddIfNotNull(parameters, "vnp_Locale", vnp_Locale);
        AddIfNotNull(parameters, "vnp_OrderInfo", vnp_OrderInfo);
        AddIfNotNull(parameters, "vnp_OrderType", vnp_OrderType);
        AddIfNotNull(parameters, "vnp_ReturnUrl", vnp_ReturnUrl);
        AddIfNotNull(parameters, "vnp_TxnRef", vnp_TxnRef);

        var data = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            // Mã hóa URL một lần duy nhất bằng WebUtility.UrlEncode theo chuẩn UTF-8
            data.Append(WebUtility.UrlEncode(key))
                .Append('=')
                .Append(WebUtility.UrlEncode(value))
                .Append('&');
        }

        var queryString = data.ToString();
        var signData = queryString.Length > 0 ? queryString.Remove(queryString.Length - 1, 1) : string.Empty;

        // Băm mật mã học HMACSHA512 để sinh chữ ký số
        var secureHash = HmacSha512(hashSecret, signData);

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}{signData}&vnp_SecureHash={secureHash}";
    }

    private static void AddIfNotNull(SortedList<string, string> list, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            list[key] = value;
        }
    }

    public static string HmacSha512(string key, string inputData)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var inputBytes = Encoding.UTF8.GetBytes(inputData);

        using var hmac = new HMACSHA512(keyBytes);
        var hashBytes = hmac.ComputeHash(inputBytes);

        var hex = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            hex.Append(b.ToString("x2"));
        }

        return hex.ToString();
    }
}

/// <summary>
/// Lớp tiện ích hỗ trợ đọc và đối soát chữ ký số phản hồi từ cổng VnPay.
/// </summary>
public sealed class VnPayResponse
{
    private readonly SortedList<string, string> _responseData = new(new VnPayOrdinalComparer());

    public string? vnp_TxnRef => GetValue("vnp_TxnRef");
    public string? vnp_TransactionNo => GetValue("vnp_TransactionNo");
    public string? vnp_ResponseCode => GetValue("vnp_ResponseCode");
    public string? vnp_TransactionStatus => GetValue("vnp_TransactionStatus");
    public string? vnp_SecureHash => GetValue("vnp_SecureHash");
    public string? vnp_BankCode => GetValue("vnp_BankCode");
    public string? vnp_CardType => GetValue("vnp_CardType");
    public string? vnp_PayDate => GetValue("vnp_PayDate");
    public string? vnp_OrderInfo => GetValue("vnp_OrderInfo");
    public string? vnp_Amount => GetValue("vnp_Amount");

    public VnPayResponse(IQueryCollection query)
    {
        foreach (var (key, value) in query)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                _responseData[key] = value.ToString();
            }
        }
    }

    public string? GetValue(string key)
    {
        return _responseData.TryGetValue(key, out var val) ? val : null;
    }

    /// <summary>
    /// Đối soát chữ ký phản hồi với khóa bí mật HMACSHA512 (chống tấn công giả mạo Data Tampering).
    /// </summary>
    public bool ValidateSignature(string? secretKey)
    {
        var inputHash = vnp_SecureHash;
        if (string.IsNullOrWhiteSpace(inputHash) || string.IsNullOrWhiteSpace(secretKey))
        {
            return false;
        }

        var rawData = new StringBuilder();
        var encodedData = new StringBuilder();

        foreach (var (key, value) in _responseData)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                key.Equals("vnp_SecureHash", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("vnp_SecureHashType", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (rawData.Length > 0)
            {
                rawData.Append('&');
                encodedData.Append('&');
            }

            rawData.Append(key).Append('=').Append(value);
            encodedData.Append(WebUtility.UrlEncode(key)).Append('=').Append(WebUtility.UrlEncode(value));
        }

        var hashRaw = VnPayRequest.HmacSha512(secretKey, rawData.ToString());
        var hashEncoded = VnPayRequest.HmacSha512(secretKey, encodedData.ToString());

        return string.Equals(hashRaw, inputHash, StringComparison.OrdinalIgnoreCase)
            || string.Equals(hashEncoded, inputHash, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetIpAddress(HttpContext context)
    {
        try
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ips.Length > 0 && !string.IsNullOrWhiteSpace(ips[0]))
                {
                    return ips[0];
                }
            }

            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrWhiteSpace(remoteIp) || remoteIp == "::1")
            {
                return "118.70.192.1";
            }

            return remoteIp;
        }
        catch
        {
            return "118.70.192.1";
        }
    }
}

/// <summary>
/// So sánh chuỗi theo bảng mã ASCII Ordinal để bảo đảm thứ tự bảng chữ cái A-Z chuẩn xác 100% của VnPay.
/// </summary>
public sealed class VnPayOrdinalComparer : IComparer<string>
{
    private static readonly CompareInfo UsCompareInfo = CompareInfo.GetCompareInfo("en-US");

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        return UsCompareInfo.Compare(x, y, CompareOptions.Ordinal);
    }
}
