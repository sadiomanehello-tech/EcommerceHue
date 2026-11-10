using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace EcommerceHue.Services
{
    public class DatabaseBackupService
    {
        private readonly HttpClient _httpClient;
        private readonly string _supabaseUrl;
        private readonly string _supabaseKey;
        private readonly string _bucketName = "sqlite-backup"; // Tên Bucket bạn vừa tạo trên Supabase
        private readonly string _fileName = "ecommercehue.db";
        private readonly string _localFilePath;

        // Hàm khởi tạo (Constructor): Tự động chạy khi ứng dụng nạp dịch vụ
        public DatabaseBackupService(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            
            // Đọc cấu hình bảo mật thông tin từ môi trường đám mây Render
            _supabaseUrl = configuration["Supabase__Url"]?.TrimEnd('/');
            _supabaseKey = configuration["Supabase__Key"];

            // Tự động phân tách chuỗi kết nối để tìm ra đường dẫn file SQLite thực tế
            var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=ecommercehue.db";
            _localFilePath = ParseFilePathFromConnectionString(connectionString);
        }

        /// <summary>
        /// HÀM 1: Tải file dữ liệu cũ từ Supabase về máy chủ Render khi ứng dụng thức dậy
        /// </summary>
        public async Task DownloadDatabaseAsync()
        {
            // Kiểm tra xem bạn đã cấu hình các biến bảo mật trên Render chưa
            if (string.IsNullOrEmpty(_supabaseUrl) || string.IsNullOrEmpty(_supabaseKey))
            {
                Console.WriteLine("=== [SUPABASE BACKUP] Thiếu cấu hình Supabase URL/Key. Bỏ qua bước khôi phục dữ liệu. ===");
                return;
            }

            try
            {
                // Đường dẫn URL API gọi trực tiếp đến file lưu trữ trên Supabase
                var requestUrl = $"{_supabaseUrl}/storage/v1/object/authenticated/{_bucketName}/{_fileName}";
                var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                
                // Đính kèm mã token để Supabase xác minh quyền truy cập (vì Bucket của bạn là Private)
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseKey);

                var response = await _httpClient.SendAsync(request);

                // Nếu tìm thấy file backup cũ thành công
                if (response.IsSuccessStatusCode)
                {
                    var fileBytes = await response.Content.ReadAsByteArrayAsync();
                    
                    // Tạo thư mục (ví dụ như thư mục hệ thống /tmp) nếu nó chưa tồn tại trên Render
                    var directory = Path.GetDirectoryName(_localFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Ghi đè các byte dữ liệu tải về thành file .db cục bộ để ứng dụng dùng
                    await File.WriteAllBytesAsync(_localFilePath, fileBytes);
                    Console.WriteLine("=== [SUPABASE BACKUP] Phục hồi tệp dữ liệu SQLite cũ thành công từ Supabase Storage! ===");
                }
                else
                {
                    Console.WriteLine($"=== [SUPABASE BACKUP] Chưa có bản sao lưu trước đó trên đám mây ({response.StatusCode}). Tạo mới DB trống.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== [SUPABASE BACKUP] Lỗi hệ thống khi khôi phục database: {ex.Message} ===");
            }
        }

        /// <summary>
        /// HÀM 2: Đẩy file SQLite từ Render lên lưu trữ trên Supabase khi web có biến động dữ liệu
        /// </summary>
        public async Task UploadDatabaseAsync()
        {
            if (string.IsNullOrEmpty(_supabaseUrl) || string.IsNullOrEmpty(_supabaseKey)) return;

            try
            {
                // Kiểm tra chắc chắn xem file .db cục bộ có tồn tại trên Render để upload không
                if (!File.Exists(_localFilePath))
                {
                    Console.WriteLine("=== [SUPABASE BACKUP] Không tìm thấy tệp tin cơ sở dữ liệu để tiến hành sao lưu.");
                    return;
                }

                var requestUrl = $"{_supabaseUrl}/storage/v1/object/authenticated/{_bucketName}/{_fileName}";
                var fileBytes = await File.ReadAllBytesAsync(_localFilePath);
                
                var byteArrayContent = new ByteArrayContent(fileBytes);
                // Định nghĩa định dạng tệp tin gửi lên là chuẩn dữ liệu SQLite
                byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-sqlite3");

                var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseKey);
                
                // ĐIỀU KIỆN QUAN TRỌNG: Báo cho Supabase biết sẽ ghi đè (Upsert) lên file cũ nếu đã có file trùng tên
                request.Headers.Add("x-upsert", "true"); 
                request.Content = byteArrayContent;

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("=== [SUPABASE BACKUP] Đã gửi bản sao lưu SQLite lên bộ nhớ đám mây an toàn!");
                }
                else
                {
                    Console.WriteLine($"=== [SUPABASE BACKUP] Gửi bản sao lưu thất bại với mã lỗi: {response.StatusCode} ===");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== [SUPABASE BACKUP] Lỗi kết nối luồng đẩy dữ liệu: {ex.Message} ===");
            }
        }

        // Hàm bổ trợ: Bóc tách đường dẫn file từ Connection String (Ví dụ: "Data Source=/tmp/db.db" -> "/tmp/db.db")
        private string ParseFilePathFromConnectionString(string connectionString)
        {
            try
            {
                // Phân tách chuỗi dựa trên cả dấu chấm phẩy lẫn dấu bằng một cách an toàn
                var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();
                    if (trimmedPart.StartsWith("Data Source", StringComparison.OrdinalIgnoreCase))
                    {
                        var index = trimmedPart.IndexOf('=');
                        if (index != -1 && index < trimmedPart.Length - 1)
                        {
                            // Lấy chính xác chuỗi đường dẫn nằm phía sau dấu "=" (Ví dụ: /tmp/ecommercehue.db)
                            return trimmedPart.Substring(index + 1).Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== [SUPABASE BACKUP WARNING] Lỗi bóc tách ConnectionString: {ex.Message}");
            }
            
            // Nếu có lỗi xảy ra hoặc không tìm thấy, ép hệ thống trỏ về đúng thư mục tạm /tmp
            return "/tmp/ecommercehue.db";
        }

    }
}
