using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using WukLamark.Models;
using WukLamark.Windows.Components;

namespace WukLamark.Windows.Sections;

internal class TableViewSection
{
    private readonly MarkerTableComponent tableComponent;

    public TableViewSection(MarkerTableComponent tableComponent)
    {
        this.tableComponent = tableComponent;
    }

    public void Draw(List<Marker> filteredMarkers, int totalCount)
    {
        ImGui.Text($"Showing {filteredMarkers.Count} of {totalCount} markers");
        ImGui.Spacing();

        tableComponent.Draw(filteredMarkers);
    }
}
