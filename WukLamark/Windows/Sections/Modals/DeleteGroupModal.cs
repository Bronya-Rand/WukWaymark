using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Linq;
using System.Numerics;
using WukLamark.Models;

namespace WukLamark.Windows.Sections.Modals;

public class DeleteGroupModal
{
    private bool isOpen = false;
    private WaymarkGroup? groupToDelete = null;
    private bool keepWaymarks = false;

    public Action<WaymarkGroup, bool>? OnConfirmDelete { get; set; }

    public void Open(WaymarkGroup group)
    {
        groupToDelete = group;
        keepWaymarks = false;
        isOpen = true;
    }

    public void Draw(Plugin plugin)
    {
        if (!isOpen || groupToDelete == null) return;

        // Validation
        var isCreator = groupToDelete.CreatorHash != null &&
                        plugin.WaymarkStorageService.CurrentCharacterHash != null &&
                        groupToDelete.CreatorHash == plugin.WaymarkStorageService.CurrentCharacterHash;

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
            var allWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();
            var waymarksInGroup = allWaymarks.Where(w => w.GroupId == groupToDelete.Id).Count();
            ImGui.Text($"Are you sure you want to delete the group '{groupToDelete.Name}'?");
            if (waymarksInGroup > 0)
            {
                ImGui.Text($"This group contains {waymarksInGroup} waymark(s).");
                ImGui.Checkbox("Keep waymarks (move to Ungrouped)", ref keepWaymarks);
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
                OnConfirmDelete?.Invoke(groupToDelete, keepWaymarks);
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
