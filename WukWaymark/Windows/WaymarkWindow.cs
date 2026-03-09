using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using WukWaymark.Services;

namespace WukWaymark.Windows
{
    /// <summary>
    /// Renders custom waymark markers on the full Area Map (AreaMap addon).
    /// This is a thin rendering layer — all coordinate calculations are done
    /// in <see cref="WaymarkMapService"/> during Framework.Update.
    /// </summary>
    public unsafe class WaymarkWindow : IDisposable
    {
        private readonly WaymarkMapService _service;
        private readonly Plugin _plugin;

        internal WaymarkWindow(WaymarkMapService service, Plugin plugin)
        {
            _service = service;
            _plugin = plugin;
            Plugin.PluginInterface.UiBuilder.Draw += Draw;
        }

        /// <summary>
        /// Main rendering method called every frame by UiBuilder.Draw.
        /// Iterates the pre-calculated waymark list from the service and renders them.
        /// </summary>
        private void Draw()
        {
            if (!_plugin.Configuration.WaymarksMapEnabled)
                return;

            if (_service.MapCenterScreenPos == null)
                return; // AreaMap not fully loaded/visible

            var drawList = ImGui.GetForegroundDrawList();
            var mousePos = ImGui.GetMousePos();
            string? hoveredWaymarkName = null;

            // Iterate through pre-calculated waymarks from the Service
            foreach (var (position, shape, size, colorU32, name, iconId) in _service.WaymarksToRender)
            {
                WaymarkRenderer.RenderWaymark(drawList, position, shape, size, colorU32, iconId);

                // Display tooltip if enabled and mouse is hovering within marker bounds
                if (_plugin.Configuration.ShowWaymarkTooltips &&
                    !string.IsNullOrEmpty(name) &&
                    Vector2.Distance(mousePos, position) <= size + 2.0f)
                {
                    hoveredWaymarkName = name;
                }
            }

            // Display tooltip for hovered waymark
            if (hoveredWaymarkName != null)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(hoveredWaymarkName);
                ImGui.EndTooltip();
            }
        }

        public void Dispose()
        {
            Plugin.PluginInterface.UiBuilder.Draw -= Draw;
            GC.SuppressFinalize(this);
        }
    }
}
