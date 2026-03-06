using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace WukWaymark.Utils
{
    public static class Colors
    {
        /// <summary>
        /// Returns a sequential color using HSV based on the current number of entries.
        /// This provides a nicer default color progression compared to fully random colors.
        /// </summary>
        /// <param name="currentIndex">The current number of entries to use as the base value</param>
        /// <returns>A Vector4 representing a color (R, G, B, A)</returns>
        public static Vector4 GetNextColor(int currentIndex)
        {
            // Increment hue per entry
            var angle = ((currentIndex * 60f) + 60f) % 360f;

            var h = angle / 360f;
            var s = 1.0f;
            var v = 1.0f;

            float r = 0f, g = 0f, b = 0f;
            ImGui.ColorConvertHSVtoRGB(h, s, v, ref r, ref g, ref b);

            return new Vector4(r, g, b, 1.0f);
        }
    }
}
