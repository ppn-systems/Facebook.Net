// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.App.Views;

/// <summary>
/// Partial implementation of <see cref="MainWindow"/> building the Account Management, Group Publishing, Sales Commenting, and Log panels.
/// </summary>
public sealed partial class MainWindow
{
    private Border BuildAccountManagementPanel()
    {
        StackPanel panel = new() { Spacing = 8 };

        panel.Children.Add(new TextBlock
        {
            Text = "1. Quản lý Tài khoản & Trình duyệt",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });

        TextBox uidBox = new() { PlaceholderText = "UID Facebook (VD: 1000123456789)" };
        uidBox.TextChanged += (_, _) => this._viewModel.NewAccountUid = uidBox.Text ?? string.Empty;

        TextBox nameBox = new() { PlaceholderText = "Tên gợi nhớ (VD: Nick Bán Hàng 01)" };
        nameBox.TextChanged += (_, _) => this._viewModel.NewAccountDisplayName = nameBox.Text ?? string.Empty;

        TextBox cookieBox = new() { PlaceholderText = "Cookie Header (Tùy chọn — có thể lấy tự động)" };
        cookieBox.TextChanged += (_, _) => this._viewModel.NewAccountCookieHeader = cookieBox.Text ?? string.Empty;

        Button addBtn = new()
        {
            Content = "Lưu / Cập nhật Tài khoản",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = this._viewModel.AddAccountCommand
        };

        ListBox accountList = new()
        {
            Height = 200,
            ItemsSource = this._viewModel.Accounts
        };
        accountList.SelectionChanged += (_, _) =>
        {
            if (accountList.SelectedItem is AccountProfile profile)
            {
                this._viewModel.SelectedAccount = profile;
            }
        };

        Button launchBtn = new()
        {
            Content = "Mở Chromium / Edge & Đồng bộ Token",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = this._viewModel.LaunchBrowserCommand
        };

        Button loadGroupsBtn = new()
        {
            Content = "Tải Danh sách Nhóm đã tham gia",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = this._viewModel.LoadJoinedGroupsCommand
        };

        panel.Children.Add(uidBox);
        panel.Children.Add(nameBox);
        panel.Children.Add(cookieBox);
        panel.Children.Add(addBtn);
        panel.Children.Add(accountList);
        panel.Children.Add(launchBtn);
        panel.Children.Add(loadGroupsBtn);

        return CreateCardBorder(new ScrollViewer { Content = panel });
    }

