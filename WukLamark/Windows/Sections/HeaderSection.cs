using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections;

internal class HeaderSection(Configuration configuration, GameStateReaderService gameStateReaderService, WaymarkStorageService waymarkStorageService)
{
    private readonly Configuration configuration = configuration;
    private readonly GameStateReaderService gameStateReaderService = gameStateReaderService;
    private readonly WaymarkStorageService waymarkStorageService = waymarkStorageService;

    public Action<ImportResult>? OnImport { get; set; }
    public Action? OnSaveLocation { get; set; }
    public Action? OnToggleView { get; set; }
    public Action? OnSettingsClicked { get; set; }

    public void Draw()
    {
        var isLoggedIn = gameStateReaderService.IsLoggedIn;
        var inPvP = gameStateReaderService.IsInPvP;
        var inCombat = gameStateReaderService.IsInCombat;
        var waymarksDisabled = gameStateReaderService.DisableWaymarkActions();

        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Custom Waymark Locations");

        // Position buttons on the right side of the header
        var buttonWidth = ImGui.GetFrameHeight();
        var buttonSpacing = 5.0f * ImGuiHelpers.GlobalScale;
        var totalButtonWidth = (buttonWidth * 4) + (buttonSpacing * 4);
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - totalButtonWidth);

        // Import from clipboard
        using (ImRaii.Disabled(!isLoggedIn))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport))
            {
                var allKnownWaymarks = waymarkStorageService.GetVisibleWaymarks();
                var allKnownGroups = waymarkStorageService.GetVisibleGroups();

                var result = WaymarkExportService.ImportFromClipboard(
                    allKnownWaymarks,
                    allKnownGroups);

                OnImport?.Invoke(result);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to import waymarks" : "Import waymarks from clipboard";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SameLine(0, buttonSpacing);

        // Save Location button (pin icon)
        using (ImRaii.Disabled(waymarksDisabled))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
            {
                OnSaveLocation?.Invoke();
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to save waymarks" :
                          inPvP ? "Saving waymarks is disabled in PvP zones" :
                          inCombat ? "Saving waymarks is disabled in combat" :
                          "Save Current Location as Waymark";
            using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, 1.0f))
                ImGui.SetTooltip(tooltip);
        }
        ImGui.SameLine(0, buttonSpacing);

        // View toggle button
        var viewIcon = configuration.UseGroupView ? FontAwesomeIcon.List : FontAwesomeIcon.FolderOpen;
        var viewTooltip = configuration.UseGroupView ? "Switch to Table View" : "Switch to Group View";
        if (ImGuiComponents.IconButton(viewIcon))
        {
            OnToggleView?.Invoke();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(viewTooltip);
        }
        ImGui.SameLine(0, buttonSpacing);

        // Settings button (gear icon)
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            OnSettingsClicked?.Invoke();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Settings");
        }
    }
}
