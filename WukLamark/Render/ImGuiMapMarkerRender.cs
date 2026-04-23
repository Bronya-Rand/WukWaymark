using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using System.Numerics;
using WukLamark.Helpers;
using WukLamark.Services;
using WukLamark.Windows;

namespace WukLamark.Render
{
    internal sealed class ImGuiMapMarkerRender(Plugin plugin) : IMapMarkerRender
    {
        private ImDrawListPtr drawList;
        private Vector2 mousePos;
        public bool IsEnabled => plugin.Configuration.WaymarksMapEnabled && !plugin.Configuration.UseKTK;
        public void BeginRender()
        {
            plugin.MapOverlayController?.RemoveAllMarkers();
            drawList = ImGui.GetBackgroundDrawList();
            mousePos = ImGui.GetMousePos();
        }
        public void RenderMarker(uint selectedMapId, MapMarkerData markerInfo)
        {
            var safeName = markerInfo.Name.IsNullOrEmpty() ? "Unnamed Marker" : markerInfo.Name;
            var formattedNotes = MapHelper.FormatMapTooltipNotes(markerInfo.Notes);
            var tooltipText = formattedNotes.Length > 0 ? $"{safeName}\n{formattedNotes}" : safeName;

            MarkerRenderer.RenderMarker(drawList, markerInfo.ScreenPos, markerInfo.Shape, markerInfo.MarkerSize, markerInfo.Color, markerInfo.IconId, markerInfo.UseShapeColorOnIcon);

            if (!plugin.Configuration.ShowWaymarkTooltips) return;
            if (Vector2.Distance(mousePos, markerInfo.ScreenPos) > markerInfo.MarkerSize + 2.0f) return;

            ImGui.SetTooltip(tooltipText);
        }
        public void EndRender() { }
    }
}
