using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace WukLamark.Utils;

public static class LocationHelper
{
    private static readonly Dictionary<ushort, string> TerritoryNameCache = [];
    private static readonly Dictionary<uint, string> WorldNameCache = [];

    /// <summary>
    /// Returns a formatted location string, optionally including ward and world info.
    /// </summary>
    /// <remarks>
    /// Location may return a ward ID if the location is in a residential zone. 
    /// Location may return a world ID if the location is in a different world than the player's current world.
    /// </remarks>
    public static string GetLocationName(ushort territoryId, uint worldId, sbyte wardId)
    {
        var territoryName = GetTerritoryName(territoryId);
        if (wardId >= 0)
            territoryName += $" - Ward {wardId + 1}";

        var worldName = GetWorldName(worldId);

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player != null && player.CurrentWorld.RowId == worldId)
            return territoryName;
        else
            return $"{territoryName} ({worldName})";
    }

    /// <summary>
    /// Resolves and caches the place name for a territory ID.
    /// </summary>
    public static string GetTerritoryName(ushort territoryId)
    {
        if (TerritoryNameCache.TryGetValue(territoryId, out var cachedName))
            return cachedName;

        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            var name = territoryRow.PlaceName.Value.Name.ToString();
            TerritoryNameCache[territoryId] = name;
            return name;
        }

        var unknownName = $"Unknown (ID: {territoryId})";
        TerritoryNameCache[territoryId] = unknownName;
        return unknownName;
    }

    /// <summary>
    /// Resolves and caches the world name for a world ID.
    /// </summary>
    public static string GetWorldName(uint worldId)
    {
        if (WorldNameCache.TryGetValue(worldId, out var cachedName))
            return cachedName;

        if (Plugin.DataManager.GetExcelSheet<World>().TryGetRow(worldId, out var worldRow))
        {
            var name = worldRow.Name.ToString();
            WorldNameCache[worldId] = name;
            return name;
        }

        var unknownName = $"Unknown (ID: {worldId})";
        WorldNameCache[worldId] = unknownName;
        return unknownName;
    }
}
