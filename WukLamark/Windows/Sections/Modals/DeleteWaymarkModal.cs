using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;
using WukLamark.Models;

namespace WukLamark.Windows.Sections.Modals;

public class DeleteWaymarkModal
{
    private bool isOpen = false;
    private Waymark? waymarkToDelete = null;

    public Action<Waymark>? OnConfirmDelete { get; set; }

    public void Open(Waymark waymark)
    {
        waymarkToDelete = waymark;
        isOpen = true;
    }

    public void Draw(Plugin plugin)
    {
        if (!isOpen || waymarkToDelete == null) return;

        // Validation
        if ((waymarkToDelete.Scope == WaymarkScope.Shared &&
            waymarkToDelete.IsReadOnly) ||
            (waymarkToDelete.Scope == WaymarkScope.Personal &&
            (waymarkToDelete.CharacterHash == null ||
            plugin.WaymarkStorageService.CurrentCharacterHash == null ||
            waymarkToDelete.CharacterHash != plugin.WaymarkStorageService.CurrentCharacterHash)))
        {
            Plugin.Log.Warning("Attempted to delete a waymark that doesn't belong to the current character. Action blocked.");
            isOpen = false;
            waymarkToDelete = null;
            return;
        }

        ImGui.OpenPopup("Delete Waypoint?##WWDeleteWaypointModal");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var deleteWaypointModal = ImRaii.PopupModal("Delete Waypoint?##WWDeleteWaypointModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (deleteWaypointModal)
        {
            ImGui.Text($"Are you sure you want to delete the waymark '{waymarkToDelete.Name}'?");
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

            if (ImGui.Button("Yes###DeleteWaypointYesButton", new Vector2(buttonWidth, 0)))
            {
                OnConfirmDelete?.Invoke(waymarkToDelete);
                isOpen = false;
                waymarkToDelete = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, spacing);

            if (ImGui.Button("Cancel###DeleteWaypointCancelButton", new Vector2(buttonWidth, 0)))
            {
                isOpen = false;
                waymarkToDelete = null;
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
