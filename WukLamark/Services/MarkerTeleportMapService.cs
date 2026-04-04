using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Utils;
using WukLamark.Windows;

namespace WukLamark.Services
{
    internal class MarkerTeleportMapService : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Configuration configuration;
        private readonly MarkerTeleportWindow window;
        private bool disposed;

        public readonly List<(Vector2 ScreenPos, MarkerShape Shape, float MarkerSize, uint Color, string? Name, uint? IconId)> MarkersToRender = [];

        // Cached state from Framework.Update for debug / window access
        public Vector2? MapCenterScreenPos { get; private set; }

        // Map clip bounds for the window layer
        public float MapMinX { get; private set; }
        public float MapMinY { get; private set; }
        public float MapMaxX { get; private set; }
        public float MapMaxY { get; private set; }

        public MarkerTeleportMapService(Plugin plugin)
        {
            this.plugin = plugin;
            this.configuration = plugin.Configuration;
            this.window = new MarkerTeleportWindow(this, plugin);
            Plugin.Framework.Update += OnFrameworkUpdate;
        }

        private unsafe void OnFrameworkUpdate(IFramework framework)
        {
            MarkersToRender.Clear();
            MapCenterScreenPos = null;

            if (!Plugin.ClientState.IsLoggedIn) return;
            //if (!configuration.ShowMarkersOnAethernet) return;

            // Get Housing Manager
            var housingManager = HousingManager.Instance();
            if (housingManager == null) return;
            var wardId = housingManager->GetCurrentWard();

            // Get local player
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
                return;
            var currentWorldId = player.CurrentWorld.RowId;

            // ═══════════════════════════════════════════════════════════════
            // Valiidate "TelepotTown" addon state
            // ═══════════════════════════════════════════════════════════════

            var teleportTownAddonPtr = Plugin.GameGui.GetAddonByName("TelepotTown");
            if (teleportTownAddonPtr.IsNull) return;

            var teleportTown = (AtkUnitBase*)teleportTownAddonPtr.Address;
            if (teleportTown == null || !teleportTown->IsVisible || teleportTown->UldManager.LoadedState != AtkLoadState.Loaded) return;

            // The actual map component
            var teleportAreaMap = teleportTown->GetComponentNodeById(21);
            if (teleportAreaMap == null) return;

            var teleportAreaMapResNode = teleportTown->GetNodeById(10);
            if (teleportAreaMapResNode == null) return;

            // TODO: Find out how AgentTelepotTown handles what map to display
            var agentMap = AgentMap.Instance();
            if (agentMap == null || agentMap->CurrentMapId == 0) return;

            // ═══════════════════════════════════════════════════════════════
            // Locate UI nodes
            // ═══════════════════════════════════════════════════════════════

            var mapComponent = teleportTown->GetComponentNodeById(21);
            if (mapComponent == null) return;

            var imageNode = (AtkImageNode*)mapComponent->Component->GetNodeById(7);
            if (imageNode == null) return;

            // ═══════════════════════════════════════════════════════════════
            // Extract map state for coordinate calculations
            // ═══════════════════════════════════════════════════════════════

            // Zoom Index appears to be set to 3 on the teleport map
            var zoomIndex = 3;
            var zoneScale = agentMap->SelectedMapSizeFactorFloat;

            Plugin.Log.Verbose($"AgentMap SelectedMapId: {agentMap->SelectedMapId}, MapSizeFactor: {agentMap->SelectedMapSizeFactorFloat}, CurrentMapId: {agentMap->CurrentMapId}");

            var actualX = teleportTown->X + (teleportAreaMapResNode->X * teleportTown->Scale);
            // Lower Y by 18 due to map extending up to header bar versus AreaMap
            var actualY = teleportTown->Y + (teleportAreaMapResNode->Y * teleportTown->Scale) - (18 * teleportTown->Scale);

            Vector2 mapCenterScreenPos;
            if (imageNode->IsVisible())
            {
                var node = &imageNode->AtkResNode;
                float nodeX, nodeY;
                node->GetPositionFloat(&nodeX, &nodeY);

                var playerMarkerCenterX = nodeX + (node->Width / 2f * node->ScaleX);
                var playerMarkerCenterY = nodeY + (node->Height / 2f * node->ScaleY);

                var mapOffsetX = 16.0f * teleportTown->Scale;
                var mapOffsetY = 52.0f * teleportTown->Scale;

                var playerScreenX = actualX - mapOffsetX + (playerMarkerCenterX * teleportTown->Scale);
                var playerScreenY = actualY + mapOffsetY + (playerMarkerCenterY * teleportTown->Scale);

                var multiplier = Calculate.GetMultiplier(zoomIndex, teleportTown->Scale);
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
                var mapOffsetX = 16.0f * teleportTown->Scale * globalScale;
                var mapOffsetY = 52.0f * teleportTown->Scale * globalScale;

                mapCenterScreenPos = new Vector2(
                    actualX - mapOffsetX + (markerCenterX * teleportTown->Scale),
                    actualY + mapOffsetY + (markerCenterY * teleportTown->Scale)
                );
            }

            MapCenterScreenPos = mapCenterScreenPos;

            // ═══════════════════════════════════════════════════════════════
            // Get map bounds for clipping
            // ═══════════════════════════════════════════════════════════════

            var clipNode = teleportAreaMapResNode;

            float mapBoxX, mapBoxY;
            clipNode->GetPositionFloat(&mapBoxX, &mapBoxY);

            MapMinX = teleportTown->X + ((mapBoxX + teleportAreaMap->X) * teleportTown->Scale);
            MapMinY = teleportTown->Y + ((mapBoxY + teleportAreaMap->Y) * teleportTown->Scale) + (18 * teleportTown->Scale); // Lower Y by 18 due to map extending up to header bar versus AreaMap
            MapMaxX = MapMinX + (clipNode->Width * clipNode->ScaleX * teleportTown->Scale) - (18 * teleportTown->Scale); // Somehow the reduction of Y works here for X?
            MapMaxY = MapMinY + (clipNode->Height * clipNode->ScaleY * teleportTown->Scale) - (36 * teleportTown->Scale); // Double the Y reduction to account for min adjustment

            // ═══════════════════════════════════════════════════════════════
            // Process and pre-calculate marker screen positions
            // ═══════════════════════════════════════════════════════════════

            var multiplierForMarkers = Calculate.GetMultiplier(zoomIndex, teleportTown->Scale);
            var mapCenterWorldPos = Vector3.Zero;
            var currentMapId = agentMap->SelectedMapId;

            foreach (var marker in plugin.MarkerStorageService.GetVisibleMarkers())
            {
                // Early culling
                if (marker.WorldId != currentWorldId)
                    continue; // Wrong world
                if (marker.MapId != currentMapId)
                    continue; // Wrong map
                if (marker.WardId != -1 && marker.WardId != wardId)
                    continue; // Wrong ward (for housing areas)

                // Visibility radius check
                if (configuration.FadeWaymarkOnMapEdge && marker.VisibilityRadius > 0)
                {
                    var distSquared = Vector3.DistanceSquared(player.Position, marker.Position);
                    var radiusSquared = marker.VisibilityRadius * marker.VisibilityRadius;

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

                var colorU32 = ImGui.ColorConvertFloat4ToU32(marker.Color);
                var markerSize = configuration.WaymarkMarkerSize * ImGuiHelpers.GlobalScale;
                if (marker.IconId != null)
                {
                    var iconSize = plugin.IconBrowserService.GetIconSize(marker.IconId.Value);
                    var deSize = 6.0f / teleportTown->Scale * ImGuiHelpers.GlobalScale;
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
                if (configuration.FadeWaymarkOnMapEdge && marker.VisibilityRadius > 0)
                {
                    var distSquared = Vector3.DistanceSquared(player.Position, marker.Position);
                    var fadeStart = marker.VisibilityRadius * 0.8f;
                    var fadeStartSquared = fadeStart * fadeStart;

                    if (distSquared > fadeStartSquared)
                    {
                        var dist = MathF.Sqrt(distSquared); // Only apply when fading
                        targetAlpha = 1.0f - ((dist - fadeStart) / (marker.VisibilityRadius - fadeStart));
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

                MarkersToRender.Add((new Vector2(markerScreenX, markerScreenY), marker.Shape, markerSize, colorU32, marker.Name, marker.IconId));
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
