using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Numerics;

namespace WukWaymark.Windows
{
    /// <summary>
    /// Renders custom waymark markers on the UI Minimap (_NaviMap).
    /// Modeled after MinimapTrackerWindow's window overlay behavior and WaymarkWindow's rendering logic.
    /// </summary>
    public unsafe class WaymarkMinimapWindow : Window, IDisposable
    {
        private readonly Plugin plugin;

        public WaymarkMinimapWindow(Plugin plugin) : base("WaymarkMinimapWindow###WaymarkMinimap")
        {
            this.plugin = plugin;

            // Initial flags - set to invisible overlay
            Flags |= ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus;

            ForceMainWindow = true;
            IsOpen = true;
        }

        private static float GetMultiplier(float zoomIndex, float uiScale)
        {
            // Retain quadratic scaling logic. (May need tuning specific to NaviMap)
            var x = Math.Clamp(zoomIndex, 0, 7);
            var result = ((107f * x * x) + x + 750f) / 3000f;
            result *= uiScale;
            return result;
        }

        public override void PreDraw()
        {
            var naviMapAddonPtr = Plugin.GameGui.GetAddonByName("_NaviMap");
            if (naviMapAddonPtr == IntPtr.Zero)
                return;

            var naviMap = (AtkUnitBase*)naviMapAddonPtr.Address;
            if (naviMap == null || !naviMap->IsVisible)
                return;

            // Optional bounds set, matching MinimapTrackerWindow inspiration
            var rootNode = naviMap->RootNode;
            if (rootNode != null)
            {
                Position = new Vector2(naviMap->X, naviMap->Y);
                Size = new Vector2(rootNode->Width * naviMap->Scale, rootNode->Height * naviMap->Scale);
            }
        }

        public override void Draw()
        {
            if (!plugin.Configuration.WaymarksEnabled)
                return;

            var naviMapAddonPtr = Plugin.GameGui.GetAddonByName("_NaviMap");
            if (naviMapAddonPtr == IntPtr.Zero)
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

            var isLocked = ((AtkComponentCheckBox*)naviMap->GetNodeById(4)->GetComponent())->IsChecked;
            var rotation = naviMap->GetNodeById(8)->Rotation;
            var zoom = naviMap->GetNodeById(18)->GetComponent()->GetImageNodeById(6)->ScaleX;
            var naviScale = naviMap->Scale;
            var zoneScale = agentMap->CurrentMapSizeFactorFloat * 1.0f;

            var viewportPos = ImGui.GetWindowViewport().Pos;

            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * naviScale, naviMapSize * naviScale);
            var minimapRadius = mapSize.X * 0.315f;

            // Minimap center screen position
            var mapCenterScreenPos = new Vector2(
                naviMap->X + mapSize.X / 2f,
                naviMap->Y + mapSize.Y / 2f
            ) + viewportPos;

            // Adjust Y to line up with minimap pivot better
            mapCenterScreenPos.Y -= 5f;

            var mousePos = ImGui.GetMousePos();
            string? hoveredWaymarkName = null;

            foreach (var waymark in plugin.Configuration.Waymarks)
            {
                if (waymark.MapId != agentMap->SelectedMapId)
                    continue;

                // Map offsets based on MinimapTrackerService logic
                // The Circle position should be the center minus the relative position
                var relativeOffset = new Vector2(
                    player.Position.X - waymark.Position.X,
                    player.Position.Z - waymark.Position.Z
                );

                relativeOffset *= zoneScale;
                relativeOffset *= naviScale;
                relativeOffset *= zoom;

                var waymarkScreenPos = mapCenterScreenPos - relativeOffset;

                // Apply rotation if unlocked
                if (!isLocked)
                {
                    waymarkScreenPos = RotatePoint(mapCenterScreenPos, waymarkScreenPos, rotation);
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
                var markerSize = plugin.Configuration.WaymarkMarkerSize;

                WaymarkRenderer.RenderWaymarkShape(drawList, waymarkScreenPos, waymark.Shape, markerSize, colorU32);

                if (plugin.Configuration.ShowWaymarkTooltips &&
                    !string.IsNullOrEmpty(waymark.Name) &&
                    Vector2.Distance(mousePos, waymarkScreenPos) <= markerSize + 2.0f)
                {
                    hoveredWaymarkName = waymark.Name;
                }
            }

            if (hoveredWaymarkName != null)
            {
                ImGui.SetTooltip(hoveredWaymarkName);
            }
        }

        private static Vector2 RotatePoint(Vector2 center, Vector2 pos, float angle)
        {
            var cosTheta = Math.Cos(angle);
            var sinTheta = Math.Sin(angle);

            var rotatedPoint = new Vector2
            {
                X = (float)(cosTheta * (pos.X - center.X) - sinTheta * (pos.Y - center.Y) + center.X),
                Y = (float)(sinTheta * (pos.X - center.X) + cosTheta * (pos.Y - center.Y) + center.Y)
            };

            return rotatedPoint;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
