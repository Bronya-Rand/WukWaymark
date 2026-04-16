using System;
using System.Numerics;

namespace WukLamark.Models
{
    public class MarkerShareEntry
    {
        public Guid? SourceId { get; set; }
        public string Name { get; set; } = "Unnamed Location";
        public Vector3 Position { get; set; }
        public ushort TerritoryId { get; set; }
        public sbyte WardId { get; set; }
        public uint MapId { get; set; }
        public uint WorldId { get; set; }
        public Vector4 Color { get; set; }
        public MarkerShape Shape { get; set; }
        public uint? IconId { get; set; }
        public bool AppliesToAllWorlds { get; set; }
    }
}
