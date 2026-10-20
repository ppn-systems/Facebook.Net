// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using Facebook.Net.Contracts.Models;

namespace Facebook.Net.UI.Core.Groups;

/// <summary>
/// Domain service contract for the Groups &amp; Color Tags Bounded Context: encapsulates tag CSV
/// manipulation on <see cref="SavedTargetGroup"/> entities and Fisher-Yates anti-spam random group selection.
/// </summary>
public interface IGroupTagDomainService
{
    /// <summary>Builds a new <see cref="GroupTag"/> with a normalized name and hex color.</summary>
    GroupTag CreateTag(string name, string? colorHex);

    /// <summary>Applies or removes <paramref name="tagId"/> on a <see cref="SavedTargetGroup"/> and returns whether the group's <c>TagsCsv</c> changed.</summary>
    (SavedTargetGroup UpdatedGroup, bool Changed) ApplyTagChange(SavedTargetGroup group, string tagId, bool assignTag);

    /// <summary>Toggles <paramref name="tagId"/> on a <see cref="SavedTargetGroup"/> and returns the updated entity plus whether the tag was removed.</summary>
    (SavedTargetGroup UpdatedGroup, bool WasRemoved) ToggleTagOnGroup(
        SavedTargetGroup existing,
        string groupName,
        string? avatarUrl,
        string tagId);

    /// <summary>Selects a random subset of <paramref name="randomCount"/> indices out of <paramref name="totalCount"/> using Fisher-Yates shuffle.</summary>
    HashSet<int> SelectRandomIndices(int totalCount, int randomCount);
}

/// <summary>
/// Default stateless implementation of <see cref="IGroupTagDomainService"/>.
/// </summary>
public sealed class GroupTagDomainService : IGroupTagDomainService
{
    /// <summary>Gets the shared stateless singleton instance of <see cref="GroupTagDomainService"/>.</summary>
    public static GroupTagDomainService Instance { get; } = new();

    /// <inheritdoc />
    public GroupTag CreateTag(string name, string? colorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string color = string.IsNullOrWhiteSpace(colorHex) ? "#F6821F" : colorHex.Trim();
        return new GroupTag(
            Id: $"tag_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Name: name.Trim(),
            ColorHex: color);
    }

    /// <inheritdoc />
    public (SavedTargetGroup UpdatedGroup, bool Changed) ApplyTagChange(SavedTargetGroup group, string tagId, bool assignTag)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (string.IsNullOrWhiteSpace(tagId))
        {
            return (group, false);
        }

        List<string> ids = [.. group.GetTagIds()];
        bool changed = false;

        if (assignTag)
        {
            bool exists = false;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                ids.Add(tagId);
                changed = true;
            }
        }
        else
        {
            for (int i = ids.Count - 1; i >= 0; i--)
            {
                if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
                {
                    ids.RemoveAt(i);
                    changed = true;
                }
            }
        }

        if (!changed)
        {
            return (group, false);
        }

        SavedTargetGroup updated = group with
        {
            TagsCsv = ids.Count > 0 ? string.Join(',', ids) : null
        };
        return (updated, true);
    }

    /// <inheritdoc />
    public (SavedTargetGroup UpdatedGroup, bool WasRemoved) ToggleTagOnGroup(
        SavedTargetGroup existing,
        string groupName,
        string? avatarUrl,
        string tagId)
    {
        ArgumentNullException.ThrowIfNull(existing);
        List<string> ids = [.. existing.GetTagIds()];
        bool removed = false;
        for (int i = ids.Count - 1; i >= 0; i--)
        {
            if (string.Equals(ids[i], tagId, StringComparison.OrdinalIgnoreCase))
            {
                ids.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed)
        {
            ids.Add(tagId);
        }

        SavedTargetGroup updated = existing with
        {
            GroupName = !string.IsNullOrWhiteSpace(groupName) ? groupName : existing.GroupName,
            AvatarUrl = existing.AvatarUrl ?? avatarUrl,
            TagsCsv = ids.Count > 0 ? string.Join(',', ids) : null
        };

        return (updated, removed);
    }

    /// <inheritdoc />
    public HashSet<int> SelectRandomIndices(int totalCount, int randomCount)
    {
        HashSet<int> chosen = new();
        if (totalCount <= 0 || randomCount <= 0)
        {
            return chosen;
        }

        int count = Math.Clamp(randomCount, 1, totalCount);
        List<int> indices = new(totalCount);
        for (int i = 0; i < totalCount; i++)
        {
            indices.Add(i);
        }

        for (int i = indices.Count - 1; i > 0; i--)
        {
            int swapIdx = Random.Shared.Next(i + 1);
            (indices[i], indices[swapIdx]) = (indices[swapIdx], indices[i]);
        }

        for (int i = 0; i < count; i++)
        {
            _ = chosen.Add(indices[i]);
        }

        return chosen;
    }
}
