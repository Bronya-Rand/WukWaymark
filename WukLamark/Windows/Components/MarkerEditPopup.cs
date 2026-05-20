using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Components;

/// <summary>
/// Represents a popup dialog for editing marker properties.
/// </summary>
internal sealed class MarkerEditPopup
{
    private readonly Plugin plugin;
    private readonly IconEditFields iconEditFields;

    #region Editing State

    private Guid? editingGroupId;
    private Guid? editingTemplateId;

    private string editingName = string.Empty;
    private string editingNote = string.Empty;
    private MarkerScope editingScope = MarkerScope.Personal;
    private bool editingReadOnly = false;
    private bool editingAppliesToAllWorlds = false;

    #endregion

    public Action<Marker, MarkerEditResult>? OnSave { get; set; }

    public MarkerEditPopup(Plugin plugin)
    {
        this.plugin = plugin;
        iconEditFields = new IconEditFields(plugin);
    }

    /// <summary>
    /// Loads editing state from the given marker.
    /// </summary>
    /// <remarks>Call this before opening the popup.</remarks>
    public void LoadFromMarker(Marker marker, MarkerGroup? markerGroup)
    {
        editingGroupId = markerGroup?.Id;
        editingTemplateId = marker.TemplateId;

        editingName = marker.Name;
        editingNote = marker.Notes;
        editingScope = marker.Scope;
        editingReadOnly = marker.IsReadOnly;
        editingAppliesToAllWorlds = marker.AppliesToAllWorlds;

        iconEditFields.LoadFrom(marker.Icon);
    }

