using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using WukWaymark.Models;
using WukWaymark.Windows;

namespace WukWaymark.Services
{
    /// <summary>
    /// Service responsible for calculating Waymark positions on the full Area Map (AreaMap addon).
    /// Following the Service + Window paradigm: all game state reading and coordinate math
    /// is done here during Framework.Update; the window only draws the results.
    /// </summary>
    internal class WaymarkMapService : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Configuration configuration;
        private readonly WaymarkWindow window;
        private bool disposed;

        // Pre-calculated waymarks ready for the window to render
        public readonly List<(Vector2 ScreenPos, WaymarkShape Shape, float MarkerSize, uint Color, string? Name, uint? IconId)> WaymarksToRender = [];

        // Cached state from Framework.Update for debug / window access
        public Vector2? MapCenterScreenPos { get; private set; }

        // Map clip bounds for the window layer (needed for edge awareness)
        public float MapMinX { get; private set; }
        public float MapMinY { get; private set; }
        public float MapMaxX { get; private set; }
        public float MapMaxY { get; private set; }

        public WaymarkMapService(Plugin plugin)
        {
            this.plugin = plugin;
            configuration = plugin.Configuration;
            window = new WaymarkWindow(this, plugin);
            Plugin.Framework.Update += OnFrameworkUpdate;
        }

        /// <summary>
        /// Calculates the scaling multiplier for converting world coordinates to screen pixels
        /// based on the AreaMap zoom slider position and UI scale.
        /// </summary>
        private static float GetMultiplier(float zoomIndex, float uiScale)
        {
            var x = Math.Clamp(zoomIndex, 0, 7);
            var result = ((107f * x * x) + x + 750f) / 3000f;
            result *= uiScale;
            return result;
        }

