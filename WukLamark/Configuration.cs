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
    /// When true, the main window shows Group View (collapsible group headers).
    /// When false, shows Table View (flat list of all waymarks with search).
    /// </summary>
    public bool UseGroupView { get; set; } = false;

    /// <summary>
    /// Enable fading for waymarks clamped to the edge of the minimap.
    /// </summary>
    public bool FadeWaymarkOnMinimapEdge { get; set; } = true;

    /// <summary>
    /// Enable fading for waymarks clamped to the edge of the area map.
    /// </summary>
    public bool FadeWaymarkOnMapEdge { get; set; } = true;

    /// <summary>
    /// The target alpha to fade to when a waymark is mapped to the edge of the area map/minimap.
    /// Default is 0.4f. Range: 0.1f to 1.0f.
    /// </summary>
    public float MapEdgeFadeAlpha { get; set; } = 0.4f;

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
