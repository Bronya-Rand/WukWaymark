using Dalamud.Configuration;
using System;
using WukLamark.Models;

namespace WukLamark;

[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>Configuration version for tracking schema changes</summary>
    public int Version { get; set; } = 0;

    /// <summary>
    /// Enable or disable rendering of map markers on the in-game map overlay.
    /// When disabled, markers are still saved but not displayed.
    /// Property name kept as 'WaymarksMapEnabled' for backward compatibility with existing configs.
    /// </summary>
    public bool WaymarksMapEnabled { get; set; } = true;

    /// <summary>
    /// Enable or disable rendering of markers on the in-game minimap.
    /// Property name kept as 'WaymarksMinimapEnabled' for backward compatibility.
    /// </summary>
    public bool WaymarksMinimapEnabled { get; set; } = true;

    /// <summary>
    /// Radius (in pixels) of markers displayed on the map.
    /// Valid range: 4.0 to 20.0 pixels.
    /// Affects all markers uniformly via this global setting.
    /// Property name kept as 'WaymarkMarkerSize' for compatibility.
    /// </summary>
    public float WaymarkMarkerSize { get; set; } = 8.0f;

    /// <summary>
    /// Show tooltips displaying marker names when hovering over markers on the map.
    /// Useful for quickly identifying markers without opening the UI.
    /// Property name kept as 'ShowWaymarkTooltips' for compatibility.
    /// </summary>
    public bool ShowWaymarkTooltips { get; set; } = true;

    /// <summary>
    /// When true, the main window shows Group View (collapsible group headers).
    /// When false, shows Table View (flat list of all markers with search).
    /// </summary>
    public bool UseGroupView { get; set; } = false;

    /// <summary>
    /// Enable fading for markers clamped to the edge of the minimap.
    /// Property name kept as 'FadeWaymarkOnMinimapEdge'.
    /// </summary>
    public bool FadeWaymarkOnMinimapEdge { get; set; } = true;

    /// <summary>
    /// Enable fading for markers clamped to the edge of the area map.
    /// Property name kept as 'FadeWaymarkOnMapEdge'.
    /// </summary>
    public bool FadeWaymarkOnMapEdge { get; set; } = true;

    /// <summary>
    /// The target alpha to fade to when a marker is mapped to the edge of the area map/minimap.
    /// Default is 0.4f. Range: 0.1f to 1.0f.
    /// </summary>
    public float MapEdgeFadeAlpha { get; set; } = 0.4f;

    /// <summary>
    /// Default shape applied to newly created markers.
    /// Users can customize individual markers after creation.
    /// Options: Circle, Square, Triangle, Diamond, Star
    /// Property name kept as 'DefaultWaymarkShape' for compatibility.
    /// </summary>
    public MarkerShape DefaultWaymarkShape { get; set; } = MarkerShape.Circle;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