    private Border BuildGroupAndPublishingPanel()
    {
        StackPanel panel = new() { Spacing = 8 };

        panel.Children.Add(new TextBlock
        {
            Text = "2. Đăng bài Nhóm / Cá nhân & Quét Bài Nhóm",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });

        TextBox groupIdBox = new()
        {
            PlaceholderText = "Target Group ID (VD: 555666777)",
            Text = this._viewModel.TargetGroupId
        };
        groupIdBox.TextChanged += (_, _) => this._viewModel.TargetGroupId = groupIdBox.Text ?? string.Empty;
        this._viewModel.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(this._viewModel.TargetGroupId), StringComparison.Ordinal) &&
                !string.Equals(groupIdBox.Text, this._viewModel.TargetGroupId, StringComparison.Ordinal))
            {
                groupIdBox.Text = this._viewModel.TargetGroupId;
            }
        };

        CheckBox publishToGroupCheck = new()
        {
            Content = "Đăng vào Nhóm (Bỏ chọn = Đăng Trang cá nhân)",
            IsChecked = this._viewModel.PublishToGroup
        };
        publishToGroupCheck.IsCheckedChanged += (_, _) =>
            this._viewModel.PublishToGroup = publishToGroupCheck.IsChecked ?? false;

        TextBox postSpintaxBox = new()
        {
            PlaceholderText = "Nội dung bài đăng (hỗ trợ Spintax {a|b})",
            Text = this._viewModel.PostSpintaxContent,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 95
        };
        postSpintaxBox.TextChanged += (_, _) =>
            this._viewModel.PostSpintaxContent = postSpintaxBox.Text ?? string.Empty;

        TextBox photoPathBox = new()
        {
            PlaceholderText = "Đường dẫn ảnh đính kèm (Tùy chọn, VD: C:\\anh_san_pham.jpg)"
        };
        photoPathBox.TextChanged += (_, _) =>
            this._viewModel.PostPhotoPath = photoPathBox.Text ?? string.Empty;

        Button publishBtn = new()
        {
            Content = "Đăng bài ngay (Publish Post)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = this._viewModel.PublishPostCommand
        };

        Button scrapeGroupBtn = new()
        {
            Content = "Thu thập bài viết mới trong Nhóm (Scrape Feed)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = this._viewModel.ScrapeGroupFeedCommand
        };

        ListBox scrapedPostsList = new()
        {
            Height = 140,
            ItemsSource = this._viewModel.ScrapedPosts
        };
        scrapedPostsList.SelectionChanged += (_, _) =>
        {
            if (scrapedPostsList.SelectedItem is PostSummary post)
            {
                this._viewModel.CommentTargetPostId = post.PostId;
            }
        };

        panel.Children.Add(groupIdBox);
        panel.Children.Add(publishToGroupCheck);
        panel.Children.Add(postSpintaxBox);
        panel.Children.Add(photoPathBox);
        panel.Children.Add(publishBtn);
        panel.Children.Add(scrapeGroupBtn);
        panel.Children.Add(scrapedPostsList);

        return CreateCardBorder(new ScrollViewer { Content = panel });
    }

    private Border BuildSalesCommentingPanel()
    {
        StackPanel panel = new() { Spacing = 8 };

        panel.Children.Add(new TextBlock
        {
            Text = "3. Comment Sale / Seeding trên Bài viết",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });

        TextBox targetPostBox = new()
        {
            PlaceholderText = "Target Post ID cần Comment Sale",
            Text = this._viewModel.CommentTargetPostId
        };
        targetPostBox.TextChanged += (_, _) =>
            this._viewModel.CommentTargetPostId = targetPostBox.Text ?? string.Empty;
        this._viewModel.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(this._viewModel.CommentTargetPostId), StringComparison.Ordinal) &&
                !string.Equals(targetPostBox.Text, this._viewModel.CommentTargetPostId, StringComparison.Ordinal))
            {
                targetPostBox.Text = this._viewModel.CommentTargetPostId;
            }
        };

        TextBox commentSpintaxBox = new()
        {
            PlaceholderText = "Nội dung Comment Sale (hỗ trợ Spintax {a|b})",
            Text = this._viewModel.CommentSpintaxContent,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 110
        };
        commentSpintaxBox.TextChanged += (_, _) =>
            this._viewModel.CommentSpintaxContent = commentSpintaxBox.Text ?? string.Empty;

        TextBox commentPhotoBox = new()
        {
            PlaceholderText = "Đường dẫn ảnh báo giá / sản phẩm (Tùy chọn)"
        };
        commentPhotoBox.TextChanged += (_, _) =>
            this._viewModel.CommentPhotoPath = commentPhotoBox.Text ?? string.Empty;

        Button commentBtn = new()
        {
            Content = "Gửi Comment Sale vào Bài viết",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Command = this._viewModel.SendSalesCommentCommand
        };

        panel.Children.Add(targetPostBox);
        panel.Children.Add(commentSpintaxBox);
        panel.Children.Add(commentPhotoBox);
        panel.Children.Add(commentBtn);

        return CreateCardBorder(new ScrollViewer { Content = panel });
    }

    private Border BuildActivityLogPanel()
    {
        Grid grid = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 6
        };

        TextBlock title = new()
        {
            Text = "Nhật ký hoạt động (Activity Logs)",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        ListBox logList = new()
        {
            ItemsSource = this._viewModel.ActivityLogs
        };
        Grid.SetRow(logList, 1);
        grid.Children.Add(logList);

        return CreateCardBorder(grid);
    }
}
