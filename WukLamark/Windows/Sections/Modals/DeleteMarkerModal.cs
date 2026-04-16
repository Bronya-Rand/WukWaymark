using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections.Modals;

public class DeleteMarkerModal
{
    private bool isOpen = false;
    private List<Marker>? markersToDelete = null;

    public Action<List<Marker>>? OnConfirmDelete { get; set; }

    public void Open(List<Marker> marker)
    {
        markersToDelete = marker;
        isOpen = true;
    }

    public void Draw(Plugin plugin)
    {
        if (!isOpen || markersToDelete == null) return;

        // Validation for marker deletion
        var validMarkersToDelete = new List<Marker>();
        foreach (var marker in markersToDelete)
        {
            if (marker == null) continue;

            // Personal Marker Check:
            // 1. Must belong to the character who made the marker
            // 2. Cannot be read-only
            // 3. Hashes must match
            // 4. Must have current character hash set in storage service
            if (marker.Scope == MarkerScope.Personal && (marker.IsReadOnly ||
                    marker.CharacterHash == null ||
                    plugin.MarkerStorageService.CurrentCharacterHash == null ||
                    marker.CharacterHash != plugin.MarkerStorageService.CurrentCharacterHash))
            { continue; }

            // Shared Marker Check:
            // 1. Cannot be read-only
            if (marker.Scope == MarkerScope.Shared && marker.IsReadOnly) continue;

            validMarkersToDelete.Add(marker);

        }

        ImGui.OpenPopup("Delete Marker?##WWDeleteMarkerModal");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var deleteMarkerModal = ImRaii.PopupModal("Delete Marker?##WWDeleteMarkerModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (deleteMarkerModal)
        {
            if (validMarkersToDelete.Count == 1)
            {
                var marker = validMarkersToDelete[0];
                ImWuk.CenteredText($"Are you sure you want to delete marker '{marker.Name}'?");
            }
            else
                ImWuk.CenteredText($"Are you sure you want to delete these {validMarkersToDelete.Count} markers?");
            ImWuk.CenteredText("This action can be undone with the Undo button.");

            // Add danger text for marker deletion in excess of 10
            if (validMarkersToDelete.Count > 10)
            {
                ImGui.Spacing();
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
                {
                    ImWuk.CenteredText($"Warning: {validMarkersToDelete.Count - 10} marker(s) will not be able to be undone once deleted.");
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Center buttons
            var buttonWidth = 120f;
            var spacing = 10f;
            var totalWidth = (buttonWidth * 2) + spacing;
            var windowWidth = ImGui.GetContentRegionAvail().X;
            var padding = (windowWidth - totalWidth) / 2;

            if (padding > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);

            using (ImRaii.Disabled(validMarkersToDelete.Count == 0))
            {
                if (ImGui.Button("Yes###DeleteMarkerYesButton", new Vector2(buttonWidth, 0)))
                {
                    OnConfirmDelete?.Invoke(validMarkersToDelete);
                    isOpen = false;
                    markersToDelete = null;
                    ImGui.CloseCurrentPopup();
                }
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
                ImGui.SetTooltip("No valid markers to delete.");

            ImGui.SameLine(0, spacing);

            if (ImGui.Button("Cancel###DeleteMarkerCancelButton", new Vector2(buttonWidth, 0)))
            {
                isOpen = false;
                markersToDelete = null;
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
