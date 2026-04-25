using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Linq;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections.Modals;

public sealed class DeleteGroupModal
{
    private bool isOpen = false;
    private MarkerGroup? groupToDelete = null;
    private bool keepMarkers = false;

    public Action<MarkerGroup, bool>? OnConfirmDelete { get; set; }

    public void Open(MarkerGroup group)
    {
        groupToDelete = group;
        keepMarkers = true;
        isOpen = true;
    }

    public void Draw(Plugin plugin)
    {
        if (!isOpen || groupToDelete == null) return;

        // Validation
        var isCreator = groupToDelete.CreatorHash != null &&
                        plugin.MarkerStorageService.CurrentCharacterHash != null &&
                        groupToDelete.CreatorHash == plugin.MarkerStorageService.CurrentCharacterHash;

        if (groupToDelete.IsReadOnly || !isCreator)
        {
            Plugin.Log.Warning("Attempted to delete a group that doesn't belong to the current character. Action blocked.");
            isOpen = false;
            groupToDelete = null;
            return;
        }

        ImGui.OpenPopup("Delete Group?##WWDeleteGroupModal");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var deleteGroupModal = ImRaii.PopupModal("Delete Group?##WWDeleteGroupModal", ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (deleteGroupModal)
        {
            var allMarkers = plugin.MarkerStorageService.GetVisibleMarkers();
            var markersInGroup = allMarkers.Where(w => w.GroupId == groupToDelete.Id).Count();
            ImWuk.CenteredText($"Are you sure you want to delete the group '{groupToDelete.Name}'?");
            if (markersInGroup > 0)
            {
                ImWuk.CenteredText($"This group contains {markersInGroup} marker(s).");
                ImGui.Checkbox("Keep markers (move to Ungrouped)", ref keepMarkers);
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

            if (ImGui.Button("Delete", new Vector2(buttonWidth, 0)))
            {
                OnConfirmDelete?.Invoke(groupToDelete, keepMarkers);
                isOpen = false;
                groupToDelete = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, spacing);

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                isOpen = false;
                groupToDelete = null;
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
