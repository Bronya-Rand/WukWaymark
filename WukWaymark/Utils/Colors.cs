using System;
using System.Numerics;

namespace WukWaymark.Utils
{
    public static class Colors
    {
        /// <summary>
        /// Returns a random color as a Vector4 with RGBA components
        /// </summary>
        /// <returns>A Vector4 representing a random color (R, G, B, A)</returns>
        public static Vector4 RandomizeColor()
        {
            var random = new Random();

            var r = (float)random.NextDouble();
            var g = (float)random.NextDouble();
            var b = (float)random.NextDouble();
            var a = 1.0f; // Fully opaque

            return new Vector4(r, g, b, a);
        }
    }
}
