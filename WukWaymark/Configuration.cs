using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using WukWaymark.Models;

namespace WukWaymark;

[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>Configuration version for tracking schema changes</summary>
    public int Version { get; set; } = 0;
    /// <summary>
    /// Collection of all saved waymarks persisted across sessions.
    /// </summary>
    public List<Waymark> Waymarks { get; set; } = [];

    /// <summary>
    /// Enable or disable rendering of waymarks on the in-game map overlay.
    /// When disabled, waymarks are still saved but not displayed.
    /// </summary>
    public bool WaymarksMapEnabled { get; set; } = true;

    /// <summary>
    /// Enable or disable rendering of waymarks on the in-game minimap.
    /// </summary>
    public bool WaymarksMinimapEnabled { get; set; } = true;

    /// <summary>
    /// Radius (in pixels) of waymark markers displayed on the map.
    /// Valid range: 4.0 to 20.0 pixels.
    /// Affects all waymarks uniformly via this global setting.
    /// </summary>
    public float WaymarkMarkerSize { get; set; } = 8.0f;

    /// <summary>
    /// Show tooltips displaying waymark names when hovering over markers on the map.
    /// Useful for quickly identifying waymarks without opening the UI.
    /// </summary>
    public bool ShowWaymarkTooltips { get; set; } = true;

    /// <summary>
    /// Default shape applied to newly created waymarks.
    /// Users can customize individual waymarks after creation.
    /// Options: Circle, Square, Triangle, Diamond, Star
    /// </summary>
    public WaymarkShape DefaultWaymarkShape { get; set; } = WaymarkShape.Circle;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
