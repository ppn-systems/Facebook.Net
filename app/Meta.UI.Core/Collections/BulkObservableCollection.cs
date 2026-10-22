// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Meta.UI.Core.Collections;

/// <summary>
/// High-performance <see cref="ObservableCollection{T}"/> that supports replacing or appending a batch of items
/// while firing only a single <see cref="NotifyCollectionChangedAction.Reset"/> notification, preventing UI layout thrashing.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    /// <summary>
    /// Replaces all items in the collection with <paramref name="items"/> and raises a single reset notification.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        this.CheckReentrancy();
        _suppressNotification = true;
        try
        {
            this.Items.Clear();
            foreach (T item in items)
            {
                this.Items.Add(item);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        this.OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        this.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <inheritdoc />
    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnPropertyChanged(e);
        }
    }
}
