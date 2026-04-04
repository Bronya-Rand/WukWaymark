using System;

namespace WukLamark.Utils
{
    public class Calculate
    {
        /// <summary>
        /// Calculates the scaling multiplier for converting world coordinates to screen pixels
        /// based on the AreaMap zoom slider position and UI scale.
        /// </summary>
        public static float GetMultiplier(float zoomIndex, float uiScale)
        {
            var x = Math.Clamp(zoomIndex, 0, 7);
            var result = ((107f * x * x) + x + 750f) / 3000f;
            result *= uiScale;
            return result;
        }
    }
}
