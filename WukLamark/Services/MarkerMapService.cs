using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using WukLamark.Helpers;
using WukLamark.Models;
using WukLamark.Windows;

namespace WukLamark.Services
{
    public sealed record MarkerMapRenderData(
        Vector2 ScreenPosition,
        MarkerShape Shape,
        float Size,
        uint ColorU32,
        string Name,
        string? Notes,
        uint? GameIconId,
        string? CustomIconName,
        bool UseShapeColor);

    /// <summary>
    /// Service responsible for calculating marker positions on the full Area Map (AreaMap addon).
    /// </summary>
    internal sealed class MarkerMapService : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Configuration configuration;
        private readonly MarkerWindow window;
        private bool disposed;

        // Default marker size in pixels
        private const float DefaultMapMarkerPx = 48.0f;
        private Vector2 defaultMapMarkerSize = new(DefaultMapMarkerPx, DefaultMapMarkerPx);

        public readonly List<MarkerMapRenderData> MarkersToRender = [];

        // Cached state from Framework.Update for debug / window access
        public Vector2? MapCenterScreenPos { get; private set; }

        // Map clip bounds for the window layer
        public float MapMinX { get; private set; }
        public float MapMinY { get; private set; }
        public float MapMaxX { get; private set; }
        public float MapMaxY { get; private set; }

