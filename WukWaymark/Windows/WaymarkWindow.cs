using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using WukWaymark.Models;

namespace WukWaymark.Windows
{
    /// <summary>
    /// Renders custom waymark markers on the full Area Map (AreaMap addon).
    /// Displays saved waymark locations as colored circles with tooltips showing waymark names.
    /// </summary>
    public unsafe class WaymarkWindow : IDisposable
    {
        private readonly Plugin plugin;

        public WaymarkWindow(Plugin plugin)
        {
            this.plugin = plugin;
            Plugin.PluginInterface.UiBuilder.Draw += DrawWaymarkOverlay;
        }

        /// <summary>
        /// Calculates the scaling multiplier for converting world coordinates to screen pixels
        /// based on the AreaMap zoom slider position and UI scale.
        /// </summary>
        /// <param name="zoomIndex">The zoom slider value (0 = zoomed out, 7 = zoomed in)</param>
        /// <param name="uiScale">The current UI scaling factor (AreaMap->Scale)</param>
        /// <returns>The effective multiplier to apply to world deltas</returns>
        private static float GetMultiplier(float zoomIndex, float uiScale)
        {
            // Keep zoom index within expected bounds (0 to 7)
            var x = Math.Clamp(zoomIndex, 0, 7);

            // Quadratic scaling formula (empirically derived for UI scale 1.0)
            // Formula: multiplier = (107x^2 + x + 750) / 3000
            var result = ((107f * x * x) + x + 750f) / 3000f;

            // Scale the result by the current UI HUD scale setting
            result *= uiScale;
            return result;
        }


        /// <summary>
        /// Main rendering method called every frame by UiBuilder.Draw.
        /// Renders waymark markers on the map overlay.
        /// </summary>
        private void DrawWaymarkOverlay()
        {
            if (!plugin.Configuration.WaymarksEnabled)
                return;

            // ═══════════════════════════════════════════════════════════════
            // STEP 1: Validate AreaMap addon state
            // ═══════════════════════════════════════════════════════════════

            var areaMapAddonPtr = Plugin.GameGui.GetAddonByName("AreaMap");
            if (areaMapAddonPtr == IntPtr.Zero)
                return; // AreaMap UI not open

            var areaMap = (AtkUnitBase*)areaMapAddonPtr.Address;
            if (areaMap == null || !areaMap->IsVisible || areaMap->UldManager.LoadedState != AtkLoadState.Loaded)
                return; // AreaMap not fully loaded or visible

            var agentMap = AgentMap.Instance();
            if (agentMap == null || agentMap->CurrentMapId == 0)
                return; // No valid map loaded

            // ═══════════════════════════════════════════════════════════════
            // STEP 2: Locate critical UI nodes
            // ═══════════════════════════════════════════════════════════════

            // Node 53: Main map container component
            var mapComponent = (AtkComponentNode*)areaMap->GetComponentNodeById(53);
            if (mapComponent == null) return;

            // Node 7: Player marker image (compass/cone graphic)
            var imageNode = (AtkImageNode*)Marshal.ReadIntPtr(areaMapAddonPtr, 0x3B8);

            // ═══════════════════════════════════════════════════════════════
            // STEP 3: Create full-screen overlay window
            // ═══════════════════════════════════════════════════════════════

            ImGuiHelpers.ForceNextWindowMainViewport();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGuiHelpers.MainViewport.Size);
            ImGui.SetNextWindowBgAlpha(0); // Fully transparent

