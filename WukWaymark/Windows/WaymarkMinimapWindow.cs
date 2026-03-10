using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using System.Numerics;
using WukWaymark.Services;

namespace WukWaymark.Windows
{
    /// <summary>
    /// Renders custom waymark markers on the UI Minimap (_NaviMap addon).
    /// 
    /// This window overlays waymark markers on the small corner minimap that appears
    /// during gameplay, allowing players to see waymark locations without opening the full map.
    /// </summary>
    internal class WaymarkMinimapWindow : Window
    {
        private readonly WaymarkMinimapService service;
        private readonly Plugin plugin;

        internal bool IsEnabled { get; set; }

        public WaymarkMinimapWindow(WaymarkMinimapService minimapService, Plugin waymark) : base("WaymarkMinimapWindow###WaymarkMinimap")
        {
            service = minimapService;
            plugin = waymark;

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
            string? hoveredWaymarkName = null;

            foreach (var (position, shape, size, color, name, iconId) in service.WaymarksToRender)
            {
                var colorU32 = ImGui.ColorConvertFloat4ToU32(color);

                WaymarkRenderer.RenderWaymark(drawList, position, shape, size, colorU32, iconId);

                if (plugin.Configuration.ShowWaymarkTooltips &&
                    !string.IsNullOrEmpty(name) &&
                    Vector2.Distance(mousePos, position) <= size + (2.0f * ImGuiHelpers.GlobalScale))
                {
                    hoveredWaymarkName = name;
                }
            }

            if (hoveredWaymarkName != null)
            {
                ImGui.SetTooltip(hoveredWaymarkName);
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
