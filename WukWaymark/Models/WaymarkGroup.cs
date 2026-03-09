using System;

namespace WukWaymark.Models;

/// <summary>
/// Represents a named collection of waymarks for organizational purposes.
/// Groups are primarily name-based, with an optional game icon for sidebar display.
/// </summary>
[Serializable]
public class WaymarkGroup
{
    /// <summary>Unique identifier for this group.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-defined name for this group (e.g., "GPose Spots", "Venues").</summary>
    public string Name { get; set; } = "Unnamed Group";

    /// <summary>
    /// Optional game icon ID for sidebar display. Loaded via ITextureProvider.
    /// Null means no icon — the group shows a text-only label in the sidebar.
    /// </summary>
    public uint? IconId { get; set; }

    /// <summary>Persistence scope for this group and its waymarks.</summary>
    public WaymarkScope Scope { get; set; } = WaymarkScope.Personal;

    /// <summary>Timestamp when this group was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
