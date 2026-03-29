using System;
using System.Collections.Generic;

namespace WukLamark.Models;

/// <summary>
/// Container class for player-specific waymarks and groups.
/// Used for serialization/deserialization of personal waymark data files.
/// </summary>
[Serializable]
public class PlayerWaymarksData
{
    /// <summary>
    /// List of waymarks belonging to this player.
    /// </summary>
    public List<Waymark> Waymarks { get; set; } = [];

    /// <summary>
    /// List of groups belonging to this player.
    /// </summary>
    public List<WaymarkGroup> Groups { get; set; } = [];
}
