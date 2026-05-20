using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections.Modals;

public sealed class DeleteGroupModal
{
    private readonly DeleteConfirmationModal<MarkerGroup> confirmationModal = new("Delete Group?##WWDeleteGroupModal")
    {
        ConfirmButtonLabel = "Delete",
        NoValidTooltip = "No valid groups to delete."
    };

    private bool keepMarkers = true;

    public Action<MarkerGroup, bool>? OnConfirmDelete { get; set; }

    public DeleteGroupModal(Plugin plugin)
    {
        confirmationModal.CanDelete = (group, servicePlugin) =>
        {
            var isCreator = group.CreatorHash != null &&
                            servicePlugin.MarkerStorageService.CurrentCharacterHash != null &&
                            group.CreatorHash == servicePlugin.MarkerStorageService.CurrentCharacterHash;

            if (group.IsReadOnly || !isCreator)
            {
                Plugin.Log.Warning("Attempted to delete a group that doesn't belong to the current character. Action blocked.");
                return false;
            }

            return true;
        };

        confirmationModal.PrimaryText = groups => $"Are you sure you want to delete the group '{groups[0].Name}'?";
        confirmationModal.DrawExtraContent = groups =>
        {
            var group = groups[0];
            var allMarkers = plugin.MarkerStorageService.GetVisibleMarkers();
            var markersInGroup = allMarkers.Count(w => w.GroupId == group.Id);
            if (markersInGroup > 0)
            {
                ImWuk.CenteredText($"This group contains {markersInGroup} marker(s).");
                ImGui.Checkbox("Keep markers (move to Ungrouped)", ref keepMarkers);
            }
        };

        confirmationModal.OnConfirmDelete = groups => OnConfirmDelete?.Invoke(groups[0], keepMarkers);
    }

    public void Open(MarkerGroup group)
    {
        keepMarkers = true;
        confirmationModal.Open([group]);
    }
    public void Draw(Plugin plugin) => confirmationModal.Draw(plugin);
}
