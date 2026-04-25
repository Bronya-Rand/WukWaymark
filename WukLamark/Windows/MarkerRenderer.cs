using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility;
using System;
using System.Numerics;
using WukLamark.Models;

namespace WukLamark.Windows
{
    public static class MarkerRenderer
    {
        /// <summary>
        /// Renders a single map marker marker using the ImGui draw list.
        /// 
        /// Handles all shape rendering logic based on the specified MarkerShape enum value.
        /// Each shape is rendered with a black outline for visibility against map backgrounds,
        /// with the interior filled using the provided color.
        /// </summary>
        /// <param name="drawList">The ImGui draw list to render to</param>
        /// <param name="position">Screen-space coordinates where the marker center should be drawn</param>
        /// <param name="shape">The shape to render (Circle, Square, Triangle, Diamond, or Star)</param>
        /// <param name="markerSize">The radius/size of the marker in pixels. Typical range: 4-20px</param>
        /// <param name="colorU32">The fill color in ImGui 32-bit RGBA format (generated via ImGui.ColorConvertFloat4ToU32)</param>
        public static void RenderMarkerShape(ImDrawListPtr drawList, Vector2 position, MarkerShape shape, float markerSize, uint colorU32)
        {
            // Extract alpha from the fill color so outline fades with the shape
            var alpha = (colorU32 >> 24) & 0xFF;
            var outlineColor = 0x00000000u | (alpha << 24);
            var outlineThickness = 1.5f;

            switch (shape)
            {
                case MarkerShape.Circle:
                    drawList.AddCircleFilled(position, markerSize + outlineThickness, outlineColor);
                    drawList.AddCircleFilled(position, markerSize, colorU32);
                    break;

                case MarkerShape.Square:
                    var squareHalf = markerSize * 0.866f; // Approximate circle inscribed square
                    drawList.AddRectFilled(position - new Vector2(squareHalf + outlineThickness, squareHalf + outlineThickness),
                                          position + new Vector2(squareHalf + outlineThickness, squareHalf + outlineThickness), outlineColor);
                    drawList.AddRectFilled(position - new Vector2(squareHalf, squareHalf),
                                          position + new Vector2(squareHalf, squareHalf), colorU32);
                    break;

                case MarkerShape.Diamond:
                    var diamondSize = markerSize * 1.15f;
                    var dOutSize = diamondSize + outlineThickness;

                    var do1 = position + new Vector2(0, -dOutSize);
                    var do2 = position + new Vector2(dOutSize, 0);
                    var do3 = position + new Vector2(0, dOutSize);
                    var do4 = position + new Vector2(-dOutSize, 0);

                    drawList.AddTriangleFilled(do1, do2, do3, outlineColor);
                    drawList.AddTriangleFilled(do1, do3, do4, outlineColor);

                    var d1 = position + new Vector2(0, -diamondSize);
                    var d2 = position + new Vector2(diamondSize, 0);
                    var d3 = position + new Vector2(0, diamondSize);
                    var d4 = position + new Vector2(-diamondSize, 0);

                    drawList.AddTriangleFilled(d1, d2, d3, colorU32);
                    drawList.AddTriangleFilled(d1, d3, d4, colorU32);
                    break;

                case MarkerShape.Triangle:
                    var sqrt3_2 = 0.866025f;
                    var outSize = markerSize + 1.0f + outlineThickness;

                    var ot1 = position + new Vector2(0, -outSize);
                    var ot2 = position + new Vector2(outSize * sqrt3_2, outSize * 0.5f);
                    var ot3 = position + new Vector2(-outSize * sqrt3_2, outSize * 0.5f);

                    drawList.AddTriangleFilled(ot1, ot2, ot3, outlineColor);

                    var inSize = markerSize + 1.0f;
                    var t1 = position + new Vector2(0, -inSize);
                    var t2 = position + new Vector2(inSize * sqrt3_2, inSize * 0.5f);
                    var t3 = position + new Vector2(-inSize * sqrt3_2, inSize * 0.5f);

                    drawList.AddTriangleFilled(t1, t2, t3, colorU32);
                    break;

                case MarkerShape.Star:
                    RenderStarShape(drawList, position, markerSize, colorU32, outlineColor, outlineThickness);
                    break;
            }
        }

