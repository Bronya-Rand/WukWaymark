namespace WukWaymark.Models
{
    /// <summary>
    /// Enumeration of available marker shapes for displaying waymarks on the map.
    /// 
    /// Each shape has a distinct visual appearance and is rendered with:
    /// - Black outline for visibility against various backgrounds
    /// - Customizable size (globally via Configuration.WaymarkMarkerSize)
    /// - Per-waymark custom color
    /// </summary>
    public enum WaymarkShape
    {
        /// <summary>Classic circular marker - round filled circle with outline</summary>
        Circle,

        /// <summary>Square/rectangular marker - useful for grid-like landmarks</summary>
        Square,

        /// <summary>Triangle marker pointing upward - good for directional markers</summary>
        Triangle,

        /// <summary>Diamond/rhombus marker - distinctive rotated square shape</summary>
        Diamond,

        /// <summary>Five-pointed star marker - stands out visually on the map</summary>
        Star
    }
}
