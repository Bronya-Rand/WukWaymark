using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using System;
using System.Numerics;
using WukLamark.Helpers;
using WukLamark.Services;

namespace WukLamark.Windows
{
    /// <summary>
    /// Renders custom markers on the full Area Map (AreaMap addon).
    /// This is a thin rendering layer — all coordinate calculations are done
    /// in <see cref="MarkerMapService"/> during Framework.Update.
    /// </summary>
    public class MarkerWindow : IDisposable
    {
        private readonly MarkerMapService service;
        private readonly Plugin plugin;

        internal MarkerWindow(MarkerMapService service, Plugin plugin)
        {
            this.service = service;
            this.plugin = plugin;
            Plugin.PluginInterface.UiBuilder.Draw += Draw;
        }

        /// <summary>
        /// Main rendering method called every frame by UiBuilder.Draw.
        /// Iterates the pre-calculated marker list from the service and renders them.
        /// </summary>
        private void Draw()
        {
            if (!Plugin.ClientState.IsLoggedIn) return;
            if (!plugin.Configuration.WaymarksMapEnabled) return;

            // Do not render if UI is fading (handles the "Gridania | New Gridania" 
            // screen transition when teleporting).
            if (NaviMapStateReader.IsUIFading()) return;
            if (service.MapCenterScreenPos == null) return; // AreaMap not fully loaded/visible

            var drawList = ImGui.GetBackgroundDrawList();
            var mousePos = ImGui.GetMousePos();
            string? hoveredMarkerName = null;

            // Iterate through pre-calculated markers from the Service
            foreach (var (position, shape, size, colorU32, name, notes, iconId, useShapeColorOnIcon) in service.MarkersToRender)
            {
                MarkerRenderer.RenderMarker(drawList, position, shape, size, colorU32, iconId, useShapeColorOnIcon);

                // Display tooltip if enabled and mouse is hovering within marker bounds
                if (!plugin.Configuration.ShowWaymarkTooltips) continue;
                if (Vector2.Distance(mousePos, position) > size + 2.0f) continue; // Add small padding for easier hovering

                var safeName = name.IsNullOrEmpty() ? "Unnamed Marker" : name;
                var formattedNotes = MapHelper.FormatMapTooltipNotes(notes);

                if (formattedNotes.Length > 0)
                    hoveredMarkerName = $"{safeName}\n{formattedNotes}";
                else
                    hoveredMarkerName = safeName;
            }

            // Display tooltip for hovered marker
            if (hoveredMarkerName != null)
                ImGui.SetTooltip(hoveredMarkerName);
        }

        public void Dispose()
        {
            Plugin.PluginInterface.UiBuilder.Draw -= Draw;
            GC.SuppressFinalize(this);
        }
    }
}
