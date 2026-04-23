using Dalamud.Utility;
using System.Numerics;
using WukLamark.Helpers;
using WukLamark.Services;
using MapMarkerInfo = KamiToolKit.Classes.MapMarkerInfo;

namespace WukLamark.Render
{
    internal sealed class KtkMapMarkerRender(Plugin plugin) : IMapMarkerRender
    {
        public bool IsEnabled => plugin.Configuration.WaymarksMapEnabled && plugin.MapOverlayController != null && plugin.Configuration.UseKTK;
        public void BeginRender()
        {
            plugin.MapOverlayController?.RemoveAllMarkers();
        }

        public void RenderMarker(uint selectedMapId, MapMarkerData markerInfo)
        {
            if (markerInfo.IconId == null) return;

            var safeName = markerInfo.Name.IsNullOrEmpty() ? "Unnamed Marker" : markerInfo.Name;
            var formattedNotes = MapHelper.FormatMapTooltipNotes(markerInfo.Notes);
            var tooltipText = formattedNotes.Length > 0 ? $"{safeName}\n{formattedNotes}" : safeName;

            // Makes Icons 32x32 in size (dependent on calculations)
            var iconSize = new Vector2(markerInfo.MarkerSize * 2, markerInfo.MarkerSize * 2);

            plugin.MapOverlayController!.AddMarker(new MapMarkerInfo
            {
                AllowAnyMap = false,
                MapId = selectedMapId,
                Position = markerInfo.WorldPos,
                IconId = markerInfo.IconId.Value,
                Tooltip = tooltipText,
                Size = iconSize
            });
        }
        public void EndRender() { }
    }
}
