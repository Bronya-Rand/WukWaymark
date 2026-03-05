using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;

namespace WukWaymark.Helpers
{
    internal class MapHelper
    {
        /// <summary>
        /// Opens the map corresponding to the specified map identifier.
        /// </summary>
        /// <param name="mapId">The unique identifier of the map to open.</param>
        public static unsafe void OpenMap(uint mapId) => AgentMap.Instance()->OpenMapByMapId(mapId);

        /// <summary>
        /// Flags the specified map location and opens the map at that position, optionally displaying a custom title.
        /// </summary>
        /// <param name="position">The world coordinates of the location to flag on the map.</param>
        /// <param name="title">An optional title to display on the map. If null, no custom title is shown.</param>
        public static unsafe void FlagMapLocation(Vector3 position, string? title = null)
        {
            var agent = AgentMap.Instance();
            agent->SetFlagMapMarker(agent->CurrentTerritoryId, agent->CurrentMapId, position);
            agent->OpenMap(agent->CurrentMapId, agent->CurrentTerritoryId, title, MapType.FlagMarker);
        }
    }
}
