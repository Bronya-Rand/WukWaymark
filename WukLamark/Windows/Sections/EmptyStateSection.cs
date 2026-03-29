using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections;

internal class EmptyStateSection(GameStateReaderService gameStateReaderService)
{
    private readonly GameStateReaderService gameStateReaderService = gameStateReaderService;
    public Action? OnSaveLocation { get; set; }

    public void Draw()
    {
        var isLoggedIn = gameStateReaderService.IsLoggedIn;
        var inPvP = gameStateReaderService.IsInPvP;
        var inCombat = gameStateReaderService.IsInCombat;
        var waymarksDisabled = gameStateReaderService.DisableWaymarkActions();

        var coloredText = "No waymarks saved yet. Create one!";
        if (!isLoggedIn)
            coloredText = "Log in first to save waymarks!";

        var coloredTextSize = ImGui.CalcTextSize(coloredText);
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - coloredTextSize.X) / 2);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), coloredText);
        if (isLoggedIn)
        {
            ImGui.Indent(10);
            ImGui.Text("Use '/wlmark here' or the");
            ImGui.SameLine();
            using (ImRaii.Disabled(waymarksDisabled))
                if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
                {
                    OnSaveLocation?.Invoke();
                }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = inPvP ? "Saving waymarks is disabled in PvP zones" :
                              inCombat ? "Saving waymarks is disabled in combat" :
                              "Save Current Location as Waymark";
                ImGui.SetTooltip(tooltip);
            }
            ImGui.SameLine();
            ImGui.Text("button above to save your current location as a waymark.");
        }
    }
}