        public MarkerMapService(Plugin plugin)
        {
            this.plugin = plugin;
            configuration = plugin.Configuration;
            window = new MarkerWindow(this, plugin);
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
        /// Called every frame to gather game state and pre-calculate marker screen positions.
        /// All coordinate math happens here so the window is a thin rendering layer.
        /// </summary>
        private unsafe void OnFrameworkUpdate(IFramework framework)
        {
            MarkersToRender.Clear();
            MapCenterScreenPos = null;

            if (!Plugin.ClientState.IsLoggedIn) return;
            if (!configuration.WaymarksMapEnabled)
                return;

            // Get Housing Manager
            var housingManager = HousingManager.Instance();
            if (housingManager == null) return;
            var wardId = housingManager->GetCurrentWard();

            // Get local player
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
                return;
            var currentWorldId = player.CurrentWorld.RowId;

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
            var zoneScale = agentMap->SelectedMapSizeFactorFloat;

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
            // STEP 6: Process and pre-calculate marker screen positions
            // ═══════════════════════════════════════════════════════════════

            var multiplierForMarkers = GetMultiplier(zoomIndex, areaMap->Scale);
            var mapCenterWorldPos = Vector3.Zero;
            var currentMapId = agentMap->SelectedMapId;

            foreach (var marker in plugin.MarkerStorageService.GetVisibleMarkers())
            {
                // Early culling
                if (!marker.AppliesToAllWorlds && marker.WorldId != currentWorldId)
                    continue; // Wrong world
                if (marker.MapId != currentMapId)
                    continue; // Wrong map
                if (marker.WardId != -1 && marker.WardId != wardId)
                    continue; // Wrong ward (for housing areas)

                // Visibility radius check
                if (configuration.FadeWaymarkOnMapEdge && marker.Icon.VisibilityRadius > 0)
                {
                    var distSquared = Vector3.DistanceSquared(player.Position, marker.Position);
                    var radiusSquared = marker.Icon.VisibilityRadius * marker.Icon.VisibilityRadius;

                    if (distSquared > radiusSquared)
                        continue; // Beyond visibility radius — don't render
                }

                var deltaWorldX = marker.Position.X - mapCenterWorldPos.X;
                var deltaWorldY = marker.Position.Z - mapCenterWorldPos.Z;

                var markerScreenX = mapCenterScreenPos.X + (deltaWorldX * zoneScale * multiplierForMarkers);
                var markerScreenY = mapCenterScreenPos.Y + (deltaWorldY * zoneScale * multiplierForMarkers);

                // Edge clamping via ray-to-rectangle intersection
                var outOfBoundsX = markerScreenX < MapMinX || markerScreenX > MapMaxX;
                var outOfBoundsY = markerScreenY < MapMinY || markerScreenY > MapMaxY;
                var isClamped = false;

                if (outOfBoundsX || outOfBoundsY)
                {
                    var rayDirX = markerScreenX - mapCenterScreenPos.X;
                    var rayDirY = markerScreenY - mapCenterScreenPos.Y;

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
                        markerScreenX = mapCenterScreenPos.X + (rayDirX * tIntersect);
                        markerScreenY = mapCenterScreenPos.Y + (rayDirY * tIntersect);
                        isClamped = true;
                    }
                }

                // Final safety clamp
                markerScreenX = Math.Clamp(markerScreenX, MapMinX, MapMaxX);
                markerScreenY = Math.Clamp(markerScreenY, MapMinY, MapMaxY);
                if (markerScreenX <= MapMinX || markerScreenX >= MapMaxX || markerScreenY <= MapMinY || markerScreenY >= MapMaxY)
                    isClamped = true;

                var colorU32 = ImGui.ColorConvertFloat4ToU32(marker.Icon.Color);
                var baseMarkerSize = configuration.WaymarkMarkerSize;
                // Override base size if marker has an explicit size set
                if (marker.Icon.Size > 0.0)
                    baseMarkerSize = marker.Icon.Size;
                var markerSize = baseMarkerSize * ImGuiHelpers.GlobalScale;

                if (marker.Icon.GameIconId != null || !marker.Icon.CustomIconName.IsNullOrEmpty())
                {
                    var deSize = 6.0f / areaMap->Scale * ImGuiHelpers.GlobalScale;

                    Vector2 iconSize;
                    if (!marker.Icon.CustomIconName.IsNullOrEmpty())
                        iconSize = IconHelper.GetCustomIconSize(marker.Icon.CustomIconName) ?? defaultMapMarkerSize;
                    else if (marker.Icon.GameIconId.HasValue)
                        iconSize = IconHelper.GetGameIconSize(marker.Icon.GameIconId.Value) ?? defaultMapMarkerSize;
                    else
                    {
                        Plugin.Log.Warning($"Marker {marker.Name} has no valid icon size, using default.");
                        iconSize = defaultMapMarkerSize;
                    }

                    if (iconSize.X > 64f || iconSize.Y > 64f)
                        markerSize = DefaultMapMarkerPx / deSize * (baseMarkerSize / 8.0f);
                    else
                    {
                        // Scale the marker size based on the lowest dimension of the icon
                        var minIconSize = Math.Min(iconSize.X, iconSize.Y);
                        markerSize = minIconSize / deSize * (baseMarkerSize / 8.0f);
                    }
                }

                // Apply visibility radius fade (last 20% of radius)
                var targetAlpha = 1.0f;
                if (configuration.FadeWaymarkOnMapEdge && marker.Icon.VisibilityRadius > 0)
                {
                    var distSquared = Vector3.DistanceSquared(player.Position, marker.Position);
                    var fadeStart = marker.Icon.VisibilityRadius * 0.8f;
                    var fadeStartSquared = fadeStart * fadeStart;

                    if (distSquared > fadeStartSquared)
                    {
                        var dist = MathF.Sqrt(distSquared); // Only apply when fading
                        targetAlpha = 1.0f - ((dist - fadeStart) / (marker.Icon.VisibilityRadius - fadeStart));
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

                MarkersToRender.Add(new MarkerMapRenderData(
                    new Vector2(markerScreenX, markerScreenY),
                    marker.Icon.Shape,
                    markerSize,
                    colorU32,
                    marker.Name,
                    marker.Notes.IsNullOrEmpty() ? null : marker.Notes,
                    marker.Icon.GameIconId,
                    marker.Icon.CustomIconName,
                    marker.Icon.UseShapeColor
                ));
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
