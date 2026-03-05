using Lumina.Excel.Sheets;
using System;
using WukWaymark.Models;
using WukWaymark.Utils;

namespace WukWaymark.Services;

public class WaymarkService
{
    private readonly Configuration configuration;

    public WaymarkService(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public Waymark? SaveCurrentLocation()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] You must be logged in to save a waymark.");
            return null;
        }

        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current location.");
            return null;
        }

        // Get the current map ID from the territory
        uint mapId = 0;
        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            mapId = territoryRow.Map.RowId;
        }

        var currentWorldId = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;
        if (currentWorldId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current world.");
            return null;
        }

        // Create a new waymark
        var waymark = new Waymark
        {
            Position = player.Position,
            TerritoryId = territoryId,
            MapId = mapId,
            WorldId = currentWorldId,
            Name = $"Waymark {configuration.Waymarks.Count + 1}",
            Color = Colors.RandomizeColor(),
            Shape = configuration.DefaultWaymarkShape,
            CreatedAt = DateTime.Now,
        };

        configuration.Waymarks.Add(waymark);
        configuration.Save();

        Plugin.ChatGui.Print($"[WukWaymark] Saved waymark '{waymark.Name}' at current location.");
        Plugin.Log.Information($"Saved waymark: {waymark.Name} at {waymark.Position} (Territory: {territoryId}, Map: {mapId})");

        return waymark;
    }
}
