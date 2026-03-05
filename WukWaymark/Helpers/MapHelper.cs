using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;

namespace WukWaymark.Helpers
{
    /// <summary>
    /// Helper class providing convenient methods for interacting with the in-game map system.
    /// </summary>
    internal class MapHelper
    {
        /// <summary>
        /// Opens the map corresponding to the specified map identifier.
        /// This displays the full area map for the given map, centered on the player.
        /// </summary>
        /// <param name="mapId">The unique identifier of the map to open (from TerritoryType.Map.RowId)</param>
        public static unsafe void OpenMap(uint mapId) => AgentMap.Instance()->OpenMapByMapId(mapId);

        /// <summary>
        /// Flags a specific map location and opens the map at that position.
        /// 
        /// This creates a temporary flag marker on the map at the specified coordinates
        /// and opens the map UI centered on that location.
        /// </summary>
        /// <param name="position">The world coordinates of the location to flag (X, Y, Z)</param>
        /// <param name="title">Optional custom title to display on the flagged location. If null, no title is shown.</param>
        public static unsafe void FlagMapLocation(Vector3 position, string? title = null)
        {
            var agent = AgentMap.Instance();
            agent->SetFlagMapMarker(agent->CurrentTerritoryId, agent->CurrentMapId, position);
            agent->OpenMap(agent->CurrentMapId, agent->CurrentTerritoryId, title, MapType.FlagMarker);
        }
    }
}