    public void Draw(Marker marker, MarkerGroup? parentGroup = null)
    {
        var identifier = marker.Id.ToString();
        using var editMarkerPopup = ImRaii.Popup($"EditMarker##{identifier}");

        if (!editMarkerPopup) return;

        var currentHash = plugin.MarkerStorageService.CurrentCharacterHash;
        var isMarkerCreator = marker.CharacterHash != null &&
                               currentHash != null &&
                               marker.CharacterHash == currentHash;

        // Inherit scope from group if marker is in a group
        var isGrouped = parentGroup != null;
        var effectiveScope = isGrouped ? parentGroup!.Scope : marker.Scope;

        if (isGrouped)
        {
            editingScope = parentGroup!.Scope;
        }

        var selectedScope = isGrouped ? parentGroup!.Scope : editingScope;
        var canOpenEdit = false;
        var cannotEditReason = string.Empty;

        // Validate edit permissions before rendering any fields
        if (isGrouped)
        {
            // Validate group permissions
            var isGroupCreator = parentGroup!.CreatorHash != null &&
                                 currentHash != null &&
                                 parentGroup.CreatorHash == currentHash;

            if (parentGroup.Scope == MarkerScope.Personal)
            {
                canOpenEdit = isGroupCreator;
                if (!canOpenEdit)
                    cannotEditReason = "Only the group's creator can edit markers in this personal group.";
            }
            else
            {
                if (parentGroup.IsReadOnly)
                {
                    canOpenEdit = isMarkerCreator;
                    if (!canOpenEdit)
                        cannotEditReason = "This group is read-only and only the marker creator can edit it.";
                }
                else
                {
                    canOpenEdit = !marker.IsReadOnly || isMarkerCreator;
                    if (!canOpenEdit)
                        cannotEditReason = $"'{marker.Name}' is read-only and cannot be edited by non-creators";
                }
            }
        }
        else
        {
            // Validate individual marker permissions
            canOpenEdit = (effectiveScope == MarkerScope.Personal && isMarkerCreator) ||
                          (effectiveScope == MarkerScope.Shared && (!marker.IsReadOnly || isMarkerCreator));

            if (!canOpenEdit)
            {
                cannotEditReason = effectiveScope == MarkerScope.Personal
                    ? $"Only the creator can edit '{marker.Name}'."
                    : $"'{marker.Name}' is read-only and cannot be edited.";
            }
        }

        // Exit early if user lacks permissions
        if (!canOpenEdit)
        {
            Plugin.Log.Warning($"Edit popup opened without permission for marker '{marker.Id}'. {cannotEditReason}.");
            ImGui.CloseCurrentPopup();
            return;
        }

        var inheritedReadOnly = isGrouped && parentGroup!.Scope == MarkerScope.Shared && parentGroup.IsReadOnly;
        var isSharedReadOnly = selectedScope == MarkerScope.Shared && (editingReadOnly || inheritedReadOnly);
        var canEditGeneralFields = !isSharedReadOnly;
        var canEditScope = !isGrouped && isMarkerCreator && !editingReadOnly;
        var canEditReadOnly = selectedScope == MarkerScope.Shared && isMarkerCreator && !editingName.IsNullOrEmpty();

        var canSave = !editingName.IsNullOrEmpty();
        // Saving only enabled if read-only state is false and is creator
        if (selectedScope == MarkerScope.Shared && marker.IsReadOnly)
            canSave = isMarkerCreator && editingReadOnly != marker.IsReadOnly;

        ImGui.Text("Edit Marker");
        ImGui.Separator();

        IconEditFields.DrawNameField(identifier, ref editingName, !canEditGeneralFields);

        var templates = plugin.MarkerStorageService.GetTemplates();
        editingTemplateId = IconEditFields.DrawTemplatePicker(identifier, editingTemplateId, plugin.Configuration, templates, !canEditGeneralFields);

        var isTemplateAssigned = editingTemplateId != null;
        var disableTemplateFields = !canEditGeneralFields || isTemplateAssigned;

        // When a template is assigned, we disable the individual fields. 
        // The map renderer will read from the template instead of these local fields via GetEffective methods.

        iconEditFields.Draw(identifier, marker.Name, disableTemplateFields);

        // Group assignment dropdown
        var groups = plugin.MarkerStorageService.GetVisibleGroups();
        editingGroupId = IconEditFields.DrawGroupPicker(identifier, editingGroupId, groups, currentHash, disableTemplateFields);

        IconEditFields.DrawNotesField(identifier, ref editingNote, !canEditGeneralFields);

        // Scope dropdown
        var scopeTooltip = isGrouped
            ? "Marker scope is inherited from its group."
            : !isMarkerCreator
                ? "Only the creator can change scope."
                : editingReadOnly && selectedScope == MarkerScope.Shared
                    ? "Disable read-only before changing scope."
                    : "Sets the visibility of the marker to other characters on the same PC.\nPersonal markers are only visible to you, while shared markers are visible to any character that logs in to FFXIV from this PC.";
        editingScope = IconEditFields.DrawScopePicker(identifier, editingScope, !canEditScope || isTemplateAssigned, scopeTooltip);

        using (ImRaii.Disabled(disableTemplateFields))
            ImGui.Checkbox($"Visible Crossworld###AllWorlds{identifier}", ref editingAppliesToAllWorlds);
        if (ImWuk.IsItemHoveredWhenDisabled())
            ImGui.SetTooltip("When enabled, this marker appears on matching maps in all worlds/data centers.");

        // Read-only checkbox (only for shared markers and only editable by the creator)
        if (selectedScope == MarkerScope.Shared)
        {
            ImGui.Spacing();
            using (ImRaii.Disabled(!canEditReadOnly))
            {
                ImGui.Checkbox("Read-Only###MarkerReadOnly", ref editingReadOnly);
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = !isMarkerCreator ?
                    "Only the creator can set this marker to read-only." :
                    editingName.IsNullOrEmpty() ? "Markers must have a name before they can be set to read-only." :
                    "When enabled, all fields are locked and marker deletion is blocked. Only the creator can disable read-only.";
                ImGui.SetTooltip(tooltip);
            }
        }

        ImGui.Spacing();

        using (ImRaii.Disabled(!canSave))
            if (ImGui.Button("Save###EditMarkerSaveButton"))
            {
                var result = new MarkerEditResult
                {
                    Name = editingName,
                    Notes = editingNote,
                    GroupId = editingGroupId,
                    TemplateId = editingTemplateId,
                    Scope = isGrouped ? parentGroup!.Scope : editingScope,
                    IsReadOnly = selectedScope == MarkerScope.Shared && editingReadOnly,
                    AppliesToAllWorlds = editingAppliesToAllWorlds,
                    Icon = iconEditFields.ToMarkerIcon()
                };
                OnSave?.Invoke(marker, result);
                ImGui.CloseCurrentPopup();
            }

        ImGui.SameLine();

        if (ImGui.Button("Cancel###EditMarkerCancel"))
        {
            ImGui.CloseCurrentPopup();
        }
    }
}

/// <summary>
/// Represents the edited values from a marker edit session.
/// </summary>
public sealed class MarkerEditResult : IEditableMarkerResult
{
    public string Name { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public Guid? GroupId { get; init; }
    public Guid? TemplateId { get; init; }
    public required MarkerIcon Icon { get; init; }
    public MarkerScope Scope { get; init; }
    public bool IsReadOnly { get; init; }
    public bool AppliesToAllWorlds { get; init; }
}
