# Facebook.Net & Facebook One Client

Bộ thư viện C# SDK giao thức **Facebook Comet GraphQL** hiệu năng cao và ứng dụng Desktop đa nền tảng **Facebook One Client** (**Avalonia UI**, kiến trúc **MVVM**) trên nền tảng **.NET 10**.

---

## Tính năng nổi bật

- **Giao thức Comet GraphQL gốc**: Tương tác trực tiếp với hệ thống Facebook Comet (`POST /api/graphql/`), tự động trích xuất khóa phiên (`fb_dtsg`, `lsd`, `c_user`), sinh chữ ký `jazoest`, phân tích luồng dữ liệu phân mảnh (`defer` / `incremental`) và giải mã định danh bài viết Base64.
- **Trình duyệt cô lập qua CDP**: Điều khiển trực tiếp Google Chrome, Microsoft Edge hoặc Brave thông qua **Chrome DevTools Protocol (CDP)** trên WebSocket gốc (không phụ thuộc Selenium hay Playwright). Hỗ trợ thư mục hồ sơ độc lập cho từng tài khoản và ủy quyền gọi `fetch()` ngay trong ngữ cảnh trang để đồng nhất chữ ký TLS/JA3.
- **Quản lý đa tài khoản & Mạng riêng**: Nhập nhanh phiên đăng nhập từ trình duyệt tích hợp, chuỗi Cookie hoặc định dạng JSON; hỗ trợ gán máy chủ mạng riêng (`HTTP` / `SOCKS5`) và kiểm tra trạng thái sức khỏe cho từng tài khoản.
- **Tự động hóa Cộng đồng & Bán hàng (`Facebook One Client`)**:
  - Đồng bộ danh sách nhóm đã tham gia, tìm kiếm và gửi yêu cầu tham gia nhóm mới theo từ khóa.
  - Soạn thảo và đăng bài hàng loạt lên các nhóm mục tiêu, hỗ trợ trộn nội dung **Spintax** (`{a|b|c}`), biến động (`{date}`, `{time}`, `{group}`), đính kèm hình ảnh và tự động đẩy bài viết.
  - Quét bài viết mới từ nhiều nhóm vào kho lưu trữ cục bộ SQLite, chấm điểm mức độ quan tâm của khách hàng theo từ khóa, xem chi tiết bài viết (ảnh/video, tác giả, hội nhóm) và tự động bình luận tư vấn chống trùng lặp.
  - Giãn cách thời gian thông minh theo phân phối chuẩn (Gaussian Jitter) để bảo vệ tài khoản.
- **Giao diện Doanh nghiệp Hiện đại**: Hỗ trợ chuyển đổi thời gian thực giữa **Giao diện Tối (Dark Mode)** và **Giao diện Sáng (Light Mode)**, đa ngôn ngữ (**Tiếng Việt `vi-VN`** & **Tiếng Anh `en-US`**), biểu đồ thống kê trực quan bằng Vector SVG và hệ thống Nhật ký Hoạt động lọc đa chiều.

---

## Kiến trúc Giải pháp (`Facebook.Net.slnx`)

### 1. Tầng Giao thức & Lưu trữ (`src/`)

| Dự án | Vai trò |
| :--- | :--- |
| **`Facebook.Net.Contracts`** | Các kiểu dữ liệu nguyên thủy, mô hình miền (`AccountProfile`, `SavedTargetGroup`, `PostSummary`, `GroupSummary`, `CampaignTemplate`, `ProxyConfig`), kết quả `FacebookResult<T>` và giao diện trừu tượng (`IFacebookClient`, `IBrowserSession`, `IAccountStore`). |
| **`Facebook.Net`** | Thực thi giao thức Facebook Comet GraphQL (`CometClient`), bộ phân tích phản hồi (`ResponseParser`), bộ trộn nội dung Spintax (`PayloadBuilder`), thuật toán giãn cách thời gian và phân loại mã lỗi. |
| **`Facebook.Net.Storage`** | Lưu trữ cục bộ bằng SQLite (`SqliteAccountStore`) quản lý tài khoản, mạng riêng, nhóm mục tiêu, kho bài viết đã thu thập, mẫu bài đăng và lịch sử bình luận. |

### 2. Tầng Điều khiển Trình duyệt (`browser/`)

| Dự án | Vai trò |
| :--- | :--- |
| **`Facebook.Net.Browser`** | Kết nối WebSocket CDP (`CdpConnection`), quản lý phiên trình duyệt (`ChromiumBrowserSession`), nạp/xuất Cookie và thực thi truy vấn trong trang. |
| **`Facebook.Net.Browser.Windows`** | Tự động dò tìm trình duyệt Chromium (Edge, Chrome, Brave) và khởi tạo tiến trình trên Windows. |
| **`Facebook.Net.Browser.MacOS`** | Tự động dò tìm trình duyệt Chromium và khởi tạo tiến trình trên macOS. |

