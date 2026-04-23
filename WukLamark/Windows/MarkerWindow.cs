using System;
using WukLamark.Render;
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
        private readonly IMapMarkerRender imGuiRenderer;
        private readonly IMapMarkerRender ktkRenderer;

        internal MarkerWindow(MarkerMapService service, Plugin plugin)
        {
            this.service = service;
            imGuiRenderer = new ImGuiMapMarkerRender(plugin);
            ktkRenderer = new KtkMapMarkerRender(plugin);
            Plugin.PluginInterface.UiBuilder.Draw += Draw;
        }

        /// <summary>
        /// Main rendering method called every frame by UiBuilder.Draw.
        /// Iterates the pre-calculated marker list from the service and renders them.
        /// </summary>
        private void Draw()
        {
            if (!Plugin.ClientState.IsLoggedIn) return;
            var mapRenderDisabled = !imGuiRenderer.IsEnabled && !ktkRenderer.IsEnabled;
            if (mapRenderDisabled) return;

            // Do not render if UI is fading (handles the "Gridania | New Gridania" 
            // screen transition when teleporting).
            if (NaviMapStateReader.IsUIFading()) return;
            if (service.MapCenterScreenPos == null) return; // AreaMap not fully loaded/visible

            var renderer = ktkRenderer.IsEnabled ? ktkRenderer : imGuiRenderer;

            renderer.BeginRender();
            foreach (var marker in service.MarkersToRender)
                renderer.RenderMarker(service.SelectedMapId, service.UIScale, marker);
            renderer.EndRender();
        }

        public void Dispose()
        {
            Plugin.PluginInterface.UiBuilder.Draw -= Draw;
            GC.SuppressFinalize(this);
        }
    }
}
