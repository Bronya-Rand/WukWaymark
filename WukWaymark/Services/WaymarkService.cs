using System;
using WukWaymark.Models;
using WukWaymark.Utils;

namespace WukWaymark.Services;

/// <summary>
/// Service class containing business logic for waymark operations.
/// Handles waymark creation, validation, and persistence.
/// </summary>
public class WaymarkService(Configuration configuration)
{
    private readonly Configuration configuration = configuration;

    /// <summary>
    /// Saves the player's current location as a new waymark.
    /// 
    /// This method:
    /// 1. Validates the player is logged in
    /// 2. Retrieves current location (position, territory, map, world)
    /// 3. Creates a waymark with auto-generated name and color
    /// 4. Persists the waymark to configuration
    /// 5. Provides user feedback via chat message
    /// 
    /// Validation errors are reported to the player via error messages.
    /// </summary>
    /// <returns>The created waymark if successful, null if validation failed</returns>
    public Waymark? SaveCurrentLocation()
    {
        // Verify player is logged in
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return null;

        // Get current territory ID
        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current location.");
            return null;
        }

        // Look up the map ID from the territory data
        var mapId = Plugin.ClientState.MapId;
        if (mapId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current map.");
            return null;
        }

        // Get current world ID (used to differentiate waymarks across data centers)
        var currentWorldId = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;
        if (currentWorldId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current world.");
            return null;
        }

        // Create a new waymark with current location data
        var waymark = new Waymark
        {
            Position = player.Position,
            TerritoryId = territoryId,
            MapId = mapId,
            WorldId = currentWorldId,
            Name = $"Waymark {configuration.Waymarks.Count + 1}",
            Color = Colors.GetNextColor(configuration.Waymarks.Count),
            Shape = configuration.DefaultWaymarkShape,
            CreatedAt = DateTime.Now,
        };

        // Persist to configuration and save to disk
        configuration.Waymarks.Add(waymark);
        configuration.Save();

        // Provide user feedback
        Plugin.ChatGui.Print($"[WukWaymark] Saved waymark '{waymark.Name}' at current location.");
        Plugin.Log.Information($"Saved waymark: {waymark.Name} at {waymark.Position} (Territory: {territoryId}, Map: {mapId})");

        return waymark;
    }
}
