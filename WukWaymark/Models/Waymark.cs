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
    /// on a map. Ensure that the assigned value is supported by the rendering system in use.</remarks>
    public WaymarkShape Shape { get; set; } = WaymarkShape.Circle;
}
