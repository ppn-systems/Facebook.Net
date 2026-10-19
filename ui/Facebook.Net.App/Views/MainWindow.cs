// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Facebook.Net.App.ViewModels;

namespace Facebook.Net.App.Views;

/// <summary>
/// Primary desktop window built with pure code-behind controls for 100% Native AOT safety and zero XAML reflection.
/// Split into partial files for layout construction.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="viewModel">The backing <see cref="MainWindowViewModel"/>.</param>
    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        this._viewModel = viewModel;
        this.DataContext = viewModel;

        this.Title = "Facebook.Net — Comet GraphQL Automation & Sales Suite (.NET 10 Native AOT)";
        this.Width = 1280;
        this.Height = 820;
        this.MinWidth = 1024;
        this.MinHeight = 700;

        this.Content = this.BuildRootLayout();
    }

    private Grid BuildRootLayout()
    {
        Grid root = new()
        {
            RowDefinitions = new RowDefinitions("Auto,*,180"),
            Margin = new Thickness(16),
            RowSpacing = 12
        };

        Border headerCard = this.BuildHeaderBanner();
        Grid.SetRow(headerCard, 0);
        root.Children.Add(headerCard);

        Grid workspaceGrid = new()
        {
            ColumnDefinitions = new ColumnDefinitions("340,*,*"),
            ColumnSpacing = 12
        };

        Border accountPanel = this.BuildAccountManagementPanel();
        Grid.SetColumn(accountPanel, 0);
        workspaceGrid.Children.Add(accountPanel);

        Border groupAndPublishPanel = this.BuildGroupAndPublishingPanel();
        Grid.SetColumn(groupAndPublishPanel, 1);
        workspaceGrid.Children.Add(groupAndPublishPanel);

        Border salesCommentPanel = this.BuildSalesCommentingPanel();
        Grid.SetColumn(salesCommentPanel, 2);
        workspaceGrid.Children.Add(salesCommentPanel);

        Grid.SetRow(workspaceGrid, 1);
        root.Children.Add(workspaceGrid);

        Border logPanel = this.BuildActivityLogPanel();
        Grid.SetRow(logPanel, 2);
        root.Children.Add(logPanel);

        return root;
    }

    private Border BuildHeaderBanner()
    {
        StackPanel stack = new()
        {
            Orientation = Orientation.Vertical,
            Spacing = 4
        };

        TextBlock title = new()
        {
            Text = "Facebook.Net — Quản lý Đa Tài khoản, Thu thập Nhóm, Đăng bài & Comment Sale",
            FontSize = 18,
            FontWeight = FontWeight.Bold
        };

        TextBlock statusText = new()
        {
            Text = this._viewModel.StatusBanner,
            FontSize = 13,
            Opacity = 0.85
        };

        this._viewModel.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(MainWindowViewModel.StatusBanner), StringComparison.Ordinal))
            {
                statusText.Text = this._viewModel.StatusBanner;
            }
        };

        stack.Children.Add(title);
        stack.Children.Add(statusText);

        return CreateCardBorder(stack);
    }

    private static Border CreateCardBorder(Control child) =>
        new()
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            Padding = new Thickness(14),
            Child = child
        };
}
