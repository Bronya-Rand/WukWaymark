using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using System.Numerics;
using WukLamark.Helpers;
using WukLamark.Render;
using WukLamark.Services;

namespace WukLamark.Windows
{
    /// <summary>
    /// Renders custom markers on the UI Minimap (_NaviMap addon).
    /// 
    /// This window overlays markers on the small corner minimap that appears
    /// during gameplay, allowing players to see marker locations without opening the full map.
    /// </summary>
    internal sealed class MarkerMinimapWindow : Window
    {
        private readonly MarkerMinimapService service;
        private readonly Plugin plugin;

        internal bool IsEnabled { get; set; }

        public MarkerMinimapWindow(MarkerMinimapService minimapService, Plugin plugin) : base("MarkerMinimapWindow###MarkerMinimap")
        {
            service = minimapService;
            this.plugin = plugin;

            // Configure as transparent, non-interactive overlay
            Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus;

            ForceMainWindow = true;
            IsOpen = true;
        }

        public override void PreDraw()
        {
            var viewport = ImGui.GetWindowViewport();
            var windowPos = viewport.Pos;

            service.PrepareRender(windowPos);

            var minimapInfo = service.GetMinimapBounds();

            Position = minimapInfo.Position;
            Size = minimapInfo.Size;
        }

        public override void Draw()
        {
            var drawList = ImGui.GetWindowDrawList();

            var mousePos = ImGui.GetMousePos();
            string? hoveredMarkerName = null;

            foreach (var (position, markerIcon, fadedColor, markerSize, name, notes) in service.MarkersToRender)
            {
                MarkerRenderer.RenderMarker(drawList, position, markerIcon, markerSize, fadedColor);
                if (!plugin.Configuration.ShowWaymarkTooltips) continue;
                if (Vector2.Distance(mousePos, position) > markerIcon.Size + (2.0f * ImGuiHelpers.GlobalScale)) continue;

                var safeName = name.IsNullOrEmpty() ? "Unnamed Marker" : name;
                var formattedNotes = MapHelper.FormatMapTooltipNotes(notes);

                if (formattedNotes.Length > 0)
                    hoveredMarkerName = $"{safeName}\n{formattedNotes}";
                else
                    hoveredMarkerName = safeName;
            }

            if (hoveredMarkerName != null)
            {
                ImGui.SetTooltip(hoveredMarkerName);
            }
        }
        public override bool DrawConditions()
        {
            return Plugin.ClientState.IsLoggedIn &&
                !NaviMapStateReader.IsUIFading() &&
                IsEnabled;
        }
    }
}
