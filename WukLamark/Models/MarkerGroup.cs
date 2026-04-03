using System;

namespace WukLamark.Models;

/// <summary>
/// Represents a named collection of waymarks for organizational purposes.
/// Groups are primarily name-based, with an optional game icon for sidebar display.
/// </summary>
[Serializable]
public class MarkerGroup
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
    public MarkerScope Scope { get; set; } = MarkerScope.Personal;

    /// <summary>Timestamp when this group was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Truncated SHA-256 hash of the character who created this group.
    /// Used to enforce read-only restrictions on shared groups.
    /// Only the creator can modify a shared group if IsReadOnly is true.
    /// </summary>
    public string? CreatorHash { get; set; }

    /// <summary>
    /// Determines if this shared group can only be modified by its creator.
    /// When true, only the character whose hash matches CreatorHash can edit/delete this group.
    /// </summary>
    public bool IsReadOnly { get; set; } = false;
}
