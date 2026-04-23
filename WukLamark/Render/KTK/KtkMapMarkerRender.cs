using Dalamud.Interface.Utility;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Helpers;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Render.KTK
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
        private bool hasCommitted = false;

        /// <summary>
        /// Buffer of markers built during the current frame's render pass.
        /// </summary>
        private readonly List<KtkMapMarker> pendingMarkers = [];

        public bool IsEnabled => plugin.Configuration.WaymarksMapEnabled && plugin.MapOverlayController != null && plugin.Configuration.UseKTK;

        internal void InvalidateCache()
        {
            hasCommitted = false;
            lastCommittedHash = 0;
        }
        public void BeginRender()
        {
            pendingMarkers.Clear();
        }

        public void RenderMarker(uint selectedMapId, float uiScale, MapMarkerData markerInfo)
        {
            var safeName = markerInfo.Name.IsNullOrEmpty() ? "Unnamed Marker" : markerInfo.Name;
            var formattedNotes = MapHelper.FormatMapTooltipNotes(markerInfo.Notes);
            var tooltipText = formattedNotes.Length > 0 ? $"{safeName}\n{formattedNotes}" : safeName;

            var markerData = new KtkMapMarker { MarkerKey = markerInfo.Name };
            var iconId = markerInfo.IconId ?? DefaultIconId;
            var markerSize = (markerInfo.MarkerSize * 1.5f) * uiScale * ImGuiHelpers.GlobalScale;

            markerData.Apply(
                selectedMapId,
                markerInfo.WorldPos,
                tooltipText,
                iconId,
                new Vector2(markerSize, markerSize),
                markerInfo.UseShapeColorOnIcon,
                Colors.ConvertU32ToVector3(markerInfo.Color)
                );

            // Buffer the marker — don't commit to KTK yet.
            pendingMarkers.Add(markerData);
        }

        public void EndRender()
        {
            var currentHash = ComputePendingHash();

            // Only rebuild native markers when something actually changed.
            if (!hasCommitted || currentHash != lastCommittedHash)
            {
                plugin.MapOverlayController?.RemoveAllMarkers();
                foreach (var marker in pendingMarkers)
                    plugin.MapOverlayController!.AddMarker(marker);

                lastCommittedHash = currentHash;
                hasCommitted = true;
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
                hash.Add(m.mapId);
                hash.Add(m.worldPos);
                hash.Add(m.iconId);
                hash.Add(m.tooltip);
                hash.Add(m.iconSize);
                hash.Add(m.useTint);
                hash.Add(m.tintColor);
            }
            return hash.ToHashCode();
        }
        public void Dispose()
        {
            // Clean up any KTK markers when this renderer is disposed.
            plugin.MapOverlayController?.RemoveAllMarkers();
            GC.SuppressFinalize(this);
        }
    }
}
