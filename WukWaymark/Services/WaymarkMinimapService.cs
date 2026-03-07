using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
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

        private unsafe void OnFrameworkUpdate(IFramework framework)
        {
            _waymarksToRender.Clear();
            _window.IsEnabled = _configuration.WaymarksMinimapEnabled;

            if (!_configuration.WaymarksMinimapEnabled)
                return;

            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null)
                return;

            var naviMapAddonPtr = Plugin.GameGui.GetAddonByName("_NaviMap");
            if (naviMapAddonPtr.IsNull)
                return;

            var naviMapAddon = (AtkUnitBase*)naviMapAddonPtr.Address;
            if (naviMapAddon == null || !naviMapAddon->IsVisible || naviMapAddon->UldManager.LoadedState != AtkLoadState.Loaded)
                return;

            var isLocked = NaviMapStateReader.IsMinimapLocked(naviMapAddon);
            var rotation = NaviMapStateReader.GetMinimapRotation(naviMapAddon);
            var zoom = NaviMapStateReader.GetMinimapZoom(naviMapAddon);

            if (isLocked == null || rotation == null || zoom == null)
                return;

            _isLocked = isLocked.Value;
            _rotation = rotation.Value;
            _zoom = zoom.Value;
            _naviScale = naviMapAddon->Scale;
            _globalScale = Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;

            var agentMap = AgentMap.Instance();
            if (agentMap == null || agentMap->CurrentMapId == 0)
                return;

            _zoneScale = agentMap->CurrentMapSizeFactorFloat * 1.0f;
            _playerWorldPos = new Vector2(localPlayer.Position.X, localPlayer.Position.Z);

            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * _naviScale, naviMapSize * _naviScale);
            _minimapRadius = mapSize.X * 0.315f;

            foreach (var waymark in _configuration.Waymarks)
            {
                if (waymark.MapId != agentMap->SelectedMapId)
                    continue;

                _waymarksToRender.Add((waymark.Position, waymark.Shape, waymark.Color, waymark.Name));
            }
        }

        internal unsafe void PrepareRender(Vector2 windowPos)
        {
            WaymarksToRender.Clear();

            if (_waymarksToRender.Count == 0)
                return;

            var naviMapAddonPtr = Plugin.GameGui.GetAddonByName("_NaviMap");
            if (naviMapAddonPtr.IsNull)
                return;

            var naviMapAddon = (AtkUnitBase*)naviMapAddonPtr.Address;

            const int naviMapSize = 218;
            var mapSize = new Vector2(naviMapSize * _naviScale, naviMapSize * _naviScale);

            _mapCenterScreenPos = new Vector2(
                naviMapAddon->X + (mapSize.X / 2f),
                naviMapAddon->Y + (mapSize.Y / 2f)
            ) + windowPos;

            _mapCenterScreenPos.Y -= 5f * _globalScale;

            foreach (var (worldPos, shape, color, name) in _waymarksToRender)
            {
                var circlePos = CalculateCirclePosition(worldPos);
                if (!circlePos.HasValue)
                    continue;

                var markerSize = _configuration.WaymarkMarkerSize * _globalScale;
                WaymarksToRender.Add((circlePos.Value, shape, markerSize, color, name));
            }
        }

        private Vector2? CalculateCirclePosition(Vector3 waymarkPosition)
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
                waymarkScreenPos = RotatePoint(_mapCenterScreenPos, waymarkScreenPos, _rotation);
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

        private static Vector2 RotatePoint(Vector2 center, Vector2 pos, float angle)
        {
            var cosTheta = Math.Cos(angle);
            var sinTheta = Math.Sin(angle);

            return new Vector2
            {
                X = (float)((cosTheta * (pos.X - center.X)) - (sinTheta * (pos.Y - center.Y)) + center.X),
                Y = (float)((sinTheta * (pos.X - center.X)) + (cosTheta * (pos.Y - center.Y)) + center.Y)
            };
        }

        internal (Vector2 Position, Vector2 Size)? GetMinimapBounds()
        {
            unsafe
            {
                var naviMapAddonPtr = Plugin.GameGui.GetAddonByName("_NaviMap");
                if (naviMapAddonPtr.IsNull)
                    return null;

                var naviMap = (AtkUnitBase*)naviMapAddonPtr.Address;
                var rootNode = naviMap->RootNode;
                if (rootNode == null)
                    return null;

                var mapPos = new Vector2(naviMap->X, naviMap->Y);
                var mapSize = new Vector2(rootNode->Width * naviMap->Scale, rootNode->Height * naviMap->Scale);

                return (mapPos, mapSize);
            }
        }

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
