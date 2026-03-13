using Dalamud.Bindings.ImGui;

namespace WukWaymark.Utils
{
    public static class ImWuk
    {
        public static bool IsItemHoveredWhenDisabled()
        {
            return ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        }
    }
}