        /// <summary>
        /// Called every frame to gather game state and pre-calculate waymark screen positions.
        /// All coordinate math happens here so the window is a thin rendering layer.
        /// </summary>
        private unsafe void OnFrameworkUpdate(IFramework framework)
        {
            WaymarksToRender.Clear();
            MapCenterScreenPos = null;

            if (!configuration.WaymarksMapEnabled)
                return;

            // Get local player
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
                return;

            // Do not render if UI is fading (handles the "Gridania | New Gridania" 
            // screen transition when teleporting).
            if (!NaviMapStateReader.IsUIFading())
                return;

            // Skip rendering in combat
            var playerCharacter = (Character*)player.Address;
            if (playerCharacter->InCombat)
                return;
            if (playerCharacter->IsInPvP())
                return;

            // ═══════════════════════════════════════════════════════════════
            // STEP 1: Validate AreaMap addon state
            // ═══════════════════════════════════════════════════════════════

            var areaMapAddonPtr = Plugin.GameGui.GetAddonByName("AreaMap");
            if (areaMapAddonPtr.IsNull)
                return;

            var areaMap = (AtkUnitBase*)areaMapAddonPtr.Address;
            if (areaMap == null || !areaMap->IsVisible || areaMap->UldManager.LoadedState != AtkLoadState.Loaded)
                return;

            var agentMap = AgentMap.Instance();
            if (agentMap == null || agentMap->CurrentMapId == 0)
                return;

            // ═══════════════════════════════════════════════════════════════
            // STEP 2: Locate critical UI nodes
            // ═══════════════════════════════════════════════════════════════

            var mapComponent = areaMap->GetComponentNodeById(53);
            if (mapComponent == null) return;

            var imageNode = (AtkImageNode*)Marshal.ReadIntPtr(areaMapAddonPtr, 0x3B8);
            if (imageNode == null) return;

            // ═══════════════════════════════════════════════════════════════
            // STEP 3: Extract map state for coordinate calculations
            // ═══════════════════════════════════════════════════════════════

            var sliderNode = (AtkComponentNode*)areaMap->GetNodeById(16);
            if (sliderNode == null) return;
            var sliderComponent = (AtkComponentSlider*)sliderNode->GetComponent();
            if (sliderComponent == null) return;

            var zoomIndex = sliderComponent->Value;
            var zoneScale = agentMap->CurrentMapSizeFactorFloat;

            // ═══════════════════════════════════════════════════════════════
            // STEP 4: Calculate static map center screen position
            // ═══════════════════════════════════════════════════════════════

            Vector2 mapCenterScreenPos;
            if (imageNode->IsVisible())
            {
                var node = &imageNode->AtkResNode;
                float nodeX, nodeY;
                node->GetPositionFloat(&nodeX, &nodeY);

                var playerMarkerCenterX = nodeX + (node->Width / 2f * node->ScaleX);
                var playerMarkerCenterY = nodeY + (node->Height / 2f * node->ScaleY);

                var mapOffsetX = 16.0f * areaMap->Scale;
                var mapOffsetY = 52.0f * areaMap->Scale;

                var playerScreenX = areaMap->X - mapOffsetX + (playerMarkerCenterX * areaMap->Scale);
                var playerScreenY = areaMap->Y + mapOffsetY + (playerMarkerCenterY * areaMap->Scale);

                var multiplier = GetMultiplier(zoomIndex, areaMap->Scale);
                var playerOffsetX = player.Position.X * zoneScale * multiplier;
                var playerOffsetY = player.Position.Z * zoneScale * multiplier;

                mapCenterScreenPos = new Vector2(
                    playerScreenX - playerOffsetX,
                    playerScreenY - playerOffsetY
                );
            }
            else
            {
                var node = &imageNode->AtkResNode;
                var nodeX = node->X;
                var nodeY = node->Y;

                var markerCenterX = nodeX + (node->Width / 2f * node->ScaleX);
                var markerCenterY = nodeY + (node->Height / 2f * node->ScaleY);

                var globalScale = ImGuiHelpers.GlobalScale;
                var mapOffsetX = 16.0f * areaMap->Scale * globalScale;
                var mapOffsetY = 52.0f * areaMap->Scale * globalScale;

                mapCenterScreenPos = new Vector2(
                    areaMap->X - mapOffsetX + (markerCenterX * areaMap->Scale),
                    areaMap->Y + mapOffsetY + (markerCenterY * areaMap->Scale)
                );
            }

            MapCenterScreenPos = mapCenterScreenPos;

            // ═══════════════════════════════════════════════════════════════
            // STEP 5: Get map bounds for edge clamping
            // ═══════════════════════════════════════════════════════════════

            var clipNode = mapComponent->Component->UldManager.SearchNodeById(0);
            if (clipNode == null) clipNode = &mapComponent->AtkResNode;

            float mapBoxX, mapBoxY;
            clipNode->GetPositionFloat(&mapBoxX, &mapBoxY);

            MapMinX = areaMap->X + (mapBoxX * areaMap->Scale);
            MapMinY = areaMap->Y + (mapBoxY * areaMap->Scale);
            MapMaxX = MapMinX + (clipNode->Width * clipNode->ScaleX * areaMap->Scale);
            MapMaxY = MapMinY + (clipNode->Height * clipNode->ScaleY * areaMap->Scale);

            // ═══════════════════════════════════════════════════════════════
            // STEP 6: Process and pre-calculate waymark screen positions
            // ═══════════════════════════════════════════════════════════════

            var multiplierForWaymarks = GetMultiplier(zoomIndex, areaMap->Scale);
            var mapCenterWorldPos = Vector3.Zero;
            var currentMapId = agentMap->SelectedMapId;

            foreach (var waymark in plugin.WaymarkStorageService.GetVisibleWaymarks())
            {
                // Early culling
                if (waymark.MapId != currentMapId)
                    continue;

                // Visibility radius check
                if (configuration.FadeWaymarkOnMapEdge && waymark.VisibilityRadius > 0)
                {
                    var distSquared = Vector3.DistanceSquared(player.Position, waymark.Position);
                    var radiusSquared = waymark.VisibilityRadius * waymark.VisibilityRadius;

                    if (distSquared > radiusSquared)
                        continue; // Beyond visibility radius — don't render
                }

                var deltaWorldX = waymark.Position.X - mapCenterWorldPos.X;
                var deltaWorldY = waymark.Position.Z - mapCenterWorldPos.Z;

                var waymarkScreenX = mapCenterScreenPos.X + (deltaWorldX * zoneScale * multiplierForWaymarks);
                var waymarkScreenY = mapCenterScreenPos.Y + (deltaWorldY * zoneScale * multiplierForWaymarks);

                // Edge clamping via ray-to-rectangle intersection
                var outOfBoundsX = waymarkScreenX < MapMinX || waymarkScreenX > MapMaxX;
                var outOfBoundsY = waymarkScreenY < MapMinY || waymarkScreenY > MapMaxY;
                var isClamped = false;

                if (outOfBoundsX || outOfBoundsY)
                {
                    var rayDirX = waymarkScreenX - mapCenterScreenPos.X;
                    var rayDirY = waymarkScreenY - mapCenterScreenPos.Y;

                    var tPointX = float.MaxValue;
                    if (rayDirX > 0)
                        tPointX = (MapMaxX - mapCenterScreenPos.X) / rayDirX;
                    else if (rayDirX < 0)
                        tPointX = (MapMinX - mapCenterScreenPos.X) / rayDirX;

                    var tPointY = float.MaxValue;
                    if (rayDirY > 0)
                        tPointY = (MapMaxY - mapCenterScreenPos.Y) / rayDirY;
                    else if (rayDirY < 0)
                        tPointY = (MapMinY - mapCenterScreenPos.Y) / rayDirY;

                    var tIntersect = Math.Min(Math.Abs(tPointX), Math.Abs(tPointY));

                    if (tIntersect < 1.0f)
                    {
                        waymarkScreenX = mapCenterScreenPos.X + (rayDirX * tIntersect);
                        waymarkScreenY = mapCenterScreenPos.Y + (rayDirY * tIntersect);
                        isClamped = true;
                    }
                }

                // Final safety clamp
                waymarkScreenX = Math.Clamp(waymarkScreenX, MapMinX, MapMaxX);
                waymarkScreenY = Math.Clamp(waymarkScreenY, MapMinY, MapMaxY);
                if (waymarkScreenX <= MapMinX || waymarkScreenX >= MapMaxX || waymarkScreenY <= MapMinY || waymarkScreenY >= MapMaxY)
                    isClamped = true;

                var colorU32 = ImGui.ColorConvertFloat4ToU32(waymark.Color);
                var markerSize = configuration.WaymarkMarkerSize * ImGuiHelpers.GlobalScale;
                if (waymark.IconId != null)
                {
                    var iconSize = plugin.IconBrowserService.GetIconSize(waymark.IconId.Value);
                    var deSize = 6.0f / areaMap->Scale * ImGuiHelpers.GlobalScale;
                    if (iconSize.HasValue)
                    {
                        markerSize = iconSize.Value.X / deSize;
                    }
                    else
                    {
                        // Fallback to 64x64 (seems most icons are this size?)
                        markerSize = 64.0f / deSize;
                    }
                }

                // Apply visibility radius fade (last 20% of radius)
                var targetAlpha = 1.0f;
                if (configuration.FadeWaymarkOnMapEdge && waymark.VisibilityRadius > 0)
                {
                    var distSquared = Vector3.DistanceSquared(player.Position, waymark.Position);
                    var fadeStart = waymark.VisibilityRadius * 0.8f;
                    var fadeStartSquared = fadeStart * fadeStart;

                    if (distSquared > fadeStartSquared)
                    {
                        var dist = MathF.Sqrt(distSquared); // Only apply when fading
                        targetAlpha = 1.0f - ((dist - fadeStart) / (waymark.VisibilityRadius - fadeStart));
                    }
                }

                if (configuration.FadeWaymarkOnMapEdge && isClamped)
                {
                    targetAlpha = Math.Min(targetAlpha, configuration.MapEdgeFadeAlpha);
                }

                if (targetAlpha < 1.0f)
                {
                    targetAlpha = Math.Clamp(targetAlpha, 0f, 1f);
                    // Modify the U32 color's alpha channel
                    var originalAlpha = ((colorU32 >> 24) & 0xFF) / 255f;
                    var a = (uint)(targetAlpha * originalAlpha * 255f);
                    colorU32 = (colorU32 & 0x00FFFFFF) | (a << 24);
                }

                WaymarksToRender.Add((new Vector2(waymarkScreenX, waymarkScreenY), waymark.Shape, markerSize, colorU32, waymark.Name, waymark.IconId));
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            Plugin.Framework.Update -= OnFrameworkUpdate;
            window.Dispose();
            disposed = true;
        }
    }
}
