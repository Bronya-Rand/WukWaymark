using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections;

internal class SearchBarSection(GameStateReaderService gameStateReaderService, WaymarkService waymarkService)
{
    private readonly GameStateReaderService gameStateReaderService = gameStateReaderService;
    private readonly WaymarkService waymarkService = waymarkService;

    public string SearchFilter = string.Empty;
    public bool FilterCurrentZone;

    public void Draw()
    {
        var isLoggedIn = gameStateReaderService.IsLoggedIn;
        var inPvP = gameStateReaderService.IsInPvP;
        var inCombat = gameStateReaderService.IsInCombat;
        var waymarksDisabled = gameStateReaderService.DisableWaymarkActions();

        // Calculate the width of the elements that will be placed to the right of the search bar
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        // "Current Zone" checkbox width
        var checkboxTextWidth = ImGui.CalcTextSize("Current Zone").X;
        var checkboxFramePadded = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        var checkboxTotalWidth = checkboxFramePadded + checkboxTextWidth;

        var rightElementsWidth = checkboxTotalWidth + spacing;

        // Undo button width
        if (waymarkService.CanUndo)
        {
            var undoButtonWidth = ImGui.GetFrameHeight();
            rightElementsWidth += undoButtonWidth + spacing;
        }

        // Search input
        ImGui.SetNextItemWidth(Math.Max(50, ImGui.GetContentRegionAvail().X - rightElementsWidth));
        ImGui.InputTextWithHint("##Search", "Search waymarks...", ref SearchFilter, 200);

        ImGui.SameLine();

        // Zone filter toggle
        using (ImRaii.Disabled(waymarksDisabled))
            ImGui.Checkbox("Current Zone", ref FilterCurrentZone);
        if (ImWuk.IsItemHoveredWhenDisabled())
            ImGui.SetTooltip(!isLoggedIn ? "Log in to filter waymarks!" :
                             inPvP ? "Filtering waymarks is disabled in PvP zones" :
                             inCombat ? "Filtering waymarks is disabled in combat" :
                             "Only show waymarks in the current zone");

        // Undo button (only when deletions exist)
        if (waymarkService.CanUndo)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(!isLoggedIn))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
                {
                    waymarkService.UndoDelete();
                }
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = !isLoggedIn ? "Log in to undo deletions!" :
                              $"Undo Deleted Waymark ({waymarkService.UndoCount})";
                ImGui.SetTooltip(tooltip);
            }
        }
    }
}