            if (ImGui.Begin("##WaymarkMapOverlay",
                ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoInputs |
                ImGuiWindowFlags.NoBackground |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav))
            {
                var drawList = ImGui.GetWindowDrawList();

                // ═══════════════════════════════════════════════════════════════
                // STEP 4: Extract map state for coordinate calculations
                // ═══════════════════════════════════════════════════════════════

                var slider = (AtkComponentSlider*)areaMap->GetNodeById(16)->GetComponent();
                var zoomIndex = slider->Value;
                var zoneScale = agentMap->CurrentMapSizeFactorFloat;

                // Map center in world space is treated as origin (0, 0, 0)
                var mapCenterWorldPos = Vector3.Zero;

                // ═══════════════════════════════════════════════════════════════
                // STEP 5: Calculate static map center screen position
                // ═══════════════════════════════════════════════════════════════

                Vector2 mapCenterScreenPos;
                var player = Plugin.ObjectTable.LocalPlayer;

                if (player != null && imageNode->IsVisible())
                {
                    // When player is in current zone: back-calculate true map center
                    // Node 7 shows player's actual position, so we can derive where (0,0,0) would be

                    var node = &imageNode->AtkResNode;
                    float nodeX, nodeY;
                    node->GetPositionFloat(&nodeX, &nodeY);

                    // Get player marker center in screen space
                    var playerMarkerCenterX = nodeX + (node->Width / 2f * node->ScaleX);
                    var playerMarkerCenterY = nodeY + (node->Height / 2f * node->ScaleY);

                    // Apply UI chrome offsets
                    var mapOffsetX = 16.0f * areaMap->Scale;
                    var mapOffsetY = 52.0f * areaMap->Scale;

                    var playerScreenX = areaMap->X - mapOffsetX + (playerMarkerCenterX * areaMap->Scale);
                    var playerScreenY = areaMap->Y + mapOffsetY + (playerMarkerCenterY * areaMap->Scale);

                    // Calculate player's offset from world origin in screen space
                    var multiplier = GetMultiplier(zoomIndex, areaMap->Scale);
                    var playerOffsetX = player.Position.X * zoneScale * multiplier;
                    var playerOffsetY = player.Position.Z * zoneScale * multiplier;

                    // Back-calculate map center: player screen pos - player offset from origin
                    mapCenterScreenPos = new Vector2(
                        playerScreenX - playerOffsetX,
                        playerScreenY - playerOffsetY
                    );
                }
                else
                {
                    // When viewing other territories: Node 7 is centered on map
                    var node = &imageNode->AtkResNode;
                    float nodeX, nodeY;
                    node->GetPositionFloat(&nodeX, &nodeY);

                    var markerCenterX = nodeX + (node->Width / 2f * node->ScaleX);
                    var markerCenterY = nodeY + (node->Height / 2f * node->ScaleY);

                    var mapOffsetX = 16.0f * areaMap->Scale;
                    var mapOffsetY = 52.0f * areaMap->Scale;

                    mapCenterScreenPos = new Vector2(
                        areaMap->X - mapOffsetX + (markerCenterX * areaMap->Scale),
                        areaMap->Y + mapOffsetY + (markerCenterY * areaMap->Scale)
                    );
                }

                // Debug: Draw purple dot at calculated map center
                //drawList.AddCircleFilled(mapCenterScreenPos, 5.0f, 0xFFFF00FF);

                // ═══════════════════════════════════════════════════════════════
                // STEP 6: Get map bounds for edge clamping
                // ═══════════════════════════════════════════════════════════════

                var clipNode = mapComponent->Component->UldManager.SearchNodeById(0);
                if (clipNode == null) clipNode = &mapComponent->AtkResNode;

                float mapBoxX, mapBoxY;
                clipNode->GetPositionFloat(&mapBoxX, &mapBoxY);

                var mapMinX = areaMap->X + (mapBoxX * areaMap->Scale);
                var mapMinY = areaMap->Y + (mapBoxY * areaMap->Scale);
                var mapMaxX = mapMinX + (clipNode->Width * clipNode->ScaleX * areaMap->Scale);
                var mapMaxY = mapMinY + (clipNode->Height * clipNode->ScaleY * areaMap->Scale);

                // ═══════════════════════════════════════════════════════════════
                // STEP 7: Process and render waymarks
                // ═══════════════════════════════════════════════════════════════

                var mousePos = ImGui.GetMousePos();
                string? hoveredWaymarkName = null;

                foreach (var waymark in plugin.Configuration.Waymarks)
                {
                    // Only show waymarks for the current map (regardless of territory)
                    if (waymark.MapId != agentMap->SelectedMapId)
                        continue;

                    // Convert waymark's world position to screen space coordinates relative to map center
                    // Calculate world-space delta from map center to waymark
                    var deltaWorldX = waymark.Position.X - mapCenterWorldPos.X;
                    var deltaWorldY = waymark.Position.Z - mapCenterWorldPos.Z;

                    var multiplier = GetMultiplier(zoomIndex, areaMap->Scale);

                    // Transform world delta to screen delta relative to map center
                    var waymarkScreenX = mapCenterScreenPos.X + (deltaWorldX * zoneScale * multiplier);
                    var waymarkScreenY = mapCenterScreenPos.Y + (deltaWorldY * zoneScale * multiplier);

                    // Clamp waymark position to map boundaries to prevent markers from being drawn off-screen
                    var outOfBoundsX = waymarkScreenX < mapMinX || waymarkScreenX > mapMaxX;
                    var outOfBoundsY = waymarkScreenY < mapMinY || waymarkScreenY > mapMaxY;

                    if (outOfBoundsX || outOfBoundsY)
                    {
                        // Ray-to-rectangle intersection
                        var rayDirX = waymarkScreenX - mapCenterScreenPos.X;
                        var rayDirY = waymarkScreenY - mapCenterScreenPos.Y;

                        var tPointX = float.MaxValue;
                        if (rayDirX > 0)
                            tPointX = (mapMaxX - mapCenterScreenPos.X) / rayDirX;
                        else if (rayDirX < 0)
                            tPointX = (mapMinX - mapCenterScreenPos.X) / rayDirX;

                        var tPointY = float.MaxValue;
                        if (rayDirY > 0)
                            tPointY = (mapMaxY - mapCenterScreenPos.Y) / rayDirY;
                        else if (rayDirY < 0)
                            tPointY = (mapMinY - mapCenterScreenPos.Y) / rayDirY;

                        var tIntersect = Math.Min(Math.Abs(tPointX), Math.Abs(tPointY));

                        if (tIntersect < 1.0f)
                        {
                            waymarkScreenX = mapCenterScreenPos.X + (rayDirX * tIntersect);
                            waymarkScreenY = mapCenterScreenPos.Y + (rayDirY * tIntersect);
                        }
                    }

                    // Final safety clamp
                    waymarkScreenX = Math.Clamp(waymarkScreenX, mapMinX, mapMaxX);
                    waymarkScreenY = Math.Clamp(waymarkScreenY, mapMinY, mapMaxY);

                    var waymarkScreenPos = new Vector2(waymarkScreenX, waymarkScreenY);

                    // Render the waymark using its configured shape
                    var colorU32 = ImGui.ColorConvertFloat4ToU32(waymark.Color);
                    var markerSize = plugin.Configuration.WaymarkMarkerSize;

                    WaymarkRenderer.RenderWaymarkShape(drawList, waymarkScreenPos, waymark.Shape, markerSize, colorU32);

                    // Display tooltip if enabled and mouse is hovering within marker bounds
                    if (plugin.Configuration.ShowWaymarkTooltips &&
                        !string.IsNullOrEmpty(waymark.Name) &&
                        Vector2.Distance(mousePos, waymarkScreenPos) <= markerSize + 2.0f)
                    {
                        hoveredWaymarkName = waymark.Name;
                    }
                }

                // Display tooltip for hovered waymark
                if (hoveredWaymarkName != null)
                {
                    ImGui.SetTooltip(hoveredWaymarkName);
                }
                ImGui.End();
            }
        }

        public void Dispose()
        {
            Plugin.PluginInterface.UiBuilder.Draw -= DrawWaymarkOverlay;
            GC.SuppressFinalize(this);
        }
    }
}
