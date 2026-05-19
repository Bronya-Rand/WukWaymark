using System;
using WukLamark.Models;

namespace WukLamark.Windows.Components
{
    /// <summary>
    /// Represents the result of editing a marker template.
    /// </summary>
    public sealed class MarkerTemplateEditResult : IEditableMarkerResult
    {
        public string Name { get; init; } = string.Empty;
        public Guid? GroupId { get; init; } = null;
        public required MarkerIcon Icon { get; init; }
        public MarkerScope Scope { get; init; }
        public bool AppliesToAllWorlds { get; init; }
    }
}
