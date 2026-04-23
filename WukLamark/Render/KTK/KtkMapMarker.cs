using KamiToolKit.Overlay.MapOverlay;
using System.Numerics;

namespace WukLamark.Render.KTK
{
    public sealed class KtkMapMarker : MapMarkerNode
    {
        public required string MarkerKey { get; init; }

        private uint mapId;
        private Vector2 worldPos;
        private string tooltip = string.Empty;
        private uint iconId = 60575;
        private Vector2 iconSize = new(12.0f, 12.0f);
        private bool useTint;
        private Vector3 tintColor = Vector3.One;
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
            IsVisible = false;
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
