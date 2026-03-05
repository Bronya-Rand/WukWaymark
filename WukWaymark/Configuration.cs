using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using WukWaymark.Models;

namespace WukWaymark;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;

    // ═══════════════════════════════════════════════════════════════
    // Waymark System Configuration
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Collection of all saved waymarks.
    /// </summary>
    public List<Waymark> Waymarks { get; set; } = new();

    /// <summary>
    /// Enable or disable waymark display on the map.
    /// </summary>
    public bool WaymarksEnabled { get; set; } = true;

    /// <summary>
    /// Size of waymark markers (radius in pixels).
    /// </summary>
    public float WaymarkMarkerSize { get; set; } = 8.0f;

    /// <summary>
    /// Show tooltips when hovering over waymark markers.
    /// </summary>
    public bool ShowWaymarkTooltips { get; set; } = true;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
