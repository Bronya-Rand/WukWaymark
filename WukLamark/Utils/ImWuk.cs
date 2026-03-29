using Dalamud.Bindings.ImGui;

namespace WukLamark.Utils
{
    public static class ImWuk
    {
        public static bool IsItemHoveredWhenDisabled()
        {
            return ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        }
    }
}
