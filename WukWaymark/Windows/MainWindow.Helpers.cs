using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using WukWaymark.Models;

namespace WukWaymark.Windows;

public partial class MainWindow
{
    private readonly Dictionary<ushort, string> territoryNameCache = [];
    private readonly Dictionary<uint, string> worldNameCache = [];

    private string GetLocationName(ushort territoryId, uint worldId)
    {
        var territoryName = GetTerritoryName(territoryId);
        var worldName = GetWorldName(worldId);

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player != null && player.CurrentWorld.RowId == worldId)
            return territoryName;
        else
            return $"{territoryName} ({worldName})";
    }

    private string GetTerritoryName(ushort territoryId)
    {
        if (territoryNameCache.TryGetValue(territoryId, out var cachedName))
            return cachedName;

        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            var name = territoryRow.PlaceName.Value.Name.ToString();
            territoryNameCache[territoryId] = name;
            return name;
        }

        var unknownName = $"Unknown (ID: {territoryId})";
        territoryNameCache[territoryId] = unknownName;
        return unknownName;
    }

    private string GetWorldName(uint worldId)
    {
        if (worldNameCache.TryGetValue(worldId, out var cachedName))
            return cachedName;

        if (Plugin.DataManager.GetExcelSheet<World>().TryGetRow(worldId, out var worldRow))
        {
            var name = worldRow.Name.ToString();
            worldNameCache[worldId] = name;
            return name;
        }

        var unknownName = $"Unknown (ID: {worldId})";
        worldNameCache[worldId] = unknownName;
        return unknownName;
    }

    private IEnumerable<Waymark> FilterWaymarks(IEnumerable<Waymark> waymarks)
    {
        var filtered = waymarks;

        // Zone filter
        if (filterCurrentZone)
        {
            var currentMapId = Plugin.ClientState.MapId;
            filtered = filtered.Where(w => w.MapId == currentMapId);
        }

        // Text search filter
        if (!string.IsNullOrEmpty(searchFilter))
        {
            filtered = filtered.Where(w =>
                w.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                w.Notes.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                GetTerritoryName(w.TerritoryId).Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
            );
        }

        return filtered;
    }
}
