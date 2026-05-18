using System;

namespace WukLamark.Models
{
    [Serializable]
    public class MarkerTemplate
    {
        public int FileVersion { get; set; } = 1;
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New Template";
        public MarkerIcon DefaultIcon { get; set; } = new MarkerIcon
        {
            SourceType = MarkerIconType.Shape,
            Shape = MarkerShape.Circle,
            Size = 0.0f,
            UseShapeColor = true
        };
        public MarkerScope DefaultScope { get; set; } = MarkerScope.Personal;
        public bool DefaultAppliesToAllWorlds { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        // For use in personal scoped templates
        public string? CharacterHash { get; set; }
    }
}
