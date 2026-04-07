using Lumina.Excel.Sheets;
using System.Collections.Generic;

namespace WukLamark.Utils;

public static class LocationHelper
{
    private static readonly Dictionary<uint, string> TerritoryNameCache = [];
    private static readonly Dictionary<uint, (string WorldName, string DataCenterName)> WorldInfoCache = [];

    // For use in search filtering by DC
    private static readonly Dictionary<string, HashSet<uint>> DataCenterWorldIdCache = new(System.StringComparer.Ordinal);

    private static bool WorldCacheInitialized;
    private static bool TerritoryCacheInitialized;

    #region Initialization
    public static void InitializeWorldCache()
    {
        if (WorldCacheInitialized) return;

        var worldSheet = Plugin.DataManager.GetExcelSheet<World>();
        if (worldSheet == null) return;

        foreach (var worldRow in worldSheet)
        {
            var worldId = worldRow.RowId;
            if (worldId == 0) continue;

            var worldName = worldRow.Name.ToString();
            var dataCenterName = worldRow.DataCenter.Value.Name.ToString();

            // Cache world info
            WorldInfoCache[worldId] = (worldName, dataCenterName);

            if (!DataCenterWorldIdCache.TryGetValue(dataCenterName, out var worldIds))
            {
                worldIds = [];
                DataCenterWorldIdCache[dataCenterName] = worldIds;
            }

            // Cache world ID under its data center for q
            worldIds.Add(worldId);
        }
        WorldCacheInitialized = true;
    }

    public static void InitializeTerritoryCache()
    {
        if (TerritoryCacheInitialized) return;

        var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        if (territorySheet == null) return;

        foreach (var territoryRow in territorySheet)
        {
            var territoryId = territoryRow.RowId;
            if (territoryId == 0) continue;
            var name = territoryRow.PlaceName.Value.Name.ToString();
            TerritoryNameCache[territoryId] = name;
        }
        TerritoryCacheInitialized = true;
    }
    #endregion

    public static IReadOnlyCollection<uint> GetWorldIdsForDataCenter(string dataCenterName)
    {
        return DataCenterWorldIdCache.TryGetValue(dataCenterName, out var worldIds) ? worldIds : [];
    }

    /// <summary>
    /// Returns a formatted location string, optionally including ward and world info.
    /// </summary>
    /// <remarks>
    /// Location may return a ward ID if the location is in a residential zone. 
    /// Location may return a world ID if the location is in a different world than the player's current world.
    /// </remarks>
    public static string GetLocationName(ushort territoryId, uint worldId, sbyte wardId, bool appliesToAllWorlds)
    {
        var territoryName = GetTerritoryName(territoryId);
        if (wardId >= 0)
            territoryName += $" - Ward {wardId + 1}";

        if (appliesToAllWorlds)
            return territoryName;

        var (targetWorldName, targetDcName) = GetWorldAndDataCenterName(worldId);

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return $"{territoryName} ({targetDcName} - {targetWorldName})";

        var playerWorldId = player.CurrentWorld.RowId;
        if (playerWorldId == worldId)
            return territoryName;

        var (_, playerDcName) = GetWorldAndDataCenterName(playerWorldId);

        if (playerDcName == targetDcName)
            return $"{territoryName} ({targetWorldName})";

        return $"{territoryName} ({targetDcName} - {targetWorldName})";
    }

    /// <summary>
    /// Resolves and caches the place name for a territory ID.
    /// </summary>
    public static string GetTerritoryName(ushort territoryId)
    {
        if (TerritoryNameCache.TryGetValue(territoryId, out var cachedName))
            return cachedName;

        // Fallback
        InitializeTerritoryCache();

        if (TerritoryNameCache.TryGetValue(territoryId, out var territoryName))
            return territoryName;

        var unknownName = $"Unknown (ID: {territoryId})";
        TerritoryNameCache[territoryId] = unknownName;
        return unknownName;
    }

    /// <summary>
    /// Retrieves the world and data center names associated with the specified world identifier.
    /// </summary>
    /// <param name="worldId">The worid ID to lookup info for.</param>
    /// <returns>A tuple containing the world name and data center name corresponding to the specified world identifier. If the
    /// world identifier is not recognized, returns a tuple with an 'Unknown' label and the provided identifier.</returns>
    public static (string WorldName, string DataCenterName) GetWorldAndDataCenterName(uint worldId)
    {
        if (WorldInfoCache.TryGetValue(worldId, out var cachedInfo))
            return cachedInfo;

        // Fallback
        InitializeWorldCache();

        if (WorldInfoCache.TryGetValue(worldId, out var worldInfo))
            return worldInfo;

        var unknownInfo = ($"Unknown (ID: {worldId})", "Unknown DC");
        WorldInfoCache[worldId] = unknownInfo;
        return unknownInfo;
    }
}
