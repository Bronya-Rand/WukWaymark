using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections.Modals;

public sealed class GroupEditorModal
{
    #region Fields

    private bool isOpen = false;
    private MarkerGroup? editingGroup = null;
    private string groupEditName = "";
    private MarkerScope groupEditScope = MarkerScope.Personal;
    private bool groupEditIsReadOnly = false;

    #endregion

    public Action<MarkerGroup, bool>? OnSave { get; set; }

    public void Open(MarkerGroup? existingGroup)
    {
        if (existingGroup != null)
        {
            editingGroup = existingGroup;
            groupEditName = existingGroup.Name;
            groupEditScope = existingGroup.Scope;
            groupEditIsReadOnly = existingGroup.IsReadOnly;
        }
        else
        {
            editingGroup = null;
            groupEditName = "";
            groupEditScope = MarkerScope.Personal;
            groupEditIsReadOnly = false;
        }
        isOpen = true;
    }

    public void Draw(Plugin plugin)
    {
        if (!isOpen) return;

        if (editingGroup != null)
        {
            var isCreatorBlock = editingGroup.CreatorHash == plugin.MarkerStorageService.CurrentCharacterHash;

            if (editingGroup.Scope == MarkerScope.Shared && editingGroup.IsReadOnly && !isCreatorBlock)
            {
                Plugin.Log.Warning("Attempted to edit a read-only shared group. Action blocked.");
                editingGroup = null;
                isOpen = false;
                return;
            }

            if (editingGroup.Scope == MarkerScope.Personal && !isCreatorBlock)
            {
                Plugin.Log.Warning("Attempted to edit someone else's personal group. Action blocked.");
                editingGroup = null;
                isOpen = false;
                return;
            }
        }

        // Validation
        var isCreator = editingGroup != null && editingGroup.CreatorHash == plugin.MarkerStorageService.CurrentCharacterHash;
        var isEditingSharedReadOnly = editingGroup != null && editingGroup.Scope == MarkerScope.Shared && editingGroup.IsReadOnly;
        var hasName = !groupEditName.IsNullOrEmpty();
        var canCreateSave = (editingGroup != null && editingGroup.Scope == MarkerScope.Shared && hasName) ||
                            (editingGroup != null && editingGroup.Scope == MarkerScope.Personal && isCreator && hasName) ||
                            (editingGroup == null && hasName);

        var isEditing = editingGroup != null;
        var modalTitle = isEditing ? "Edit Group" : "Create Group";
        var modalTitleWithId = $"{modalTitle}##WWGroupEditorModal";

        ImGui.OpenPopup(modalTitleWithId);

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var editorModal = ImRaii.PopupModal(modalTitleWithId, ref isOpen, ImGuiWindowFlags.AlwaysAutoResize);
        if (editorModal)
        {
            ImGui.Text("Group Name:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            using (ImRaii.Disabled(isEditingSharedReadOnly))
                ImGui.InputText("###GroupName", ref groupEditName, 100);
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                ImGui.SetTooltip("Shared read-only groups can only toggle read-only before editing other fields.");
            }

            ImGui.Spacing();

            // Scope dropdown
            ImGui.Text("Scope:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            var scopeDropPreview = Enum.GetName(groupEditScope) ?? "Unknown";
            using (ImRaii.Disabled((editingGroup != null && !isCreator) || isEditingSharedReadOnly))
            {
                using (var scopeDrop = ImRaii.Combo("###GroupScope", scopeDropPreview))
                {
                    if (scopeDrop.Success)
                    {
                        if (ImGui.Selectable(MarkerScope.Personal.ToString(), groupEditScope == MarkerScope.Personal))
                            groupEditScope = MarkerScope.Personal;
                        if (ImGui.Selectable(MarkerScope.Shared.ToString(), groupEditScope == MarkerScope.Shared))
                            groupEditScope = MarkerScope.Shared;
                    }
                }
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = isEditingSharedReadOnly
                    ? "Disable read-only before changing the scope of this group."
                    : editingGroup != null && !isCreator
                    ? "Only the creator can change the scope of this group."
                    : "Select the scope of the group. Personal groups are private to you, while Shared groups can be seen by others.";
                ImGui.SetTooltip(tooltip);
            }

            // Read-only checkbox (only shown in edit mode for shared groups and only editable by the creator)
            if (groupEditScope == MarkerScope.Shared && editingGroup != null)
            {
                ImGui.Spacing();
                using (ImRaii.Disabled(groupEditName.IsNullOrEmpty() || (editingGroup != null && !isCreator)))
                {
                    ImGui.Checkbox("Read-Only (Prevents deletion)###GroupReadOnly", ref groupEditIsReadOnly);
                }
                if (ImWuk.IsItemHoveredWhenDisabled())
                {
                    var tooltip = !isCreator ? "Only the creator can toggle read-only for this group."
                        : groupEditName.IsNullOrEmpty() ? "Enter a group name to toggle read-only."
                        : "When enabled, only you can edit this shared group. Prevents deletion until disabled.";
                    ImGui.SetTooltip(tooltip);
                }
            }
            else
                groupEditIsReadOnly = false;

            ImGui.Spacing();

            // Save/Create button
            var buttonLabel = isEditing ? "Save" : "Create";
            using (ImRaii.Disabled(!canCreateSave))
                if (ImGui.Button($"{buttonLabel}###GroupSaveCreateButton"))
                {
                    if (!string.IsNullOrWhiteSpace(groupEditName))
                    {
                        OnSave?.Invoke(new MarkerGroup
                        {
                            Id = editingGroup?.Id ?? Guid.NewGuid(),
                            Name = groupEditName,
                            CreatorHash = editingGroup?.CreatorHash ?? plugin.MarkerStorageService.CurrentCharacterHash,
                            IsReadOnly = groupEditScope == MarkerScope.Shared && groupEditIsReadOnly,
                            Scope = groupEditScope
                        }, isEditing);
                        editingGroup = null;
                        isOpen = false;
                    }
                }

            ImGui.SameLine();

            if (ImGui.Button("Cancel###GroupCancelButton"))
            {
                editingGroup = null;
                isOpen = false;
            }
        }
    }
}
