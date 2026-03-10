using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukWaymark.Models;
using WukWaymark.Windows;

namespace WukWaymark.Services
{
    /// <summary>
    /// Service responsible for calculating Waymark positions on the Minimap.
    /// Following the Service + Window paradigm to decouple logic and rendering.
    /// </summary>
    internal class WaymarkMinimapService : IDisposable
    {
        private readonly Plugin plugin;
        private readonly Configuration configuration;
        private readonly WaymarkMinimapWindow window;
        private readonly WindowSystem windowSystem;
        private bool disposed;

        // Publicly accessible list mapping Waymarks to their PreDraw evaluated locations
        public List<(Vector2 Position, WaymarkShape Shape, float Size, Vector4 Color, string? Name, uint? IconId)> WaymarksToRender { get; } = [];

        // Player data gathered during Framework.Update
        private readonly List<(Vector3 WorldPos, WaymarkShape Shape, Vector4 Color, string? Name, uint? IconId, float VisibilityRadius)> waymarksToRender = [];

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

        public WaymarkMinimapService(Plugin plugin)
        {
            this.plugin = plugin;
            configuration = plugin.Configuration;
            windowSystem = plugin.WindowSystem;
            window = new WaymarkMinimapWindow(this, plugin);

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
            waymarksToRender.Clear();
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

            var naviMapAddon = (AtkUnitBase*)naviMapAddonPtr.Address;
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

            // Cache waymarks to render
            foreach (var waymark in plugin.WaymarkStorageService.GetVisibleWaymarks())
            {
                // Minimap early culling
                if (waymark.WorldId != currentWorldId)
                    continue; // Wrong world
                if (waymark.MapId != agentMap->CurrentMapId)
                    continue; // Wrong map
                if (waymark.WardId != -1 && waymark.WardId != wardId)
                    continue; // Wrong ward (for housing areas)

                // Visibility radius check using squared distance (avoids sqrt)
                if (configuration.FadeWaymarkOnMinimapEdge && waymark.VisibilityRadius > 0)
                {
                    var distSquared = Vector3.DistanceSquared(localPlayer.Position, waymark.Position);
                    var radiusSquared = waymark.VisibilityRadius * waymark.VisibilityRadius;
                    if (distSquared > radiusSquared)
                        continue;
                }

                // Store world position - will convert to screen coords in PreDraw
                waymarksToRender.Add((waymark.Position, waymark.Shape, waymark.Color, waymark.Name, waymark.IconId, waymark.VisibilityRadius));
            }
        }

        /// <summary>
        /// Prepares the waymarks for rendering by converting their world positions to screen coordinates.
        /// </summary>
        /// <param name="windowPos">The position of the window.</param>
        internal unsafe void PrepareRender(Vector2 windowPos)
        {
            WaymarksToRender.Clear();

            if (waymarksToRender.Count == 0)
                return;

            // Use cached naviMap position instead of re-querying GetAddonByName
            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * naviScale, naviMapSize * naviScale);

            mapCenterScreenPos = new Vector2(
                naviMapX + (mapSize.X / 2f),
                naviMapY + (mapSize.Y / 2f)
            ) + windowPos;

            mapCenterScreenPos.Y -= 5f * globalScale;

            // Pass pre-computed cos/sin to avoid recomputing per waymark
            foreach (var (worldPos, shape, color, name, iconId, visibilityRadius) in waymarksToRender)
            {
                var circlePos = CalculateCirclePosition(worldPos, cosRotation, sinRotation);
                var markerSize = configuration.WaymarkMarkerSize * globalScale;
                if (iconId != null)
                {
                    var iconSize = plugin.IconBrowserService.GetIconSize(iconId.Value);
                    var deSize = 6.0f / naviScale * globalScale;
                    if (iconSize.HasValue)
                    {
                        markerSize = iconSize.Value.X / deSize;
                    }
                    else
                    {
                        // Fallback to 64x64 (seems most icons are this size?)
                        markerSize = 64f / deSize;
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

                WaymarksToRender.Add((circlePos, shape, markerSize, fadedColor, name, iconId));
            }
        }

        #region Coordinate Transformation

        /// <summary>
        /// Calculates the screen position of a waymark relative to the minimap center.
        /// </summary>
        /// <param name="waymarkPosition">The world position of the waymark.</param>
        /// <param name="cosTheta">The cosine of the rotation angle.</param>
        /// <param name="sinTheta">The sine of the rotation angle.</param>
        /// <returns>The screen position of the waymark, or null if it is outside the minimap bounds.</returns>
        private Vector2 CalculateCirclePosition(Vector3 waymarkPosition, float cosTheta, float sinTheta)
        {
            var relativeOffset = new Vector2(
                playerWorldPos.X - waymarkPosition.X,
                playerWorldPos.Y - waymarkPosition.Z
            );

            relativeOffset *= zoneScale;
            relativeOffset *= naviScale;
            relativeOffset *= zoom;

            var waymarkScreenPos = mapCenterScreenPos - relativeOffset;

            if (!isLocked)
            {
                var dx = waymarkScreenPos.X - mapCenterScreenPos.X;
                var dy = waymarkScreenPos.Y - mapCenterScreenPos.Y;
                waymarkScreenPos = new Vector2(
                    (cosTheta * dx) - (sinTheta * dy) + mapCenterScreenPos.X,
                    (sinTheta * dx) + (cosTheta * dy) + mapCenterScreenPos.Y
                );
            }

            var distance = Vector2.Distance(mapCenterScreenPos, waymarkScreenPos);
            if (distance > minimapRadius)
            {
                var originToObject = waymarkScreenPos - mapCenterScreenPos;
                originToObject *= minimapRadius / distance;
                waymarkScreenPos = mapCenterScreenPos + originToObject;
            }

            return waymarkScreenPos;
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
            Plugin.Log.Information("WaymarkMinimapService disposed.");
            // Window removal happens from Plugin.Dispose
            disposed = true;
        }
    }
}
