---
trigger: always_on
---

# Quy tắc Bảo mật Hệ thống & Tích hợp VnPay

## Quản lý thông tin nhạy cảm
- TUYỆT ĐỐI KHÔNG hardcode (viết trực tiếp) các chuỗi ký tự mật vào mã nguồn. Bao gồm: Chuỗi kết nối cơ sở dữ liệu (Connection Strings), Mã bảo mật VnPay (`vnp_HashSecret`), và Mã định danh website (`vnp_TmnCode`).
- Tất cả các tham số nhạy cảm trên phải được đọc thông qua `IConfiguration` lấy từ tệp `appsettings.json` hoặc biến môi trường (Environment Variables).

## Xử lý luồng giao dịch VnPay
- Khi viết mã cho hàm tạo URL thanh toán hướng sang cổng VnPay, bắt buộc phải sử dụng thuật toán mã hóa **HMACSHA512** để tạo chuỗi chữ ký (`vnp_SecureHash`).
- Tại Controller tiếp nhận phản hồi từ VnPay (`VnpayIPN`), bắt buộc phải thực hiện kiểm tra kiểm định chữ ký (Checksum Validation) trước khi cập nhật trạng thái đơn hàng (`Order` thành `Paid`) trong Database để tránh lỗ hổng giả mạo dữ liệu đơn hàng (Data Tampering).
- Luôn thêm cơ chế ghi nhật ký (Logging) đối với các giao dịch thất bại hoặc chữ ký không hợp lệ để phục vụ mục đích kiểm toán bảo mật.
