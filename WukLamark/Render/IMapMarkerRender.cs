using WukLamark.Services;

namespace WukLamark.Render
{
    internal interface IMapMarkerRender
    {
        bool IsEnabled { get; }
        void BeginRender();
        void RenderMarker(uint selectedMapId, MapMarkerData markerInfo);
        void EndRender();
    }
}
