using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Windows;

namespace WukLamark.Services
{
    /// <summary>
    /// Service responsible for calculating marker positions on the Minimap.
    /// Following the Service + Window paradigm to decouple logic and rendering.
    /// </summary>
    internal class MarkerMinimapService : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Configuration configuration;
        private readonly MarkerMinimapWindow window;
        private readonly WindowSystem windowSystem;
        private bool disposed;

        // Publicly accessible list mapping markers to their PreDraw evaluated locations
        public List<(Vector2 Position, MarkerShape Shape, float Size, Vector4 Color, string? Name, uint? IconId, bool useShapeColorOnIcon)> MarkersToRender { get; } = [];

        // Player data gathered during Framework.Update
        private readonly List<(Vector3 WorldPos, MarkerShape Shape, Vector4 Color, string? Name, uint? IconId, float? IconScale, float VisibilityRadius, bool useShapeColorOnIcon)> markersToRenderCache = [];

        // Minimap state cached per frame
        private float minimapRadius;
        private Vector2 mapCenterScreenPos;
        private Vector2 playerWorldPos;
        private float zoneScale;
        private float naviScale;
        private float zoom;
        private float rotation;
        private bool isLocked;
        private float globalScale;

        // naviMap screen position cache — avoids GetAddonByName in PrepareRender/GetMinimapBounds
        private float naviMapX;
        private float naviMapY;

        // cos/sin computed from _rotation cache — computed once per frame
        private float sinRotation;
        private float cosRotation;

        public MarkerMinimapService(Plugin plugin)
        {
            this.plugin = plugin;
            configuration = plugin.Configuration;
            windowSystem = plugin.WindowSystem;
            window = new MarkerMinimapWindow(this, plugin);

            // Register the window directly with the passed in system to centralize tracking
            windowSystem.AddWindow(window);

            // Hook into framework update
            Plugin.Framework.Update += OnFrameworkUpdate;
        }

        /// <summary>
        /// Updates the minimap state and caches relevant data for rendering.
        /// </summary>
        /// <param name="framework">The framework instance.</param>
        private unsafe void OnFrameworkUpdate(IFramework framework)
        {
            // Clear cached render lists
            markersToRenderCache.Clear();
            window.IsEnabled = configuration.WaymarksMinimapEnabled;

            if (!Plugin.ClientState.IsLoggedIn) return;
            if (!configuration.WaymarksMinimapEnabled)
                return;

            // Get Housing Manager
            var housingManager = HousingManager.Instance();
            if (housingManager == null) return;
            var wardId = housingManager->GetCurrentWard();

            // Get local player
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return;
            var currentWorldId = localPlayer.CurrentWorld.RowId;

            // Skip rendering in combat
            var playerCharacter = (Character*)localPlayer.Address;
            if (playerCharacter->InCombat)
                return;
            if (playerCharacter->IsInPvP())
                return;

            // Get the minimap addon
            var naviMapAddonPtr = Plugin.GameGui.GetAddonByName("_NaviMap");
            if (naviMapAddonPtr.IsNull)
                return;

            var naviMapAddon = (AddonNaviMap*)naviMapAddonPtr.Address;
            if (naviMapAddon == null || !naviMapAddon->IsVisible || naviMapAddon->UldManager.LoadedState != AtkLoadState.Loaded)
                return;

            // Read all minimap state in a single pass
            if (!NaviMapStateReader.TryReadMinimapState(naviMapAddon, out var minimapLocked, out var minimapRotation, out var minimapZoom))
                return;

            isLocked = minimapLocked;
            rotation = minimapRotation;
            zoom = minimapZoom;
            naviScale = naviMapAddon->Scale;
            globalScale = ImGuiHelpers.GlobalScale;

            // Cache naviMap screen position so PrepareRender/GetMinimapBounds don't need GetAddonByName
            naviMapX = naviMapAddon->X;
            naviMapY = naviMapAddon->Y;

            // Pre-compute cos/sin once per frame
            cosRotation = MathF.Cos(rotation);
            sinRotation = MathF.Sin(rotation);

            // Get AgentMap for zone scale
            var agentMap = AgentMap.Instance();
            if (agentMap == null || agentMap->CurrentMapId == 0)
                return;

            // CurrentMapSizeFactorFloat returns values like 0.02 for a zone scale of 2
            // Multiply by 1.0f to convert it to a proper scale factor (e.g. 2.0 for a zone scale of 2)
            zoneScale = agentMap->CurrentMapSizeFactorFloat * 1.0f;

            // Cache player world position
            playerWorldPos = new Vector2(localPlayer.Position.X, localPlayer.Position.Z);

            // Calculate minimap radius
            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * naviScale, naviMapSize * naviScale);
            minimapRadius = mapSize.X * 0.315f;

