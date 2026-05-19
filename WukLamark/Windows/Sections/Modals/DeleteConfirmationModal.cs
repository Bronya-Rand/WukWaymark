using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections.Modals;

public sealed class DeleteConfirmationModal<T>
{
    private bool isOpen;
    private List<T>? itemsToDelete;

    public Action<List<T>>? OnConfirmDelete { get; set; }
    public Func<T, Plugin, bool>? CanDelete { get; set; }
    public Func<List<T>, string>? PrimaryText { get; set; }
    public string? SecondaryText { get; set; }
    public string ConfirmButtonLabel { get; set; } = "Yes";
    public string CancelButtonLabel { get; set; } = "Cancel";
    public string NoValidTooltip { get; set; } = "No valid items to delete.";
    public Action<List<T>>? DrawExtraContent { get; set; }

    public string PopupId { get; }

    public DeleteConfirmationModal(string popupId)
    {
        PopupId = popupId;
    }

    public void Open(List<T> items)
    {
        itemsToDelete = items;
        isOpen = true;
    }

    public void Draw(Plugin plugin)
    {
        if (!isOpen || itemsToDelete == null) return;

        var validItemsToDelete = new List<T>();
        foreach (var item in itemsToDelete)
        {
            if (item == null) continue;
            if (CanDelete != null && !CanDelete(item, plugin)) continue;
            validItemsToDelete.Add(item);
        }

        ImGui.OpenPopup(PopupId);

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var deleteModal = ImRaii.PopupModal(PopupId, ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (!deleteModal) return;

        if (PrimaryText != null)
            ImWuk.CenteredText(PrimaryText(validItemsToDelete));
        if (SecondaryText is { Length: > 0 })
            ImWuk.CenteredText(SecondaryText);

        DrawExtraContent?.Invoke(validItemsToDelete);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var buttonWidth = 120f;
        var spacing = 10f;
        var totalWidth = (buttonWidth * 2) + spacing;
        var windowWidth = ImGui.GetContentRegionAvail().X;
        var padding = (windowWidth - totalWidth) / 2;

        if (padding > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);

        using (ImRaii.Disabled(validItemsToDelete.Count == 0))
        {
            if (ImGui.Button($"{ConfirmButtonLabel}###DeleteConfirmButton", new Vector2(buttonWidth, 0)))
            {
                OnConfirmDelete?.Invoke(validItemsToDelete);
                isOpen = false;
                itemsToDelete = null;
                ImGui.CloseCurrentPopup();
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
            ImGui.SetTooltip(NoValidTooltip);

        ImGui.SameLine(0, spacing);

        if (ImGui.Button($"{CancelButtonLabel}###DeleteCancelButton", new Vector2(buttonWidth, 0)))
        {
            isOpen = false;
            itemsToDelete = null;
            ImGui.CloseCurrentPopup();
        }
    }
}
