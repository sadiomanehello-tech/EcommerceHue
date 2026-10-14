---
trigger: always_on
---

# Quy chuẩn Lập trình & Hành vi Agent

## Ngôn ngữ và Giao tiếp
- Luôn giao tiếp, giải thích thuật ngữ và viết mã nhận xét (comments) bằng **tiếng Việt**.
- Câu trả lời cần ngắn gọn, đi thẳng vào giải pháp và cấu trúc file cần sửa.

## Quy chuẩn Kiến trúc (ASP.NET Core MVC / Backend)
- Tuyệt đối tuân thủ mô hình **MVC (Model-View-Controller)**, tách biệt rõ ràng giữa Business Logic (Service Layer), Data Access (Repository Layer/DbContext) và Presentation (Controllers/Views).
- Đặt tên lớp theo chuẩn PascalCase (ví dụ: `ProductController`), đặt tên biến và hàm theo chuẩn camelCase (ví dụ: `totalAmount`).
- Đối với các sản phẩm đặc sản Huế (thực phẩm), tất cả các Model liên quan đến `Product` bắt buộc phải có hai trường: `ExpiryDate` (Hạn sử dụng) và `StockQuantity` (Số lượng tồn kho).

## Kiểm soát hành vi
- KHÔNG tự ý xóa, sửa các file cấu hình hệ thống gốc của dự án (`.csproj`, `appsettings.json`, `Program.cs`) mà không liệt kê kế hoạch thay đổi cụ thể và hỏi ý kiến người dùng trước.
