using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace EcommerceHue.Services
{
    public class DatabaseBackupService
    {
        private readonly HttpClient _httpClient;
        private readonly string _supabaseUrl;
        private readonly string _supabaseKey;
        private readonly string _bucketName = "sqlite-backup"; // Tên Bucket trên Supabase Storage
        private readonly string _fileName = "ecommercehue.db";
        private readonly string _localFilePath;

        // Hàm khởi tạo: Tự động chạy khi ứng dụng nạp dịch vụ
        public DatabaseBackupService(IConfiguration configuration)
        {
            _httpClient = new HttpClient();

            // Đọc cấu hình linh hoạt cả hai định dạng cấu hình phân cấp (dấu hai chấm) và biến môi trường thô
            _supabaseUrl = (configuration["Supabase:Url"] 
                         ?? configuration["Supabase__Url"] 
                         ?? Environment.GetEnvironmentVariable("Supabase__Url") 
                         ?? Environment.GetEnvironmentVariable("Supabase:Url") 
                         ?? "").TrimEnd('/');

            _supabaseKey = configuration["Supabase:Key"] 
                        ?? configuration["Supabase__Key"] 
                        ?? Environment.GetEnvironmentVariable("Supabase__Key") 
                        ?? Environment.GetEnvironmentVariable("Supabase:Key") 
                        ?? "";

            // Tự động phân tách chuỗi kết nối để tìm ra đường dẫn file SQLite thực tế, fallback chuẩn /tmp/ecommercehue.db
            var connectionString = configuration.GetConnectionString("DefaultConnection") 
                                ?? configuration["ConnectionStrings__DefaultConnection"] 
                                ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
                                ?? "Data Source=/tmp/ecommercehue.db";

            _localFilePath = ParseFilePathFromConnectionString(connectionString);
        }

        /// <summary>
        /// HÀM 1: Tải file dữ liệu cũ từ Supabase về máy chủ khi ứng dụng khởi động
        /// </summary>
        public async Task DownloadDatabaseAsync()
        {
            // Kiểm tra cấu hình URL / Key
            if (string.IsNullOrEmpty(_supabaseUrl) || string.IsNullOrEmpty(_supabaseKey))
            {
                Console.WriteLine("=== [SUPABASE BACKUP] Thiếu cấu hình Supabase URL/Key. Bỏ qua bước khôi phục dữ liệu. ===");
                return;
            }

            try
            {
                // Endpoint tải file private từ Supabase Storage
                var requestUrl = $"{_supabaseUrl}/storage/v1/object/authenticated/{_bucketName}/{_fileName}";
                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                // Đính kèm apikey và Bearer token
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseKey);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var fileBytes = await response.Content.ReadAsByteArrayAsync();

                    // Đảm bảo thư mục đích tồn tại
                    var directory = Path.GetDirectoryName(_localFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Ghi đè file SQLite cục bộ
                    await File.WriteAllBytesAsync(_localFilePath, fileBytes);
                    Console.WriteLine($"=== [SUPABASE BACKUP] Phục hồi tệp dữ liệu SQLite ({fileBytes.Length:N0} bytes) thành công về '{_localFilePath}'!");
                }
                else
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"=== [SUPABASE BACKUP] Chưa có bản sao lưu trước đó trên đám mây ({response.StatusCode}: {errBody}). Tạo mới DB trống.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== [SUPABASE BACKUP] Lỗi hệ thống khi khôi phục database: {ex.Message} ===");
            }
        }

        /// <summary>
        /// HÀM 2: Đẩy file SQLite lên lưu trữ trên Supabase khi có biến động dữ liệu (Đơn hàng, Khách hàng, Thanh toán)
        /// </summary>
        public async Task UploadDatabaseAsync()
        {
            if (string.IsNullOrEmpty(_supabaseUrl) || string.IsNullOrEmpty(_supabaseKey))
            {
                Console.WriteLine("=== [SUPABASE BACKUP ERROR] Thiếu cấu hình Supabase__Url hoặc Supabase__Key!");
                return;
            }

            try
            {
                // Kiểm tra file SQLite cục bộ có tồn tại không
                if (!File.Exists(_localFilePath))
                {
                    Console.WriteLine($"=== [SUPABASE BACKUP] Không tìm thấy tệp tin cơ sở dữ liệu '{_localFilePath}' để tiến hành sao lưu.");
                    return;
                }

                // Xả toàn bộ dữ liệu từ bộ nhớ đệm WAL xuống file chính và giải phóng connection pool
                try
                {
                    using (var connection = new SqliteConnection($"Data Source={_localFilePath}"))
                    {
                        await connection.OpenAsync();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                            await command.ExecuteNonQueryAsync();
                        }
                    }
                    SqliteConnection.ClearAllPools();
                }
                catch (Exception walEx)
                {
                    Console.WriteLine($"=== [SUPABASE BACKUP WARNING] Lỗi checkpoint WAL: {walEx.Message}");
                }

                // Endpoint upload chuẩn của Supabase Storage: /storage/v1/object/{bucket}/{file}
                var requestUrl = $"{_supabaseUrl}/storage/v1/object/{_bucketName}/{_fileName}";
                var fileBytes = await File.ReadAllBytesAsync(_localFilePath);

                var byteArrayContent = new ByteArrayContent(fileBytes);
                byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-sqlite3");

                var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseKey);

                // Cho phép ghi đè (Upsert) file đã tồn tại
                request.Headers.Add("x-upsert", "true");
                request.Content = byteArrayContent;

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"=== [SUPABASE BACKUP SUCCESS] Đã gửi bản sao lưu SQLite ({fileBytes.Length:N0} bytes) lên Supabase Storage '{_bucketName}/{_fileName}' thành công!");
                }
                else
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"=== [SUPABASE BACKUP FAILED] Status: {response.StatusCode} | Detail: {errBody}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== [SUPABASE BACKUP] Lỗi kết nối luồng đẩy dữ liệu: {ex.Message} ===");
            }
        }

        // Hàm bổ trợ: Bóc tách đường dẫn file từ Connection String
        private string ParseFilePathFromConnectionString(string connectionString)
        {
            try
            {
                var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();
                    if (trimmedPart.StartsWith("Data Source", StringComparison.OrdinalIgnoreCase))
                    {
                        var index = trimmedPart.IndexOf('=');
                        if (index != -1 && index < trimmedPart.Length - 1)
                        {
                            return trimmedPart.Substring(index + 1).Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== [SUPABASE BACKUP WARNING] Lỗi bóc tách ConnectionString: {ex.Message}");
            }

            // Fallback mặc định đồng bộ tuyệt đối với /tmp/ecommercehue.db trên Render
            return "/tmp/ecommercehue.db";
        }
    }
}
