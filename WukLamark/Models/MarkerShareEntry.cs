using System;
using System.Numerics;

namespace WukLamark.Models
{
    public sealed class MarkerShareEntry
    {
        public Guid? SourceId { get; set; }
        public string Name { get; set; } = "Unnamed Location";
        public Vector3 Position { get; set; }
        public uint TerritoryId { get; set; }
        public sbyte WardId { get; set; }
        public uint MapId { get; set; }
        public uint WorldId { get; set; }
        public bool AppliesToAllWorlds { get; set; }
        public required MarkerIcon Icon { get; set; }
    }
}
