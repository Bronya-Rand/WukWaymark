using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;
using WukLamark.Models;

namespace WukLamark.Windows.Sections.Modals;

public class DeleteMarkerModal
{
    private bool isOpen = false;
    private Marker? markerToDelete = null;

    public Action<Marker>? OnConfirmDelete { get; set; }

    public void Open(Marker marker)
    {
        markerToDelete = marker;
        isOpen = true;
    }

    public void Draw(Plugin plugin)
    {
        if (!isOpen || markerToDelete == null) return;

        // Validation
        if ((markerToDelete.Scope == MarkerScope.Shared &&
            markerToDelete.IsReadOnly) ||
            (markerToDelete.Scope == MarkerScope.Personal &&
            (markerToDelete.CharacterHash == null ||
            plugin.MarkerStorageService.CurrentCharacterHash == null ||
            markerToDelete.CharacterHash != plugin.MarkerStorageService.CurrentCharacterHash)))
        {
            Plugin.Log.Warning("Attempted to delete a marker that doesn't belong to the current character. Action blocked.");
            isOpen = false;
            markerToDelete = null;
            return;
        }

        ImGui.OpenPopup("Delete Marker?##WWDeleteMarkerModal");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var deleteMarkerModal = ImRaii.PopupModal("Delete Marker?##WWDeleteMarkerModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (deleteMarkerModal)
        {
            ImGui.Text($"Are you sure you want to delete the marker '{markerToDelete.Name}'?");
            ImGui.Text("This action can be undone with the Undo button.");

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

            if (ImGui.Button("Yes###DeleteMarkerYesButton", new Vector2(buttonWidth, 0)))
            {
                OnConfirmDelete?.Invoke(markerToDelete);
                isOpen = false;
                markerToDelete = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, spacing);

            if (ImGui.Button("Cancel###DeleteMarkerCancelButton", new Vector2(buttonWidth, 0)))
            {
                isOpen = false;
                markerToDelete = null;
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
