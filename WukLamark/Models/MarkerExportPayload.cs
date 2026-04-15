using System.Collections.Generic;

namespace WukLamark.Models
{
    public class MarkerExportPayload
    {
        public string Version { get; set; } = "1";
        public string Type { get; set; } = "Share";
        public List<MarkerShareEntry> Markers { get; set; } = [];
    }
}
