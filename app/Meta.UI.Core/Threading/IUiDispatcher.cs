// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using Avalonia.Threading;

namespace Meta.UI.Core.Threading;

/// <summary>
/// Abstraction for marshaling UI updates onto the primary UI thread, decoupling ViewModels
/// from direct static <see cref="Dispatcher.UIThread"/> references and improving unit testability.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Determines whether the calling thread has access to the UI thread.
    /// </summary>
    bool CheckAccess();

    /// <summary>
    /// Posts an action to be executed on the UI thread, or executes it inline if already on the UI thread.
    /// </summary>
    void Post(Action action);
}

/// <summary>
/// Default <see cref="IUiDispatcher"/> implementation backed by Avalonia's <see cref="Dispatcher.UIThread"/>
/// when an Avalonia application is running, with safe inline fallback during headless unit tests.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <summary>Gets the shared singleton instance.</summary>
    public static AvaloniaUiDispatcher Instance { get; } = new();

    /// <inheritdoc />
    public bool CheckAccess()
    {
        try
        {
            return Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess();
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (this.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            Dispatcher.UIThread.Post(action);
        }
        catch (InvalidOperationException)
        {
            action();
        }
    }
}
