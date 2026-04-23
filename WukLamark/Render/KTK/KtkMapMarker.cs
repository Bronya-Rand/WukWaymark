using KamiToolKit.Overlay.MapOverlay;
using System.Numerics;

namespace WukLamark.Render.KTK
{
    public sealed class KtkMapMarker : MapMarkerNode
    {
        public required string MarkerKey { get; init; }

        internal uint mapId;
        internal Vector2 worldPos;
        internal string tooltip = string.Empty;
        internal uint iconId = 60575;
        internal Vector2 iconSize = new(12.0f, 12.0f);
        internal bool useTint;
        internal Vector3 tintColor = Vector3.One;
        public void Apply(uint mapId, Vector2 worldPos, string tooltip, uint iconId, Vector2 iconSize, bool useTint, Vector3 tintColor)
        {
            this.mapId = mapId;
            this.worldPos = worldPos;
            this.tooltip = tooltip;
            this.iconId = iconId;
            this.iconSize = iconSize;
            this.useTint = useTint;
            this.tintColor = tintColor;
        }
        protected override void OnUpdate()
        {
            MapId = mapId;
            Position = worldPos;
            TextTooltip = tooltip;
            IconId = iconId;
            Size = iconSize;
            MultiplyColor = useTint ? tintColor : Vector3.One;
            IsVisible = true;
        }
    }
}
