using System;
using System.Numerics;
using System.Text.Json.Serialization;

namespace WukLamark.Models;

/// <summary>
/// Represents a custom map marker location saved by the player.
/// </summary>
[Serializable]
public class Marker
{
    /// <summary>
    /// Unique identifier for this marker.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User-defined name for this marker.
    /// </summary>
    public string Name { get; set; } = "Unnamed Location";

    /// <summary>
    /// World position where the marker was created (X, Y, Z coordinates).
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Territory ID where this marker is located.
    /// </summary>
    public ushort TerritoryId { get; set; }

    /// <summary>
    /// Ward ID for this marker location. (Only relevant for residential areas)
    /// </summary>
    public sbyte WardId { get; set; }

    /// <summary>
    /// Map ID for this marker location.
    /// </summary>
    public uint MapId { get; set; }

    /// <summary>
    /// World ID for this marker location.
    /// </summary>
    /// <remarks>Used to differentiate markers between different worlds.</remarks>
    public uint WorldId { get; set; }

    /// <summary>
    /// When true, this marker is treated as global and shown across all worlds/data centers.
    /// </summary>
    public bool AppliesToAllWorlds { get; set; } = false;

    /// <summary>
    /// Timestamp when this marker was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Optional notes or description for this marker.
    /// </summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Group this marker belongs to. Null means ungrouped.
    /// </summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// Stores the icon information for this marker, including source type, shape, 
    /// game/custom icon, size, color, and visibility radius.
    /// </summary>
    /// <remarks>
    /// Priority of icon rendering: Custom Icon -> Game Icon -> Shape.
    /// </remarks>
    public MarkerIcon Icon { get; set; } = new MarkerIcon
    {
        SourceType = MarkerIconType.Shape,
        Shape = MarkerShape.Circle,
        Size = 0.0f,
        UseShapeColor = false
    };

    /// <summary>
    /// Persistence scope — Personal (character-specific) or Shared (all characters on this install).
    /// </summary>
    public MarkerScope Scope { get; set; } = MarkerScope.Personal;

    /// <summary>
    /// Truncated SHA-256 hash of the character's content ID who created this marker.
    /// Used for personal marker scoping without storing raw content IDs.
    /// For shared markers, this tracks the creator for read-only enforcement.
    /// Only relevant when <see cref="Scope"/> is <see cref="MarkerScope.Personal"/> or when IsReadOnly is true.
    /// </summary>
    public string? CharacterHash { get; set; }

    /// <summary>
    /// Determines if this shared marker can only be modified by its creator.
    /// When true, only the character whose hash matches CharacterHash can edit/delete this marker.
    /// </summary>
    public bool IsReadOnly { get; set; } = false;

    #region Obsolete Properties

    /// <summary>
    /// Maximum distance (in yalms) at which this marker is visible on the map/minimap.
    /// A value of 0 means the marker is always visible regardless of distance.
    /// </summary>
    [Obsolete("Use the Icon property instead.")]
    [JsonInclude]
    public float VisibilityRadius { get; set; } = 0f;

    /// <summary>
    /// Gets or sets the geometric shape used to represent the marker.
    /// </summary>
    /// <remarks>The selected shape determines how the marker is visually displayed in the user interface or
    /// on a map. When <see cref="IconId"/> is set, the icon takes precedence over the shape.</remarks>
    [Obsolete("Use the Icon property instead.")]
    [JsonInclude]
    public MarkerShape Shape { get; set; } = MarkerShape.Circle;

    /// <summary>
    /// Custom color for this marker (RGBA).
    /// </summary>
    [Obsolete("Use the Icon property instead.")]
    [JsonInclude]
    public Vector4 Color { get; set; } = new Vector4(1.0f, 0.8f, 0.0f, 1.0f); // Gold/yellow default

    /// <summary>
    /// Game icon ID for rendering this marker. Loaded via ITextureProvider.
    /// When null, the <see cref="Shape"/> and <see cref="Color"/> are used for rendering instead.
    /// </summary>
    /// <remarks>
    /// Game icons render with their original RGB colors from the game's .tex files; RGB tinting is not applied.
    /// However, their alpha may be modified by the renderer.
    /// </remarks>
    [Obsolete("Use the Icon property instead.")]
    [JsonInclude]
    public uint? IconId { get; set; }

    /// <summary>
    /// The size the icon should be rendered at.
    /// </summary>
    /// <remarks>
    /// If null or 0, the default size from the plugin configuration is used.
    /// </remarks>
    [Obsolete("Use the Icon property instead.")]
    [JsonInclude]
    public float? IconSize { get; set; } = 0.0f;

    /// <summary>
    /// Whether to use the shape color when rendering the game icon on the map and minimap. 
    /// </summary>
    [Obsolete("Use the Icon property instead.")]
    [JsonInclude]
    public bool UseShapeColorOnIcon { get; set; } = false;
    #endregion
}
