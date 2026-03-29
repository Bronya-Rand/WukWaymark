using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Sections.Modals;

public class GroupEditorModal
{
    #region Fields

    private bool isOpen = false;
    private WaymarkGroup? editingGroup = null;
    private string groupEditName = "";
    private WaymarkScope groupEditScope = WaymarkScope.Personal;
    private bool groupEditIsReadOnly = false;

    #endregion

    public Action<WaymarkGroup, bool>? OnSave { get; set; }

    public void Open(WaymarkGroup? existingGroup)
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
            groupEditScope = WaymarkScope.Personal;
            groupEditIsReadOnly = false;
        }
        isOpen = true;
    }

    public void Draw(Plugin plugin)
    {
        if (!isOpen) return;

        if (editingGroup != null)
        {
            var isCreatorBlock = editingGroup.CreatorHash == plugin.WaymarkStorageService.CurrentCharacterHash;

            if (editingGroup.Scope == WaymarkScope.Shared && editingGroup.IsReadOnly && !isCreatorBlock)
            {
                Plugin.Log.Warning("Attempted to edit a read-only shared group. Action blocked.");
                editingGroup = null;
                isOpen = false;
                return;
            }

            if (editingGroup.Scope == WaymarkScope.Personal && !isCreatorBlock)
            {
                Plugin.Log.Warning("Attempted to edit someone else's personal group. Action blocked.");
                editingGroup = null;
                isOpen = false;
                return;
            }
        }

        // Validation
        var isCreator = editingGroup != null && editingGroup.CreatorHash == plugin.WaymarkStorageService.CurrentCharacterHash;
        var hasName = !groupEditName.IsNullOrEmpty();
        var canCreateSave = (editingGroup != null && editingGroup.Scope == WaymarkScope.Shared && hasName) ||
                            (editingGroup != null && editingGroup.Scope == WaymarkScope.Personal && isCreator && hasName) ||
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
            ImGui.InputText("###GroupName", ref groupEditName, 100);

            ImGui.Spacing();

            // Scope dropdown
            ImGui.Text("Scope:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            var scopeDropPreview = Enum.GetName(groupEditScope) ?? "Unknown";
            using (ImRaii.Disabled(editingGroup != null && !isCreator))
            {
                using (var scopeDrop = ImRaii.Combo("###GroupScope", scopeDropPreview))
                {
                    if (scopeDrop.Success)
                    {
                        if (ImGui.Selectable(WaymarkScope.Personal.ToString(), groupEditScope == WaymarkScope.Personal))
                            groupEditScope = WaymarkScope.Personal;
                        if (ImGui.Selectable(WaymarkScope.Shared.ToString(), groupEditScope == WaymarkScope.Shared))
                            groupEditScope = WaymarkScope.Shared;
                    }
                }
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = editingGroup != null && !isCreator
                    ? "Only the creator can change the scope of this group."
                    : "Select the scope of the group. Personal groups are private to you, while Shared groups can be seen by others.";
                ImGui.SetTooltip(tooltip);
            }

            // Read-only checkbox (only shown for shared groups and only editable by the creator)
            if (groupEditScope == WaymarkScope.Shared)
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

            ImGui.Spacing();

            // Save/Create button
            var buttonLabel = isEditing ? "Save" : "Create";
            using (ImRaii.Disabled(!canCreateSave))
                if (ImGui.Button($"{buttonLabel}###GroupSaveCreateButton"))
                {
                    if (!string.IsNullOrWhiteSpace(groupEditName))
                    {
                        OnSave?.Invoke(new WaymarkGroup
                        {
                            Id = editingGroup?.Id ?? Guid.NewGuid(),
                            Name = groupEditName,
                            CreatorHash = editingGroup?.CreatorHash ?? plugin.WaymarkStorageService.CurrentCharacterHash,
                            IsReadOnly = groupEditScope == WaymarkScope.Shared && groupEditIsReadOnly,
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
