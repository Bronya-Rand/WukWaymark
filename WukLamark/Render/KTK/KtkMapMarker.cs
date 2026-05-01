using KamiToolKit.Overlay.MapOverlay;
using System;
using System.Numerics;

namespace WukLamark.Render.KTK
{
    /// <summary>
    /// The map marker node used by KTK (KamiToolKit) to render custom map markers on the in-game map.
    /// </summary>
    public sealed class KtkMapMarker : MapMarkerNode
    {
        public required Guid MarkerId { get; init; }

        internal uint mapId;
        internal Vector2 worldPosition;
        internal string tooltip = string.Empty;
        internal uint? gameIconId = null;
        internal Vector2 iconSize = new(12.0f, 12.0f);
        internal bool useTint;
        internal Vector3 tintColor = Vector3.One;

        public void Apply(uint mapId, Vector2 worldPosition, string tooltip, uint? gameIconId, Vector2 iconSize, bool useTint, Vector3 tintColor)
        {
            this.mapId = mapId;
            this.worldPosition = worldPosition;
            this.tooltip = tooltip;
            this.gameIconId = gameIconId;
            this.iconSize = iconSize;
            this.useTint = useTint;
            this.tintColor = tintColor;
        }
        protected override void OnUpdate()
        {
            MapId = mapId;
            Position = worldPosition;
            TextTooltip = tooltip;
            IconId = gameIconId;
            Size = iconSize;
            MultiplyColor = useTint ? tintColor : Vector3.One;
            IsVisible = true;
        }
    }
}
