using System;
using System.Collections.Generic;

namespace WukLamark.Models;

/// <summary>
/// Container class for player-specific markers and groups.
/// Used for serialization/deserialization of personal marker data files.
/// </summary>
[Serializable]
public class PlayerMarkerData
{
    /// <summary>
    /// List of markers belonging to this player.
    /// </summary>
    /// <remarks>
    /// For historical data compatibility, the JSON property name remains "Waymarks".
    /// </remarks>
    public List<Marker> Waymarks { get; set; } = [];

    /// <summary>
    /// List of groups belonging to this player.
    /// </summary>
    public List<MarkerGroup> Groups { get; set; } = [];
}