### 3. Tầng Giao diện Desktop (`ui/`)

| Dự án | Vai trò |
| :--- | :--- |
| **`Facebook.Net.UI.Core`** | Quản lý bảng màu Sáng/Tối (`ThemeService`), đa ngôn ngữ (`LocalizationService`), trình nhập dữ liệu phiên (`UniversalSessionImporter`) và cơ sở dữ liệu Nhật ký Hoạt động (`SqliteAuditLogStore`). |
| **`Facebook.Net.UI.Components`** | Hệ thống thành phần giao diện tái sử dụng (`SearchFilterBar`, `SegmentedFilterPill`, `DataTableView`, `SvgDonutChart`, `AvatarBadge`, `SurfaceCard`, `VectorIcons`). |
| **`Facebook.Net.UI.ViewModels`** | Tầng ViewModel chuẩn MVVM (`ShellViewModel`, `AuditLogDrawerViewModel`, `NavigationService`). |
| **`Facebook.Net.App`** | Ứng dụng Desktop **Facebook One Client** (`FacebookOneClient.exe`). |

### 4. Tầng Kiểm thử (`tests/`)

| Dự án | Vai trò |
| :--- | :--- |
| **`Facebook.Net.Contracts.Tests`** | Kiểm thử các mô hình dữ liệu và cấu trúc kết quả trong `Facebook.Net.Contracts`. |
| **`Facebook.Net.Tests`** | Kiểm thử bộ phân tích Comet GraphQL, giải mã Base64 Story ID, Spintax và phân loại lỗi. |
| **`Facebook.Net.Storage.Tests`** | Kiểm thử truy vấn và lưu trữ SQLite (`SqliteAccountStore`). |
| **`Facebook.Net.Browser.Tests`** | Kiểm thử kết nối CDP và quản lý phiên trình duyệt. |
| **`Facebook.Net.UI.Tests`** | Kiểm thử ViewModels, đồng bộ khóa ngôn ngữ (`vi-VN` / `en-US`) và thành phần giao diện. |

---

## Xây dựng, Kiểm thử & Đóng gói

### Yêu cầu hệ thống
- **.NET SDK**: `10.0+`
- **Hệ điều hành**: Windows 10/11 (x64) hoặc macOS

### Biên dịch và chạy kiểm thử

```bash
dotnet build Facebook.Net.slnx -c Release
dotnet test Facebook.Net.slnx -c Release
```

### Đóng gói ứng dụng thành 1 tệp thực thi duy nhất (`FacebookOneClient.exe`)

```bash
dotnet publish ui/Facebook.Net.App/Facebook.Net.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/win-x64
```

Sau khi chạy lệnh trên, thư mục `./publish/win-x64` sẽ xuất ra duy nhất 1 tệp thực thi độc lập **`FacebookOneClient.exe`** (đã tích hợp sẵn toàn bộ thư viện .NET, giao diện Avalonia, SQLite và tài nguyên).

---

## Ví dụ sử dụng SDK

```csharp
using System.Threading;
using Facebook.Net.Browser.Windows;
using Facebook.Net.Contracts.Abstractions;
using Facebook.Net.Contracts.Models;
using Facebook.Net.Protocol;

// 1. Khởi tạo trình duyệt cô lập và khôi phục phiên tài khoản
WindowsBrowserAdapter browserAdapter = new();
await using IBrowserSession session = await browserAdapter.LaunchAsync(
    new BrowserLaunchOptions(AccountUid: "100000000000000", Headless: false),
    CancellationToken.None);

using CometClient client = new(session);
FacebookResult<AccountProfile> authResult = await client.InitializeSessionAsync(CancellationToken.None);

if (authResult.IsSuccess)
{
    // 2. Đồng bộ danh sách nhóm đã tham gia
    var groupsResult = await client.GetJoinedGroupsAsync(CancellationToken.None);

    // 3. Đăng bài viết vào nhóm với cú pháp Spintax
    string message = PayloadBuilder.SpinText("{Xin chào|Chào cả nhà}, mình chia sẻ tài liệu mới nhé!");
    await client.PublishGroupPostAsync(
        new GroupPostRequest(GroupId: "123456789", MessageText: message),
        CancellationToken.None);
}
```

---

## Tuyên bố miễn trừ trách nhiệm

> **Chú ý**: *Facebook.Net là dự án độc lập được phát triển phục vụ mục đích nghiên cứu kỹ thuật và tự động hóa quy trình quản trị cộng đồng. Dự án không trực thuộc, liên kết hay được ủy quyền bởi Meta Platforms, Inc. Người sử dụng tự chịu trách nhiệm tuân thủ các điều khoản dịch vụ hiện hành khi vận hành.*

---

## Bản quyền & Giấy phép

Copyright (c) 2026 PPN Corporation. All rights reserved.

Phân phối theo giấy phép [Apache License 2.0](LICENSE).
