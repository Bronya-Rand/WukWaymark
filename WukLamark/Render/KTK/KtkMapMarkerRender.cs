using Dalamud.Interface.Utility;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using WukLamark.Helpers;
using WukLamark.Models;
using WukLamark.Services;

namespace WukLamark.Render.KTK
{
    /// <summary>
    /// Renders map markers using KTK (KamiToolKit)'s native <see cref="KamiToolKit.Overlay.MapOverlay.MapOverlayController"/> system.
    /// </summary>
    /// <param name="plugin">WukLamark's main plugin instance.</param>
    /// <remarks>
    /// Unlike ImGui (rebuilds draw list every frame), XIV native markers are
    /// persistent. For similar ImGui logic, we track which markers were seen during
    /// the current render pass.
    /// </remarks>
    internal sealed class KtkMapMarkerRender(Plugin plugin) : IMapMarkerRender
    {
        /// <summary>
        /// Represents the default icon to use for non-icon markers in KTK.
        /// </summary>
        private const uint DefaultIconId = 60575;

        /// <summary>
        /// Stores the currently active markers that have been added to the KTK map overlay.
        /// </summary>
        private readonly Dictionary<Guid, KtkMapMarker> activeMarkers = [];
        /// <summary>
        /// Stores the IDs of markers that have been seen during the current render pass.
        /// </summary>
        private readonly HashSet<Guid> markersSeen = [];

        public bool IsEnabled => plugin.Configuration.WaymarksMapEnabled
            && plugin.MapOverlayController != null
            && plugin.Configuration.UseKTK;

        /// <summary>
        /// Clears all cached map markers and resets the internal marker tracking state.
        /// </summary>
        internal void InvalidateCache()
        {
            foreach (var marker in activeMarkers.Values)
                plugin.MapOverlayController?.RemoveMarker(marker);

            activeMarkers.Clear();
            markersSeen.Clear();
        }
        public void BeginRender()
        {
            markersSeen.Clear();
        }

        public void RenderMarker(uint selectedMapId, float uiScale, MapMarkerData markerInfo)
        {
            var safeName = markerInfo.Name.IsNullOrEmpty() ? "Unnamed Marker" : markerInfo.Name;
            var formattedNotes = MapHelper.FormatMapTooltipNotes(markerInfo.Notes);
            var tooltipText = formattedNotes.Length > 0 ? $"{safeName}\n{formattedNotes}" : safeName;

            var markerId = markerInfo.Id;
            var markerSize = markerInfo.MarkerSize * 1.5f * uiScale * ImGuiHelpers.GlobalScale;

            var iconId = markerInfo.Icon.GameIconId;
            var useShapeColor = markerInfo.Icon.UseShapeColor;
            if (iconId == null && markerInfo.Icon.SourceType == MarkerIconType.Shape)
            {
                iconId = DefaultIconId;
                useShapeColor = true;
            }

            Plugin.CustomIconService.TryGetCustomIcon(markerInfo.Icon.CustomIconName, out var customIconTexture);
            var texturePath = !markerInfo.Icon.CustomIconName.IsNullOrEmpty() ? Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "CustomIcons", markerInfo.Icon.CustomIconName) : null;

            // Create a new marker if it doesn't exist, or retrieve the existing one.
            if (!activeMarkers.TryGetValue(markerId, out var mapMarker))
            {
                mapMarker = new KtkMapMarker { MarkerId = markerId };
                activeMarkers[markerId] = mapMarker;
                plugin.MapOverlayController!.AddMarker(mapMarker);
            }

            var vector3Color = new Vector3(markerInfo.Icon.Color.X, markerInfo.Icon.Color.Y, markerInfo.Icon.Color.Z);

            mapMarker.Apply(
                selectedMapId,
                markerInfo.WorldPosition,
                tooltipText,
                iconId,
                customIconTexture,
                texturePath,
                new Vector2(markerSize, markerSize),
                useShapeColor,
                vector3Color
                );

            markersSeen.Add(markerId);
        }

        public void EndRender()
        {
            List<Guid>? markersToRemove = null;

            // Identify markers that were not seen during this render pass
            foreach (var kv in activeMarkers)
            {
                if (markersSeen.Contains(kv.Key)) continue;
                markersToRemove ??= [];
                markersToRemove.Add(kv.Key);
            }

            // Remove markers that were not seen during this render pass
            // If no markers are to be removed, exit early.
            if (markersToRemove == null) return;

            foreach (var markerId in markersToRemove)
            {
                var marker = activeMarkers[markerId];
                plugin.MapOverlayController?.RemoveMarker(marker);
                activeMarkers.Remove(markerId);
            }
        }
        public void Dispose()
        {
            InvalidateCache();
            GC.SuppressFinalize(this);
        }
    }
}
