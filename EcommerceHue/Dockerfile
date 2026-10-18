# ==============================================================================
# Multi-stage Build Dockerfile dành cho ASP.NET Core (.NET 9 SDK & Runtime)
# Tối ưu hóa dung lượng image khi chọn Root Directory là EcommerceHue
# ==============================================================================

# BƯỚC 1: Build & Publish (Sử dụng .NET 9 SDK)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Sao chép tệp dự án và khôi phục các gói phụ thuộc (Restore Dependencies)
COPY ["EcommerceHue.csproj", "./"]
RUN dotnet restore "EcommerceHue.csproj"

# Sao chép toàn bộ mã nguồn và biên dịch phát hành (Publish)
COPY . .
RUN dotnet publish "EcommerceHue.csproj" -c Release -o /app/publish /p:UseAppHost=false

# BƯỚC 2: Runtime (Sử dụng ASP.NET Core 9 Runtime gọn nhẹ, tối ưu bảo mật)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Cấu hình mở cổng mạng 8080 mặc định cho Render & Railway tự động ánh xạ link HTTPS
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Sao chép gói phần mềm đã biên dịch từ Bước 1 sang Runtime
COPY --from=build /app/publish .

# Cấp quyền đọc/ghi toàn quyền cho thư mục /app để ứng dụng tự động tạo và ghi tệp SQLite (ecommercehue.db) trên Linux Render
RUN chmod -R 777 /app

# Lệnh khởi chạy ứng dụng chính
ENTRYPOINT ["dotnet", "EcommerceHue.dll"]
