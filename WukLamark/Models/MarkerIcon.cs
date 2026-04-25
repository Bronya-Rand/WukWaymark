using System.Numerics;

namespace WukLamark.Models
{
    public enum MarkerIconType
    {
        Shape = 0,
        Game = 1,
        Custom = 2,
    }
    public sealed class MarkerIcon
    {
        /// <summary>
        /// The source type of the icon.
        /// </summary>
        public MarkerIconType SourceType { get; init; }

        /// <summary>
        /// The ID of a game icon associated with this instance, if available.
        /// </summary>
        public uint? GameIconId { get; init; }

        /// <summary>
        /// The shape used to represent the marker.
        /// </summary>
        public MarkerShape Shape { get; init; } = MarkerShape.Circle;

        /// <summary>
        /// The name of a custom icon, if applicable, located in 'WukLamark/CustomIcons'.
        /// </summary>
        public string? CustomIconName { get; init; }

        /// <summary>
        /// Custom color for this marker (RGBA).
        /// </summary>
        public Vector4 Color { get; init; } = new Vector4(1.0f, 0.8f, 0.0f, 1.0f); // Default to gold/yellow

        /// <summary>
        /// The size the icon should be rendered at.
        /// </summary>
        /// <remarks>
        /// If set to 0.0, the default size from the plugin configuration is used.
        /// </remarks>
        public float Size { get; init; } = 0.0f;

        /// <summary>
        /// Whether to use the shape color when rendering the game icon on the map and minimap. 
        /// </summary>
        public bool UseShapeColor { get; init; }

        /// <summary>
        /// Maximum distance (in yalms) at which this marker is visible on the map/minimap.
        /// </summary>
        /// <remarks>
        /// A value of 0 means the marker is always visible regardless of distance.
        /// </remarks>
        public float VisibilityRadius { get; init; } = 0.0f;
    }
}
