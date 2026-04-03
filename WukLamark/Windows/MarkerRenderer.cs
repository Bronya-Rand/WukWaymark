using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using System;
using System.Numerics;
using WukLamark.Models;

namespace WukLamark.Windows
{
    public static class MarkerRenderer
    {
        private static void RenderMarkerShape(ImDrawListPtr drawList, Vector2 center, MarkerShape shape, float radius, uint colorU32)
        {
            // Simplified example renderers for available shapes
            // Circle
            if (shape == MarkerShape.Circle)
            {
                drawList.AddCircleFilled(center, radius, colorU32);
                return;
            }
            // Square
            if (shape == MarkerShape.Square)
            {
                var half = radius;
                drawList.AddRectFilled(center - new Vector2(half, half), center + new Vector2(half, half), colorU32);
                return;
            }
            // Fallback: draw a circle
            drawList.AddCircleFilled(center, radius, colorU32);
        }

        /// <summary>
        /// Renders a marker using a game texture icon via ITextureProvider.
        /// The icon is drawn centered at the specified position.
        /// </summary>
        /// <param name="drawList">The ImGui draw list to render to</param>
        /// <param name="position">Screen-space center position for the icon</param>
        /// <param name="iconId">The game icon ID to load via ITextureProvider</param>
        /// <param name="markerSize">Half-size of the icon in pixels (icon will be 1.5x this value)</param>
        public static void RenderMarkerIcon(ImDrawListPtr drawList, Vector2 position, uint iconId, float markerSize, uint tintColor = uint.MaxValue)
        {
            IDalamudTextureWrap? iconTex;
            try
            {
                iconTex = Plugin.TextureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
            }
            catch (Exception)
            {
                iconTex = null;
            }
            if (iconTex == null || iconTex.Handle == nint.Zero)
                return;

            var halfSize = markerSize * 1.5f; // Slightly larger than shape markers for clarity
            var topLeft = position - new Vector2(halfSize, halfSize);
            var bottomRight = position + new Vector2(halfSize, halfSize);

            drawList.AddImage(iconTex.Handle, topLeft, bottomRight, Vector2.Zero, Vector2.One, tintColor);
        }

        /// <summary>
        /// Renders a marker using either an icon (if IconId is set) or a shape fallback.
        /// This is the primary entry point for rendering markers.
        /// </summary>
        /// <param name="drawList">The ImGui draw list to render to</param>
        /// <param name="position">Screen-space center position</param>
        /// <param name="shape">Fallback shape if no icon</param>
        /// <param name="markerSize">Marker size in pixels</param>
        /// <param name="colorU32">Fill color for shape rendering</param>
        /// <param name="iconId">Optional game icon ID; if set, renders icon instead of shape</param>
        public static void RenderMarker(ImDrawListPtr drawList, Vector2 position, MarkerShape shape, float markerSize, uint colorU32, uint? iconId)
        {
            if (iconId.HasValue && iconId.Value != 0)
            {
                // Pass the fill color as tint so icon respects alpha fade
                var tint = 0x00FFFFFFu | (colorU32 & 0xFF000000u); // white RGB + alpha from colorU32
                RenderMarkerIcon(drawList, position, iconId.Value, markerSize, tint);
            }
            else
            {
                RenderMarkerShape(drawList, position, shape, markerSize, colorU32);
            }
        }
    }
}
