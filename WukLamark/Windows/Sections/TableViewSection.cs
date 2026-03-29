using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using WukLamark.Models;
using WukLamark.Windows.Components;

namespace WukLamark.Windows.Sections;

internal class TableViewSection
{
    private readonly WaymarkTableComponent tableComponent;

    public TableViewSection(WaymarkTableComponent tableComponent)
    {
        this.tableComponent = tableComponent;
    }

    public void Draw(List<Waymark> filteredWaymarks, int totalCount)
    {
        ImGui.Text($"Showing {filteredWaymarks.Count} of {totalCount} waymarks");
        ImGui.Spacing();

        tableComponent.Draw(filteredWaymarks);
    }
}
