using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using WukWaymark.Models;

namespace WukWaymark.Windows
{
    /// <summary>
    /// Static utility class responsible for rendering waymark shapes on ImGui draw lists.
    /// </summary>
    public static class WaymarkRenderer
    {
        /// <summary>
        /// Renders a single waymark marker using the ImGui draw list.
        /// 
        /// Handles all shape rendering logic based on the specified WaymarkShape enum value.
        /// Each shape is rendered with a black outline for visibility against map backgrounds,
        /// with the interior filled using the provided color.
        /// </summary>
        /// <param name="drawList">The ImGui draw list to render to</param>
        /// <param name="position">Screen-space coordinates where the marker center should be drawn</param>
        /// <param name="shape">The shape to render (Circle, Square, Triangle, Diamond, or Star)</param>
        /// <param name="markerSize">The radius/size of the marker in pixels. Typical range: 4-20px</param>
        /// <param name="colorU32">The fill color in ImGui 32-bit RGBA format (generated via ImGui.ColorConvertFloat4ToU32)</param>
        public static void RenderWaymarkShape(ImDrawListPtr drawList, Vector2 position, WaymarkShape shape, float markerSize, uint colorU32)
        {
            // Draw black outline for visibility
            var outlineColor = 0xFF000000u;
            var outlineThickness = 1.5f;

            switch (shape)
            {
                case WaymarkShape.Circle:
                    drawList.AddCircleFilled(position, markerSize + outlineThickness, outlineColor);
                    drawList.AddCircleFilled(position, markerSize, colorU32);
                    break;

                case WaymarkShape.Square:
                    var squareHalf = markerSize * 0.866f; // Approximate circle inscribed square
                    drawList.AddRectFilled(position - new Vector2(squareHalf + outlineThickness, squareHalf + outlineThickness),
                                          position + new Vector2(squareHalf + outlineThickness, squareHalf + outlineThickness), outlineColor);
                    drawList.AddRectFilled(position - new Vector2(squareHalf, squareHalf),
                                          position + new Vector2(squareHalf, squareHalf), colorU32);
                    break;

                case WaymarkShape.Diamond:
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

                case WaymarkShape.Triangle:
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

                case WaymarkShape.Star:
                    RenderStarShape(drawList, position, markerSize, colorU32, outlineColor, outlineThickness);
                    break;
            }
        }

        /// <summary>
        /// Helper to render a star-shaped waymark.
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
    }
}
