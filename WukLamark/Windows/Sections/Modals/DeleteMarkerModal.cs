using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections.Modals;

public sealed class DeleteMarkerModal
{
    private readonly DeleteConfirmationModal<Marker> confirmationModal = new("Delete Marker?##WWDeleteMarkerModal")
    {
        SecondaryText = "This action can be undone with the Undo button.",
        NoValidTooltip = "No valid markers to delete."
    };

    public Action<List<Marker>>? OnConfirmDelete
    {
        get => confirmationModal.OnConfirmDelete;
        set => confirmationModal.OnConfirmDelete = value;
    }

    public DeleteMarkerModal(Plugin plugin)
    {
        confirmationModal.CanDelete = (marker, servicePlugin) =>
        {
            if (marker.Scope == MarkerScope.Personal && (marker.IsReadOnly ||
                    marker.CharacterHash == null ||
                    servicePlugin.MarkerStorageService.CurrentCharacterHash == null ||
                    marker.CharacterHash != servicePlugin.MarkerStorageService.CurrentCharacterHash))
                return false;

            if (marker.Scope == MarkerScope.Shared && marker.IsReadOnly)
                return false;

            return true;
        };

        confirmationModal.PrimaryText = markers => markers.Count == 1
            ? $"Are you sure you want to delete marker '{markers[0].Name}'?"
            : $"Are you sure you want to delete these {markers.Count} markers?";

        confirmationModal.DrawExtraContent = markers =>
        {
            if (markers.Count > 10)
            {
                ImGui.Spacing();
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed))
                {
                    ImWuk.CenteredText($"Warning: {markers.Count - 10} marker(s) will not be able to be undone once deleted.");
                }
            }
        };
    }

    public void Open(List<Marker> marker) => confirmationModal.Open(marker);
    public void Draw(Plugin plugin) => confirmationModal.Draw(plugin);
}
