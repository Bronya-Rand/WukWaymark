using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections;

internal class SearchBarSection(GameStateReaderService gameStateReaderService, MarkerService markerService)
{
    private readonly GameStateReaderService gameStateReaderService = gameStateReaderService;
    private readonly MarkerService markerService = markerService;

    public string SearchFilter = string.Empty;
    public bool FilterCurrentZone;

    public void Draw()
    {
        var isLoggedIn = gameStateReaderService.IsLoggedIn;
        var inPvP = gameStateReaderService.IsInPvP;
        var inCombat = gameStateReaderService.IsInCombat;
        var markersDisabled = gameStateReaderService.DisableMarkerActions();

        // Calculate the width of the elements that will be placed to the right of the search bar
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        // "Current Zone" checkbox width
        var checkboxTextWidth = ImGui.CalcTextSize("Current Zone").X;
        var checkboxFramePadded = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        var checkboxTotalWidth = checkboxFramePadded + checkboxTextWidth;

        var rightElementsWidth = checkboxTotalWidth + spacing;

        // Undo button width
        if (markerService.CanUndo)
        {
            var undoButtonWidth = ImGui.GetFrameHeight();
            rightElementsWidth += undoButtonWidth + spacing;
        }

        // Search input
        ImGui.SetNextItemWidth(Math.Max(50, ImGui.GetContentRegionAvail().X - rightElementsWidth));
        ImGui.InputTextWithHint("##Search", "Search markers...", ref SearchFilter, 200);

        ImGui.SameLine();

        // Zone filter toggle
        using (ImRaii.Disabled(markersDisabled))
            ImGui.Checkbox("Current Zone", ref FilterCurrentZone);
        if (ImWuk.IsItemHoveredWhenDisabled())
            ImGui.SetTooltip(!isLoggedIn ? "Log in to filter markers!" :
                             inPvP ? "Filtering markers is disabled in PvP zones" :
                             inCombat ? "Filtering markers is disabled in combat" :
                             "Only show markers in the current zone");

        // Undo button (only when deletions exist)
        if (markerService.CanUndo)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(!isLoggedIn))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
                {
                    markerService.UndoDelete();
                }
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = !isLoggedIn ? "Log in to undo deletions!" :
                              $"Undo Deleted Marker ({markerService.UndoCount})";
                ImGui.SetTooltip(tooltip);
            }
        }
    }
}
