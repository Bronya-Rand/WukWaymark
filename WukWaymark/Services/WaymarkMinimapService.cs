using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
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
        private readonly Configuration _configuration;
        private readonly WaymarkMinimapWindow _window;
        private readonly WindowSystem _windowSystem;
        private bool _disposed;

        // Publicly accessible list mapping Waymarks to their PreDraw evaluated locations
        public List<(Vector2 Position, WaymarkShape Shape, float Size, Vector4 Color, string? Name)> WaymarksToRender { get; } = [];

        // Player data gathered during Framework.Update
        private readonly List<(Vector3 WorldPos, WaymarkShape Shape, Vector4 Color, string? Name)> _waymarksToRender = [];

        // Minimap state cached per frame
        private float _minimapRadius;
        private Vector2 _mapCenterScreenPos;
        private Vector2 _playerWorldPos;
        private float _zoneScale;
        private float _naviScale;
        private float _zoom;
        private float _rotation;
        private bool _isLocked;
        private float _globalScale;

        // naviMap screen position cache — avoids GetAddonByName in PrepareRender/GetMinimapBounds
        private float _naviMapX;
        private float _naviMapY;

        // cos/sin computed from _rotation cache — computed once per frame
        private float _sinRotation;
        private float _cosRotation;

        public WaymarkMinimapService(Plugin plugin)
        {
            _configuration = plugin.Configuration;
            _windowSystem = plugin.WindowSystem;
            _window = new WaymarkMinimapWindow(this, plugin);

            // Register the window directly with the passed in system to centralize tracking
            _windowSystem.AddWindow(_window);

            // Hook into framework update
            Plugin.Framework.Update += OnFrameworkUpdate;
        }

        /// <summary>
        /// Updates the minimap state and caches relevant data for rendering.
        /// </summary>
        /// <param name="framework">The framework instance.</param>
        private unsafe void OnFrameworkUpdate(IFramework framework)
        {
            _waymarksToRender.Clear();
            _window.IsEnabled = _configuration.WaymarksMinimapEnabled;

            if (!_configuration.WaymarksMinimapEnabled)
                return;

            // Get local player
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return;

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
            if (!NaviMapStateReader.TryReadMinimapState(naviMapAddon, out var isLocked, out var rotation, out var zoom))
                return;

            _isLocked = isLocked;
            _rotation = rotation;
            _zoom = zoom;
            _naviScale = naviMapAddon->Scale;
            _globalScale = ImGuiHelpers.GlobalScale;

            // Cache naviMap screen position so PrepareRender/GetMinimapBounds don't need GetAddonByName
            _naviMapX = naviMapAddon->X;
            _naviMapY = naviMapAddon->Y;

            // Pre-compute cos/sin once per frame
            _cosRotation = MathF.Cos(_rotation);
            _sinRotation = MathF.Sin(_rotation);

            // Get AgentMap for zone scale
            var agentMap = AgentMap.Instance();
            if (agentMap == null || agentMap->CurrentMapId == 0)
                return;

            // CurrentMapSizeFactorFloat returns values like 0.02 for a zone scale of 2
            // Multiply by 1.0f to convert it to a proper scale factor (e.g. 2.0 for a zone scale of 2)
            _zoneScale = agentMap->CurrentMapSizeFactorFloat * 1.0f;

            // Cache player world position
            _playerWorldPos = new Vector2(localPlayer.Position.X, localPlayer.Position.Z);

            // Calculate minimap radius
            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * _naviScale, naviMapSize * _naviScale);
            _minimapRadius = mapSize.X * 0.315f;

            // Cache waymarks to render
            foreach (var waymark in _configuration.Waymarks)
            {
                if (waymark.MapId != agentMap->SelectedMapId)
                    continue;

                // Store world position - will convert to screen coords in PreDraw
                _waymarksToRender.Add((waymark.Position, waymark.Shape, waymark.Color, waymark.Name));
            }
        }

        /// <summary>
        /// Prepares the waymarks for rendering by converting their world positions to screen coordinates.
        /// </summary>
        /// <param name="windowPos">The position of the window.</param>
        internal unsafe void PrepareRender(Vector2 windowPos)
        {
            WaymarksToRender.Clear();

            if (_waymarksToRender.Count == 0)
                return;

            // Use cached naviMap position instead of re-querying GetAddonByName
            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * _naviScale, naviMapSize * _naviScale);

            _mapCenterScreenPos = new Vector2(
                _naviMapX + (mapSize.X / 2f),
                _naviMapY + (mapSize.Y / 2f)
            ) + windowPos;

            _mapCenterScreenPos.Y -= 5f * _globalScale;

            // Pass pre-computed cos/sin to avoid recomputing per waymark
            foreach (var (worldPos, shape, color, name) in _waymarksToRender)
            {
                var circlePos = CalculateCirclePosition(worldPos, _cosRotation, _sinRotation);
                var markerSize = _configuration.WaymarkMarkerSize * _globalScale;
                WaymarksToRender.Add((circlePos, shape, markerSize, color, name));
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
                _playerWorldPos.X - waymarkPosition.X,
                _playerWorldPos.Y - waymarkPosition.Z
            );

            relativeOffset *= _zoneScale;
            relativeOffset *= _naviScale;
            relativeOffset *= _zoom;

            var waymarkScreenPos = _mapCenterScreenPos - relativeOffset;

            if (!_isLocked)
            {
                var dx = waymarkScreenPos.X - _mapCenterScreenPos.X;
                var dy = waymarkScreenPos.Y - _mapCenterScreenPos.Y;
                waymarkScreenPos = new Vector2(
                    (cosTheta * dx) - (sinTheta * dy) + _mapCenterScreenPos.X,
                    (sinTheta * dx) + (cosTheta * dy) + _mapCenterScreenPos.Y
                );
            }

            var distance = Vector2.Distance(_mapCenterScreenPos, waymarkScreenPos);
            if (distance > _minimapRadius)
            {
                var originToObject = waymarkScreenPos - _mapCenterScreenPos;
                originToObject *= _minimapRadius / distance;
                waymarkScreenPos = _mapCenterScreenPos + originToObject;
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
            var mapSize = new Vector2(naviMapSize * _naviScale, naviMapSize * _naviScale);
            // Use cached _naviMapX/_naviMapY
            var mapPos = new Vector2(_naviMapX, _naviMapY);
            return (mapPos, mapSize);
        }

        #endregion
        public void Dispose()
        {
            if (_disposed)
                return;

            Plugin.Framework.Update -= OnFrameworkUpdate;
            Plugin.Log.Information("WaymarkMinimapService disposed.");
            // Window removal happens from Plugin.Dispose
            _disposed = true;
        }
    }
}