            // Cache markers to render
            foreach (var marker in plugin.MarkerStorageService.GetVisibleMarkers())
            {
                // Minimap early culling
                if (!marker.AppliesToAllWorlds && marker.WorldId != currentWorldId)
                    continue; // Wrong world
                if (marker.MapId != agentMap->CurrentMapId)
                    continue; // Wrong map
                if (marker.WardId != -1 && marker.WardId != wardId)
                    continue; // Wrong ward (for housing areas)

                // Visibility radius check using squared distance (avoids sqrt)
                if (configuration.FadeWaymarkOnMinimapEdge && marker.VisibilityRadius > 0)
                {
                    var distSquared = Vector3.DistanceSquared(localPlayer.Position, marker.Position);
                    var radiusSquared = marker.VisibilityRadius * marker.VisibilityRadius;
                    if (distSquared > radiusSquared)
                        continue;
                }

                // Store world position - will convert to screen coords in PreDraw
                markersToRenderCache.Add((marker.Position, marker.Shape, marker.Color, marker.Name, marker.IconId, marker.IconSize, marker.VisibilityRadius, marker.UseShapeColorOnIcon));
            }
        }

        /// <summary>
        /// Prepares the markers for rendering by converting their world positions to screen coordinates.
        /// </summary>
        /// <param name="windowPos">The position of the window.</param>
        internal unsafe void PrepareRender(Vector2 windowPos)
        {
            MarkersToRender.Clear();

            if (markersToRenderCache.Count == 0)
                return;

            // Use cached naviMap position instead of re-querying GetAddonByName
            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * naviScale, naviMapSize * naviScale);

            mapCenterScreenPos = new Vector2(
                naviMapX + (mapSize.X / 2f),
                naviMapY + (mapSize.Y / 2f)
            ) + windowPos;

            mapCenterScreenPos.Y -= 5f * globalScale;

            // Pass pre-computed cos/sin to avoid recomputing per marker
            foreach (var (worldPos, shape, color, name, iconId, iconSize, visibilityRadius, useShapeColorOnIcon) in markersToRenderCache)
            {
                var circlePos = CalculateCirclePosition(worldPos, cosRotation, sinRotation);

                var baseMarkerSize = configuration.WaymarkMarkerSize;
                // Override base size if marker has an explicit size set
                if (iconSize.HasValue && iconSize.Value > 0)
                    baseMarkerSize = iconSize.Value;
                var markerSize = baseMarkerSize * globalScale;

                if (iconId != null)
                {
                    var iconGameSize = plugin.IconBrowserService.GetIconSize(iconId.Value);
                    var deSize = 6.0f / naviScale * globalScale;
                    if (iconGameSize.HasValue)
                    {
                        if (plugin.IconBrowserService.IconIsIcon(iconId.Value))
                            markerSize = iconGameSize.Value.X / deSize * (baseMarkerSize / 8.0f);
                        else
                            // Non-map icons are larger than 64.
                            markerSize = 32.0f / deSize * (baseMarkerSize / 8.0f);
                    }
                    else
                    {
                        var fallbackBase = plugin.IconBrowserService.IconIsIcon(iconId.Value) ? 64.0f : 32.0f;
                        markerSize = fallbackBase / deSize * (baseMarkerSize / 8.0f);
                    }
                }

                // Apply visibility radius alpha fade (last 20% of radius)
                var fadedColor = color;
                if (configuration.FadeWaymarkOnMinimapEdge && visibilityRadius > 0)
                {
                    // Use squared distance to avoid sqrt, only compute sqrt when fading
                    var dx = playerWorldPos.X - worldPos.X;
                    var dy = playerWorldPos.Y - worldPos.Z;
                    var distSquared = (dx * dx) + (dy * dy);

                    var fadeStart = visibilityRadius * 0.8f;
                    var fadeStartSquared = fadeStart * fadeStart;

                    if (distSquared > fadeStartSquared)
                    {
                        var dist = MathF.Sqrt(distSquared); // Only sqrt when actually fading
                        var alpha = 1.0f - ((dist - fadeStart) / (visibilityRadius - fadeStart));
                        fadedColor.W = Math.Clamp(alpha, 0f, 1f) * color.W;
                    }
                }

                // Minimap border fade — reduce alpha when marker is clamped to edge
                var screenDist = Vector2.Distance(mapCenterScreenPos, circlePos);
                if (configuration.FadeWaymarkOnMinimapEdge && screenDist >= minimapRadius * 0.95f)
                {
                    var edgeFade = 1.0f - ((screenDist - (minimapRadius * 0.95f)) / (minimapRadius * 0.05f));
                    fadedColor.W *= Math.Clamp(edgeFade, 0.4f, 1.0f);
                }

                MarkersToRender.Add((circlePos, shape, markerSize, fadedColor, name, iconId, useShapeColorOnIcon));
            }
        }

        #region Coordinate Transformation

        /// <summary>
        /// Calculates the screen position of a marker relative to the minimap center.
        /// </summary>
        /// <param name="markerPosition">The world position of the marker.</param>
        /// <param name="cosTheta">The cosine of the rotation angle.</param>
        /// <param name="sinTheta">The sine of the rotation angle.</param>
        /// <returns>The screen position of the marker, or null if it is outside the minimap bounds.</returns>
        private Vector2 CalculateCirclePosition(Vector3 markerPosition, float cosTheta, float sinTheta)
        {
            var relativeOffset = new Vector2(
                playerWorldPos.X - markerPosition.X,
                playerWorldPos.Y - markerPosition.Z
            );

            relativeOffset *= zoneScale;
            relativeOffset *= naviScale;
            relativeOffset *= zoom;

            var markerScreenPos = mapCenterScreenPos - relativeOffset;

            if (!isLocked)
            {
                var dx = markerScreenPos.X - mapCenterScreenPos.X;
                var dy = markerScreenPos.Y - mapCenterScreenPos.Y;
                markerScreenPos = new Vector2(
                    (cosTheta * dx) - (sinTheta * dy) + mapCenterScreenPos.X,
                    (sinTheta * dx) + (cosTheta * dy) + mapCenterScreenPos.Y
                );
            }

            var distance = Vector2.Distance(mapCenterScreenPos, markerScreenPos);
            if (distance > minimapRadius)
            {
                var originToObject = markerScreenPos - mapCenterScreenPos;
                originToObject *= minimapRadius / distance;
                markerScreenPos = mapCenterScreenPos + originToObject;
            }

            return markerScreenPos;
        }

        /// <summary>
        /// Gets the screen position and size of the minimap.
        /// </summary>
        /// <returns>The screen position and size of the minimap.</returns>
        internal (Vector2 Position, Vector2 Size) GetMinimapBounds()
        {
            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * naviScale, naviMapSize * naviScale);
            // Use cached _naviMapX/_naviMapY
            var mapPos = new Vector2(naviMapX, naviMapY);
            return (mapPos, mapSize);
        }

        #endregion
        public void Dispose()
        {
            if (disposed)
                return;

            Plugin.Framework.Update -= OnFrameworkUpdate;
            Plugin.Log.Information("MarkerMinimapService disposed.");
            // Window removal happens from Plugin.Dispose
            disposed = true;
        }
    }
}
