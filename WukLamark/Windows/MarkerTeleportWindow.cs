using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using WukLamark.Services;

namespace WukLamark.Windows
{
    public class MarkerTeleportWindow : IDisposable
    {
        private readonly MarkerTeleportMapService service;
        private readonly Plugin plugin;

        internal MarkerTeleportWindow(MarkerTeleportMapService service, Plugin plugin)
        {
            this.service = service;
            this.plugin = plugin;
            Plugin.PluginInterface.UiBuilder.Draw += Draw;
        }

        private void Draw()
        {
            if (!Plugin.ClientState.IsLoggedIn) return;
            //if (!plugin.Configuration.ShowMarkersOnAethernet) return;

            if (service.MapCenterScreenPos == null) return; // TelepotTown not fully loaded/visible

            var drawList = ImGui.GetBackgroundDrawList();
            var mousePos = ImGui.GetMousePos();
            string? hoveredMarkerName = null;

            // Iterate through pre-calculated markers from the Service
            foreach (var (position, shape, size, colorU32, name, iconId) in service.MarkersToRender)
            {
                MarkerRenderer.RenderMarker(drawList, position, shape, size, colorU32, iconId);

                // Display tooltip if enabled and mouse is hovering within marker bounds
                if (plugin.Configuration.ShowWaymarkTooltips &&
                    !string.IsNullOrEmpty(name) &&
                    Vector2.Distance(mousePos, position) <= size + 2.0f)
                {
                    hoveredMarkerName = name;
                }
            }

            // Display tooltip for hovered marker
            if (hoveredMarkerName != null)
            {
                ImGui.SetTooltip(hoveredMarkerName);
            }
        }

        public void Dispose()
        {
            Plugin.PluginInterface.UiBuilder.Draw -= Draw;
            GC.SuppressFinalize(this);
        }
    }
}
