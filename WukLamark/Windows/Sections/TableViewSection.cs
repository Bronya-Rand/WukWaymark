using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using WukLamark.Models;
using WukLamark.Windows.Components;

namespace WukLamark.Windows.Sections;

internal class TableViewSection(MarkerTableComponent tableComponent)
{
    private readonly MarkerTableComponent tableComponent = tableComponent;

    public void Draw(List<Marker> filteredMarkers, int totalCount)
    {
        ImGui.Text($"Showing {filteredMarkers.Count} of {totalCount} markers");
        ImGui.Spacing();

        tableComponent.Draw(filteredMarkers);
    }
}
