using Dalamud.Utility;
using System.Numerics;
using WukLamark.Models;

namespace WukLamark.Migration.PMarkerData
{
    internal sealed class MigratePlayerMarkerDataV1 : IMigratePlayerMarkerData
    {
        private static readonly Vector4 LegacyDefaultColor = new(1.0f, 0.8f, 0.0f, 1.0f);

        public int FromVersion => 0;
        public int ToVersion => 1;

        public bool ApplyMigration(PlayerMarkerData data)
        {
            var changed = false;

            // Since we made 'Waymarks' legacy, all old map marker data is now
            // redirected to 'Markers'
            foreach (var marker in data.Markers)
            {
                var iconLooksDefault =
                    marker.Icon.SourceType == MarkerIconType.Shape &&
                    marker.Icon.GameIconId == null &&
                    marker.Icon.CustomIconName.IsNullOrWhitespace() &&
                    marker.Icon.Shape == MarkerShape.Circle &&
                    marker.Icon.Size == 0.0f &&
                    marker.Icon.VisibilityRadius == 0.0f &&
                    !marker.Icon.UseShapeColor &&
                    marker.Icon.Color == LegacyDefaultColor;

#pragma warning disable CS0618 // Type or member is obsolete
                var legacyHasData =
                    marker.IconId.HasValue ||
                    marker.IconSize.GetValueOrDefault(0.0f) != 0.0f ||
                    marker.VisibilityRadius != 0.0f ||
                    marker.Shape != MarkerShape.Circle ||
                    marker.UseShapeColorOnIcon ||
                    marker.Color != LegacyDefaultColor;

                if (!iconLooksDefault || !legacyHasData) continue;

                var hasGameIcon = marker.IconId is > 0;

                marker.Icon = new MarkerIcon
                {
                    SourceType = hasGameIcon ? MarkerIconType.Game : MarkerIconType.Shape,
                    GameIconId = hasGameIcon ? marker.IconId : null,
                    CustomIconName = null,
                    Shape = marker.Shape,
                    Size = marker.IconSize.GetValueOrDefault(0.0f),
                    UseShapeColor = marker.UseShapeColorOnIcon,
                    VisibilityRadius = marker.VisibilityRadius,
                    Color = marker.Color
                };

                marker.IconId = null;
                marker.IconSize = 0.0f;
                marker.VisibilityRadius = 0.0f;
                marker.Shape = MarkerShape.Circle;
                marker.Color = LegacyDefaultColor;
                marker.UseShapeColorOnIcon = false;
#pragma warning restore CS0618 // Type or member is obsolete

                changed = true;
            }
            return changed;
        }
    }
}
