// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Meta.UI.ViewModels.Shell;

namespace Meta.UI.ViewModels.Navigation;

/// <summary>
/// Contract for page navigation across independent application views (MAUI Shell / NavigationPage style).
/// </summary>
public interface INavigationService
{
    /// <summary>Gets the currently active navigation page section.</summary>
    NavigationSection CurrentSection { get; }

    /// <summary>Gets a value indicating whether there is a previous page in the back stack.</summary>
    bool CanGoBack { get; }

    /// <summary>Raised whenever the active page changes.</summary>
    event EventHandler<NavigationSection>? Navigated;

    /// <summary>Navigates to the specified page section and pushes the previous page onto the back stack.</summary>
    void NavigateTo(NavigationSection section);

    /// <summary>Navigates back to the previous page section if available.</summary>
    bool GoBack();
}

/// <summary>
/// Default thread-safe implementation of <see cref="INavigationService"/> managing page transitions and back-stack history.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly Stack<NavigationSection> _backStack = new();
    private NavigationSection _currentSection;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationService"/> class.
    /// </summary>
    public NavigationService(NavigationSection initialSection = NavigationSection.Overview) =>
        _currentSection = initialSection;

    /// <inheritdoc />
    public NavigationSection CurrentSection => _currentSection;

    /// <inheritdoc />
    public bool CanGoBack => _backStack.Count > 0;

    /// <inheritdoc />
    public event EventHandler<NavigationSection>? Navigated;

    /// <inheritdoc />
    public void NavigateTo(NavigationSection section)
    {
        if (_currentSection == section)
        {
            return;
        }

        _backStack.Push(_currentSection);
        _currentSection = section;
        this.Navigated?.Invoke(this, section);
    }

    /// <inheritdoc />
    public bool GoBack()
    {
        if (_backStack.Count == 0)
        {
            return false;
        }

        NavigationSection previous = _backStack.Pop();
        _currentSection = previous;
        this.Navigated?.Invoke(this, previous);
        return true;
    }
}
