using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WukLamark.Models;

/// <summary>
/// Container class for player-specific markers and groups.
/// Used for serialization/deserialization of personal marker data files.
/// </summary>
[Serializable]
public class PlayerMarkerData
{
    /// <summary>
    /// Data schema version for migration tracking.
    /// 0 - Initial version without schema versioning.
    /// 1 - Added schema versioning.
    /// </summary>
    public int SchemaVersion { get; set; } = 0;
    /// <summary>
    /// List of markers belonging to this player.
    /// </summary>
    public List<Marker> Markers { get; set; } = [];

    /// <summary>
    /// Legacy JSON property name for <see cref="Markers"/>
    /// Used only for deserialization
    /// </summary>
    [Obsolete("Use Markers property instead.")]
    [JsonPropertyName("Waymarks")]
    public List<Marker> Waymarks
    {
#pragma warning disable CS8603 // Possible null reference return.
        get => null;
#pragma warning restore CS8603 // Possible null reference return.
        set
        {
            if (value is { Count: > 0 } && Markers.Count == 0)
                Markers = value;
        }
    }

    /// <summary>
    /// List of groups belonging to this player.
    /// </summary>
    public List<MarkerGroup> Groups { get; set; } = [];
}
