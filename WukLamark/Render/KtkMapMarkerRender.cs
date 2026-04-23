using Dalamud.Interface.Utility;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Helpers;
using WukLamark.Services;
using MapMarkerInfo = KamiToolKit.Classes.MapMarkerInfo;

namespace WukLamark.Render
{
    /// <summary>
    /// Renders map markers using KTK (KamiToolKit)'s native <see cref="KamiToolKit.Overlay.MapOverlay.MapOverlayController"/> system.
    /// </summary>
    /// <param name="plugin">WukLamark's main plugin instance.</param>
    /// <remarks>
    /// Unlike ImGui (rebuilds draw list every frame), XIV native markers are
    /// persistent. For similar ImGui logic, we'll buffer each frame's desired
    /// markers, compute a content hash, and only commit (remove + re-add) when the
    /// data changes.
    /// </remarks>
    internal sealed class KtkMapMarkerRender(Plugin plugin) : IMapMarkerRender
    {
        /// <summary>
        /// Represents the default icon to use for non-icon markers in KTK.
        /// </summary>
        private const uint DefaultIconId = 60575;

        /// <summary>
        /// Hash of the last committed marker state, used to detect changes.
        /// </summary>
        private int lastCommittedHash = 0;

        /// <summary>
        /// Buffer of markers built during the current frame's render pass.
        /// </summary>
        private readonly List<MapMarkerInfo> pendingMarkers = [];

        public bool IsEnabled => plugin.Configuration.WaymarksMapEnabled && plugin.MapOverlayController != null && plugin.Configuration.UseKTK;

        public void BeginRender()
        {
            pendingMarkers.Clear();
        }

        public void RenderMarker(uint selectedMapId, float uiScale, MapMarkerData markerInfo)
        {
            var safeName = markerInfo.Name.IsNullOrEmpty() ? "Unnamed Marker" : markerInfo.Name;
            var formattedNotes = MapHelper.FormatMapTooltipNotes(markerInfo.Notes);
            var tooltipText = formattedNotes.Length > 0 ? $"{safeName}\n{formattedNotes}" : safeName;

            var markerData = new MapMarkerInfo
            {
                AllowAnyMap = false,
                MapId = selectedMapId,
                Position = markerInfo.WorldPos,
                Tooltip = tooltipText
            };

            var iconId = markerInfo.IconId ?? DefaultIconId;
            var iconSize = IconHelper.GetIconSize(iconId);
            if (iconSize == null)
            {
                Plugin.Log.Warning($"Icon size not found for icon ID: {iconId}. Using default size.");
                iconSize = new Vector2(32, 32); // Default size if icon size is not found
            }

            markerData.IconId = iconId;

            var markerSize = 12 * uiScale * ImGuiHelpers.GlobalScale;
            markerData.Size = new Vector2(markerSize, markerSize);

            // Buffer the marker — don't commit to KTK yet.
            pendingMarkers.Add(markerData);
        }

        public void EndRender()
        {
            var currentHash = ComputePendingHash();

            // Only rebuild native markers when something actually changed.
            if (currentHash != lastCommittedHash)
            {
                plugin.MapOverlayController?.RemoveAllMarkers();
                foreach (var marker in pendingMarkers)
                    plugin.MapOverlayController!.AddMarker(marker);

                lastCommittedHash = currentHash;
            }
        }

        /// <summary>
        /// Computes a combined hash of all buffered markers to detect
        /// whether a rebuild is needed without comparing element-by-element.
        /// </summary>
        private int ComputePendingHash()
        {
            var hash = new HashCode();
            hash.Add(pendingMarkers.Count);
            foreach (var m in pendingMarkers)
            {
                hash.Add(m.MapId);
                hash.Add(m.Position);
                hash.Add(m.IconId);
                hash.Add(m.Tooltip);
                hash.Add(m.Size);
            }
            return hash.ToHashCode();
        }
    }
}
