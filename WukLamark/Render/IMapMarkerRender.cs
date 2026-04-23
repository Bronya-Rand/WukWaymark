using WukLamark.Services;

namespace WukLamark.Render
{
    internal interface IMapMarkerRender
    {
        bool IsEnabled { get; }
        void BeginRender();
        void RenderMarker(uint selectedMapId, float uiScale, MapMarkerData markerInfo);
        void EndRender();
    }
}
