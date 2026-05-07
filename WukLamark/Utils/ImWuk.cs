using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace WukLamark.Utils
{
    public static class ImWuk
    {
        /// <summary>
        /// Returns a <see cref="ImGui.IsItemHovered"/> with the <see cref="ImGuiHoveredFlags.AllowWhenDisabled"/> flag set.
        /// </summary>
        /// <returns></returns>
        public static bool IsItemHoveredWhenDisabled()
        {
            return ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        }

        /// <summary>
        /// Renders text centered within the available content region.
        /// </summary>
        /// <param name="text">The text to be rendered.</param>
        public static void CenteredText(string text)
        {
            var textWidth = ImGui.CalcTextSize(text).X;
            var avail = ImGui.GetContentRegionAvail().X;
            var x = (avail - textWidth) / 2;
            if (x > 0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + x);

            ImGui.Text(text);
        }
        public static void CenteredTextColored(Vector4 color, string text)
        {
            var textWidth = ImGui.CalcTextSize(text).X;
            var avail = ImGui.GetContentRegionAvail().X;
            var x = (avail - textWidth) / 2;
            if (x > 0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + x);
            ImGui.TextColored(color, text);
        }
    }
}
