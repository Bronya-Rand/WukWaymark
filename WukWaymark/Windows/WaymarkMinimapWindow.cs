using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
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
        private readonly WaymarkMinimapService _service;
        private readonly Plugin _plugin;

        internal bool IsEnabled { get; set; }

        public WaymarkMinimapWindow(WaymarkMinimapService service, Plugin plugin) : base("WaymarkMinimapWindow###WaymarkMinimap")
        {
            _service = service;
            _plugin = plugin;

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

            _service.PrepareRender(windowPos);

            var minimapInfo = _service.GetMinimapBounds();

            if (minimapInfo.HasValue)
            {
                Position = minimapInfo.Value.Position;
                Size = minimapInfo.Value.Size;
            }
        }

        public override void Draw()
        {
            var drawList = ImGui.GetWindowDrawList();

            var mousePos = ImGui.GetMousePos();
            string? hoveredWaymarkName = null;

            foreach (var (position, shape, size, color, name) in _service.WaymarksToRender)
            {
                var colorU32 = ImGui.ColorConvertFloat4ToU32(color);

                WaymarkRenderer.RenderWaymarkShape(drawList, position, shape, size, colorU32);

                if (_plugin.Configuration.ShowWaymarkTooltips &&
                    !string.IsNullOrEmpty(name) &&
                    Vector2.Distance(mousePos, position) <= size + (2.0f * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale))
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
            return IsEnabled;
        }
    }
}
