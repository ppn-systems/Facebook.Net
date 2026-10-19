# Facebook.Net — Enterprise Social Automation SDK & Cloudflare-Styled Avalonia Desktop Suite (.NET 10 / Native AOT)

**Facebook.Net** là bộ SDK tự động hóa mạng xã hội hiệu năng cao và ứng dụng Desktop đa nền tảng (**Avalonia UI 12**, kiến trúc **MVVM** đa project) được xây dựng trên nền tảng **.NET 10** (tương thích 100% **Native AOT**, `TreatWarningsAsErrors = true`, không phụ thuộc Selenium/Playwright/Newtonsoft.Json).

---

## Kiến trúc Giải pháp (15 Projects)

### 1. Core SDK & Giao thức Comet GraphQL (`src/`)
- **`Facebook.Net.Contracts`**: Tập hợp các kiểu nguyên thủy (`AccountStatus`, `CheckpointReason`, `GroupMembershipState`, `PostVisibility`, `ReactionType`, `ProxyProtocol`), mô hình dữ liệu (`FacebookAccount`, `TargetGroupRecord`, `CampaignTemplateRecord`, `PostSummary`, `GroupSummary`, `SessionContext`), kết quả `FacebookResult<T>` và giao diện lưu trữ/vận chuyển.
- **`Facebook.Net`**: Thực thi giao thức Facebook Comet (`POST /api/graphql/`), quản lý phiên, trích xuất token (`fb_dtsg`, `lsd`, `c_user`), tạo chữ ký `jazoest`, phân tích phản hồi đa JSON (`for (;;);`), hệ thống **Spintax** (`PayloadBuilder.SpinText`), và 8 client nghiệp vụ:
  - `FeedClient` (đăng bài tường cá nhân, nhóm, kèm hình ảnh, thả cảm xúc, xóa bài)
  - `CommentClient` (bình luận bán hàng, trả lời bình luận, đẩy bài / Up-top)
  - `GroupClient` (tìm kiếm nhóm theo từ khóa, tham gia/rời nhóm, quét danh sách nhóm đã tham gia, quét bài viết mới trong nhóm để lọc khách hàng tiềm năng)
  - `MediaClient` (tải ảnh lên lấy `fbid`)
  - `MessagingClient`, `FriendClient`, `ProfileClient`, `SearchClient`
- **`Facebook.Net.Storage`**: Lưu trữ SQLite không dùng Reflection (`SqliteAccountStore`), quản lý hồ sơ tài khoản, proxy, trạng thái sức khỏe, danh sách nhóm mục tiêu (`target_groups`), mẫu chiến dịch Spintax (`campaign_templates`), và lịch sử chống trùng lặp bình luận (`action_history`).
- **`Facebook.Net.Transport`**: HTTP/2 Transport hỗ trợ xoay vòng Proxy, giới hạn tốc độ theo tài khoản và chống vân tay trình duyệt.
- **`Facebook.Net.Hosting`**: Tích hợp `Microsoft.Extensions.DependencyInjection` và `IHostedService`.

### 2. Trình duyệt Cô lập CDP (`browser/`)
- **`Facebook.Net.Browser`**: Điều khiển trực tiếp Chrome, Microsoft Edge hoặc Brave qua giao thức **Chrome DevTools Protocol (CDP)** trên WebSocket gốc (Zero-dependency). Hỗ trợ:
  - Tự động phát hiện trình duyệt đã cài trên máy hoặc chỉ định đường dẫn tùy chỉnh
  - Hồ sơ cô lập riêng biệt cho từng tài khoản (`user-data-dir`)
  - Nạp trực tiếp Cookie (`Network.setCookies`) và trích xuất `SessionContext` từ trang đang mở
  - Tự động hóa tương tác bàn phím/chuột có độ trễ giống người thật

### 3. Giao diện Desktop Avalonia UI 12 chuẩn MVVM (`ui/` & `extensions/`)
- **`Facebook.Net.UI.Core`**:
  - Thiết kế chuẩn **Cloudflare Dashboard** (`#0F1115` / `#18181B` Dark Mode, `#F8FAFC` / `#FFFFFF` Light Mode, điểm nhấn cam `#F6821F`).
  - Đa ngôn ngữ thời gian thực (**Tiếng Việt `vi-VN`** & **Tiếng Anh `en-US`**) không một thuật ngữ kỹ thuật (`Zero UID / Zero UUID`).
  - `UniversalSessionImporter`: Tự động nhận diện JSON từ **J2Team Cookies**, **Get Token Cookie / EditThisCookie**, chuỗi `c_user=...; xs=...;` hoặc định dạng `UID|Pass|2FA|Cookie` mà không bắt người dùng nhập UID thủ công.
  - `SqliteAuditLogStore`: Cơ sở dữ liệu Nhật ký Hoạt động riêng biệt (`facebook_audit.db`), hỗ trợ lọc 4 chiều (Danh mục sự kiện, Khoảng thời gian, Tài khoản, Từ khóa) và xuất báo cáo `.csv` / `.json`.
- **`Facebook.Net.UI.Components`**: Thư viện Control tái sử dụng (`CloudflareCard`, `StatusPillBadge`, `MetricStatCard`, `SectionHeader`, `SvgIconView` với kho vector Path).
- **`Facebook.Net.UI.ViewModels`**: Tầng `ObservableObject` / `RelayCommand` tách file `partial` gọn gàng (`ShellViewModel`, `AuditLogDrawerViewModel`).
- **`Facebook.Net.App`**: Ứng dụng thực thi chính gồm 4 phân khu chuyên biệt và Thanh Taskbar Nhật ký Hoạt động đóng/mở phía dưới.
- **`extensions/Facebook.Net.CookieExtension`**: Tiện ích mở rộng **Chromium Manifest V3** giúp sao chép gói phiên Facebook chỉ với 1 cú nhấp chuột để dán thẳng vào ứng dụng.

---

## Kiểm thử & Xây dựng

```bash
dotnet build Facebook.Net.slnx
dotnet test Facebook.Net.slnx
```

> Các bài kiểm thử tích hợp thực tế (`LiveSessionEndToEndTests`) sẽ tự động chạy khi phát hiện biến môi trường `FB_TEST_COOKIE_PATH` hoặc tệp `.secrets/facebook_cookie.txt`, đảm bảo an toàn tuyệt đối trên môi trường CI/CD.

---

## License & Copyright

Copyright (c) 2026 PPN Corporation. All rights reserved.

Distributed under the [Apache License 2.0](LICENSE).