        /// <summary>
        /// Helper to render a star-shaped map marker.
        /// </summary>
        public static void RenderStarShape(ImDrawListPtr drawList, Vector2 center, float radius, uint colorU32, uint outlineColor, float outlineThickness)
        {
            const int points = 5;

            // Draw outline (larger star)
            for (var i = 0; i < points * 2; i++)
            {
                var angle1 = ((float)i * MathF.PI / points) - (MathF.PI / 2);
                var angle2 = ((float)(i + 1) * MathF.PI / points) - (MathF.PI / 2);

                var r1 = (i % 2 == 0 ? radius : radius * 0.38f) + outlineThickness;
                var r2 = ((i + 1) % 2 == 0 ? radius : radius * 0.38f) + outlineThickness;

                var p1 = center + new Vector2(MathF.Cos(angle1) * r1, MathF.Sin(angle1) * r1);
                var p2 = center + new Vector2(MathF.Cos(angle2) * r2, MathF.Sin(angle2) * r2);

                drawList.AddTriangleFilled(center, p1, p2, outlineColor);
            }

            // Draw inner star
            for (var i = 0; i < points * 2; i++)
            {
                var angle1 = ((float)i * MathF.PI / points) - (MathF.PI / 2);
                var angle2 = ((float)(i + 1) * MathF.PI / points) - (MathF.PI / 2);

                var r1 = i % 2 == 0 ? radius : radius * 0.38f;
                var r2 = (i + 1) % 2 == 0 ? radius : radius * 0.38f;

                var p1 = center + new Vector2(MathF.Cos(angle1) * r1, MathF.Sin(angle1) * r1);
                var p2 = center + new Vector2(MathF.Cos(angle2) * r2, MathF.Sin(angle2) * r2);

                drawList.AddTriangleFilled(center, p1, p2, colorU32);
            }
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
            catch (IconNotFoundException)
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
        /// Renders a marker using a custom icon loaded from the plugin's CustomIcons folder.
        /// </summary>
        /// <param name="drawList">The ImGui draw list to render to</param>
        /// <param name="position">Screen-space center position for the icon</param>
        /// <param name="customIconName">The name of the custom icon file</param>
        /// <param name="markerSize">Half-size of the icon in pixels (icon will be 1.5x this value)</param>
        /// <param name="tintColor">Tint color to apply to the icon</param>
        public static void RenderMarkerCustomIcon(ImDrawListPtr drawList, Vector2 position, string customIconName, float markerSize, uint tintColor = uint.MaxValue)
        {
            if (!Plugin.CustomIconService.TryGetCustomIcon(customIconName, out var iconTex) || iconTex == null || iconTex.Handle == nint.Zero)
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
        public static void RenderMarker(ImDrawListPtr drawList, Vector2 position, MarkerShape shape, float markerSize, uint colorU32, uint? iconId, string? customIconName, bool useShapeColorOnIcon)
        {
            // Pass the fill color as tint so icon respects alpha fade
            var tint = 0x00FFFFFFu | (colorU32 & 0xFF000000u); // white RGB + alpha from colorU32

            // Apply shape color as tint if 'useShapeColorOnIcon' is true.
            if (useShapeColorOnIcon)
                tint = colorU32;

            if (!customIconName.IsNullOrEmpty())
                RenderMarkerCustomIcon(drawList, position, customIconName, markerSize, tint);
            else if (iconId.HasValue && iconId.Value != 0)
                RenderMarkerIcon(drawList, position, iconId.Value, markerSize, tint);
            else
                RenderMarkerShape(drawList, position, shape, markerSize, colorU32);
        }
    }
}
