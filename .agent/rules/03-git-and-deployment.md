---
trigger: always_on
---

# Quy chuẩn Git và Cấu hình Triển khai (Docker)

## Quy ước Git Commit (Conventional Commits)
- Khi người dùng yêu cầu commit mã nguồn, hãy đề xuất thông điệp commit theo chuẩn:
  - `feat(cart):` khi thêm tính năng liên quan đến giỏ hàng đặc sản.
  - `fix(vnpay):` khi sửa lỗi luồng thanh toán hoặc mã hóa.
  - `chore(docker):` khi cập nhật file cấu hình Docker hoặc môi trường server.

## Quy tắc Đóng gói và Triển khai (Dockerize)
- Khi sinh file `Dockerfile`, bắt buộc phải áp dụng kiến trúc **Multi-stage build** để tách biệt quá trình Build (sử dụng .NET SDK) và quá trình Chạy (sử dụng .NET Runtime nhẹ) nhằm tối ưu dung lượng hình ảnh (Image) lưu trữ trên server.
- File `docker-compose.yml` phải cấu hình mạng nội bộ mã hóa (Bridge Network) cách ly container Database với môi trường internet bên ngoài, chỉ mở cổng (Expose ports) public cho ứng dụng Web.
