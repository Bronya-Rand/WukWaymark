using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
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
    public unsafe class WaymarkMinimapWindow : Window, IDisposable
    {
        private readonly Plugin plugin;
        private bool MinimapLocked { get; set; }
        private float MinimapRotation { get; set; }
        private float MinimapZoom { get; set; }
        public WaymarkMinimapWindow(Plugin plugin) : base("WaymarkMinimapWindow###WaymarkMinimap")
        {
            this.plugin = plugin;

            // Configure as transparent, non-interactive overlay
            Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus;

            ForceMainWindow = true;
            IsOpen = true;
        }

        /// <summary>

        /// Called before rendering to detect and configure the minimap addon.
        /// Ensures the overlay window is positioned and sized correctly to match the minimap.
        /// </summary>
        public override void PreDraw()
        {
            var naviMapAddonPtr = Plugin.GameGui.GetAddonByName("_NaviMap");
            if (naviMapAddonPtr == IntPtr.Zero)
                return;

            var naviMap = (AtkUnitBase*)naviMapAddonPtr.Address;
            if (naviMap == null || !naviMap->IsVisible)
                return;

            var rootNode = naviMap->RootNode;
            if (rootNode != null)
            {
                Position = new Vector2(naviMap->X, naviMap->Y);
                Size = new Vector2(rootNode->Width * naviMap->Scale, rootNode->Height * naviMap->Scale);
            }
        }

        /// <summary>
        /// Renders the waymark markers onto the minimap.
        /// Calculates each waymark's screen position based on player and map data,
        /// and draws the markers using the configured shapes and colors.
        /// </summary>
        public override void Draw()
        {
            if (!plugin.Configuration.WaymarksMinimapEnabled)
                return;

            var naviMapAddonPtr = Plugin.GameGui.GetAddonByName("_NaviMap");
            if (naviMapAddonPtr.IsNull)
                return;

            var naviMap = (AtkUnitBase*)naviMapAddonPtr.Address;
            if (naviMap == null || !naviMap->IsVisible || naviMap->UldManager.LoadedState != AtkLoadState.Loaded)
                return;

            var agentMap = AgentMap.Instance();
            if (agentMap == null || agentMap->CurrentMapId == 0)
                return;

            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
                return;

            var drawList = ImGui.GetWindowDrawList();

            // ═══════════════════════════════════════════════════════════════
            // Calculate screen and bounds for _NaviMap
            // ═══════════════════════════════════════════════════════════════

            var isLocked = NaviMapStateReader.IsMinimapLocked(naviMap);
            if (isLocked == null)
                return;
            MinimapLocked = isLocked.Value;

            var rotation = NaviMapStateReader.GetMinimapRotation(naviMap);
            if (rotation == null)
                return;
            MinimapRotation = rotation.Value;

            var zoom = NaviMapStateReader.GetMinimapZoom(naviMap);
            if (zoom == null)
                return;
            MinimapZoom = zoom.Value;

            var naviScale = naviMap->Scale;
            var zoneScale = agentMap->CurrentMapSizeFactorFloat * 1.0f;

            var viewportPos = ImGui.GetWindowViewport().Pos;

            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * naviScale, naviMapSize * naviScale);
            var minimapRadius = mapSize.X * 0.315f;

            // Minimap center screen position
            var mapCenterScreenPos = new Vector2(
                naviMap->X + (mapSize.X / 2f),
                naviMap->Y + (mapSize.Y / 2f)
            ) + viewportPos;

            var globalScale = Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
            // Adjust Y to line up with minimap pivot better
            mapCenterScreenPos.Y -= 5f * globalScale;

            var mousePos = ImGui.GetMousePos();
            string? hoveredWaymarkName = null;

            foreach (var waymark in plugin.Configuration.Waymarks)
            {
                if (waymark.MapId != agentMap->SelectedMapId)
                    continue;

                // The Circle position should be the center minus the relative position
                var relativeOffset = new Vector2(
                    player.Position.X - waymark.Position.X,
                    player.Position.Z - waymark.Position.Z
                );

                relativeOffset *= zoneScale;
                relativeOffset *= naviScale;
                relativeOffset *= MinimapZoom;

                var waymarkScreenPos = mapCenterScreenPos - relativeOffset;

                // Apply rotation if unlocked
                if (!MinimapLocked)
                {
                    waymarkScreenPos = RotatePoint(mapCenterScreenPos, waymarkScreenPos, MinimapRotation);
                }

                // Clamp to minimap radius
                var distance = Vector2.Distance(mapCenterScreenPos, waymarkScreenPos);
                if (distance > minimapRadius)
                {
                    var originToObject = waymarkScreenPos - mapCenterScreenPos;
                    originToObject *= minimapRadius / distance;
                    waymarkScreenPos = mapCenterScreenPos + originToObject;
                }

                var colorU32 = ImGui.ColorConvertFloat4ToU32(waymark.Color);
                var markerSize = plugin.Configuration.WaymarkMarkerSize * globalScale;

                WaymarkRenderer.RenderWaymarkShape(drawList, waymarkScreenPos, waymark.Shape, markerSize, colorU32);

                if (plugin.Configuration.ShowWaymarkTooltips &&
                    !string.IsNullOrEmpty(waymark.Name) &&
                    Vector2.Distance(mousePos, waymarkScreenPos) <= markerSize + (2.0f * globalScale))
                {
                    hoveredWaymarkName = waymark.Name;
                }
            }

            if (hoveredWaymarkName != null)
            {
                ImGui.SetTooltip(hoveredWaymarkName);
            }
        }

        /// <summary>
        /// Rotates a point around a center by a given angle.
        /// Used for waymark rotation when the minimap is unlocked.
        /// </summary>
        private static Vector2 RotatePoint(Vector2 center, Vector2 pos, float angle)
        {
            var cosTheta = Math.Cos(angle);
            var sinTheta = Math.Sin(angle);

            var rotatedPoint = new Vector2
            {
                X = (float)((cosTheta * (pos.X - center.X)) - (sinTheta * (pos.Y - center.Y)) + center.X),
                Y = (float)((sinTheta * (pos.X - center.X)) + (cosTheta * (pos.Y - center.Y)) + center.Y)
            };

            return rotatedPoint;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
