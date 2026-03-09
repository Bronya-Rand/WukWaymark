using System;
using System.Numerics;

namespace WukWaymark.Models;

/// <summary>
/// Represents a custom waymark location saved by the player.
/// </summary>
[Serializable]
public class Waymark
{
    /// <summary>
    /// Unique identifier for this waymark.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User-defined name for this waymark.
    /// </summary>
    public string Name { get; set; } = "Unnamed Location";

    /// <summary>
    /// World position where the waymark was created (X, Y, Z coordinates).
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Territory ID where this waymark is located.
    /// </summary>
    public ushort TerritoryId { get; set; }

    /// <summary>
    /// Map ID for this waymark location.
    /// </summary>
    public uint MapId { get; set; }

    /// <summary>
    /// World ID for this waymark location.
    /// </summary>
    /// <remarks>Used to differentiate waymarks between different worlds.</remarks>
    public uint WorldId { get; set; }

    /// <summary>
    /// Custom color for this waymark marker (RGBA).
    /// </summary>
    public Vector4 Color { get; set; } = new Vector4(1.0f, 0.8f, 0.0f, 1.0f); // Gold/yellow default

    /// <summary>
    /// Timestamp when this waymark was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Optional notes or description for this waymark.
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the geometric shape used to represent the waymark.
    /// </summary>
    /// <remarks>The selected shape determines how the waymark is visually displayed in the user interface or
    /// on a map. When <see cref="IconId"/> is set, the icon takes precedence over the shape.</remarks>
    public WaymarkShape Shape { get; set; } = WaymarkShape.Circle;

    /// <summary>
    /// Group this waymark belongs to. Null means ungrouped.
    /// </summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// Game icon ID for rendering this waymark. Loaded via ITextureProvider.
    /// When null, the <see cref="Shape"/> and <see cref="Color"/> are used for rendering instead.
    /// </summary>
    /// <remarks>
    /// Game icons render with their original RGB colors from the game's .tex files; RGB tinting is not applied.
    /// However, their alpha may be modified by the renderer.
    /// </remarks>
    public uint? IconId { get; set; }

    /// <summary>
    /// Persistence scope — Personal (character-specific) or Shared (all characters on this install).
    /// </summary>
    public WaymarkScope Scope { get; set; } = WaymarkScope.Personal;

    /// <summary>
    /// Truncated SHA-256 hash of the character's content ID who created this waymark.
    /// Used for personal waymark scoping without storing raw content IDs.
    /// Only relevant when <see cref="Scope"/> is <see cref="WaymarkScope.Personal"/>.
    /// </summary>
    public string? CharacterHash { get; set; }

    /// <summary>
    /// Maximum distance (in yalms) at which this waymark is visible on the map/minimap.
    /// A value of 0 means the waymark is always visible regardless of distance.
    /// </summary>
    public float VisibilityRadius { get; set; } = 0f;
}
