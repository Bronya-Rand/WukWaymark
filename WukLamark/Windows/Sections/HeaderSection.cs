using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections;

internal sealed class HeaderSection(Configuration configuration, GameStateReaderService gameStateReaderService, MarkerStorageService markerStorageService)
{
    private readonly Configuration configuration = configuration;
    private readonly GameStateReaderService gameStateReaderService = gameStateReaderService;
    private readonly MarkerStorageService markerStorageService = markerStorageService;

    public Action<ImportResult, MarkerGroup?>? OnImport { get; set; }
    public Action? OnExportSelected { get; set; }
    public Func<bool>? CanExportMarkers { get; set; }
    public Action<bool>? OnSaveLocation { get; set; }
    public Action? OnToggleView { get; set; }
    public Action? OnSettingsClicked { get; set; }

    public void Draw()
    {
        var isLoggedIn = gameStateReaderService.IsLoggedIn;
        var inPvP = gameStateReaderService.IsInPvP;
        var inCombat = gameStateReaderService.IsInCombat;
        var markersDisabled = gameStateReaderService.DisableMarkerActions();
        var canExportSelected = CanExportMarkers?.Invoke() ?? false;
        var isShiftHeld = ImGui.GetIO().KeyShift;

        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Custom Marker Locations");

        // Position buttons on the right side of the header
        var buttonCount = 5;
        var buttonWidth = ImGui.GetFrameHeight();
        var buttonSpacing = (float)buttonCount * ImGuiHelpers.GlobalScale;
        var totalButtonWidth = (buttonWidth * buttonCount) + (buttonSpacing * (buttonCount + 1));
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - totalButtonWidth);

        // Import from clipboard
        using (ImRaii.Disabled(!isLoggedIn))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport))
            {
                var allKnownMarkers = markerStorageService.GetVisibleMarkers();

                var result = MarkerExportService.ImportFromClipboard(
                    allKnownMarkers);

                OnImport?.Invoke(result, null);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to import markers" : "Import markers from clipboard";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SameLine(0, buttonSpacing);

        // Export selected markers
        using (ImRaii.Disabled(!canExportSelected))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileExport))
                OnExportSelected?.Invoke();
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !canExportSelected ? "No markers selected to export" : "Export selected marker(s) to clipboard";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SameLine(0, buttonSpacing);

        // Save Location button (pin icon)
        using (ImRaii.Disabled(markersDisabled))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
            {
                OnSaveLocation?.Invoke(isShiftHeld);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to save markers!" :
                          inPvP ? "Saving markers is disabled in PvP zones." :
                          inCombat ? "Saving markers is disabled in combat." :
                          "Save Current Location as Marker.\nHold Shift to save this location as a crossworld map marker.";
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
