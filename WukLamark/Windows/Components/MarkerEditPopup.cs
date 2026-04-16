using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System;
using System.Linq;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Components;

internal class MarkerEditPopup
{
    private readonly Plugin plugin;
    private readonly IconPickerModal iconPickerModal;

    #region Editing State

    private string editingName = string.Empty;
    private Vector4 editingColor = Vector4.One;
    private string editingNote = string.Empty;
    private MarkerShape editingShape = MarkerShape.Circle;
    private Guid? editingGroupId;
    private float editingVisibilityRadius;
    private uint? editingIconId = null;
    private float editingIconSize = 0.0f;
    private MarkerScope editingScope = MarkerScope.Personal;
    private bool editingReadOnly = false;
    private bool editingAppliesToAllWorlds = false;

    #endregion

    public Action<Marker, MarkerEditResult>? OnSave { get; set; }

    public MarkerEditPopup(Plugin plugin)
    {
        this.plugin = plugin;
        iconPickerModal = new IconPickerModal(plugin)
        {
            OnIconSelected = iconId => editingIconId = iconId
        };
    }

    /// <summary>
    /// Loads editing state from the given marker.
    /// </summary>
    /// <remarks>Call this before opening the popup.</remarks>
    public void LoadFromMarker(Marker marker)
    {
        editingName = marker.Name;
        editingColor = marker.Color;
        editingShape = marker.Shape;
        editingNote = marker.Notes;
        editingGroupId = marker.GroupId;
        editingVisibilityRadius = marker.VisibilityRadius;
        editingIconId = marker.IconId;
        editingIconSize = marker.IconSize ?? plugin.Configuration.WaymarkMarkerSize;
        editingScope = marker.Scope;
        editingReadOnly = marker.IsReadOnly;
        editingAppliesToAllWorlds = marker.AppliesToAllWorlds;
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

        ImGui.Text("Name:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(!canEditGeneralFields))
            ImGui.InputText($"###Name{identifier}", ref editingName, 100);

        ImGui.Text("Color:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(!canEditGeneralFields))
            ImGui.ColorEdit4($"###Color{identifier}", ref editingColor);
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Sets the color of the marker on the minimap/map. This is overridden by the icon if one is selected.";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.Text("Shape:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        var shapeDropPreview = Enum.GetName(editingShape) ?? "Unknown";
        using (ImRaii.Disabled(!canEditGeneralFields))
        {
            using (var shapeDrop = ImRaii.Combo($"###Shape{identifier}", shapeDropPreview))
            {
                if (shapeDrop.Success)
                {
                    var shapeNames = Enum.GetNames<MarkerShape>();
                    foreach (var shapeName in shapeNames)
                    {
                        if (ImGui.Selectable(shapeName, shapeName == shapeDropPreview))
                        {
                            editingShape = Enum.Parse<MarkerShape>(shapeName);
                        }
                    }
                }
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Assigns a shape to the marker. This is overridden by the icon if one is selected.";
            ImGui.SetTooltip(tooltip);
        }

        // Shape/Icon size slider
        ImGui.Text("Shape/Icon Size:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(!canEditGeneralFields))
            ImGui.SliderFloat($"###Size{identifier}", ref editingIconSize, 0f, 24.0f, editingIconSize == 0 ? "Global Icon/Shape Size" : "%.1f");

        // Group assignment dropdown
        ImGui.Text("Group:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        var groups = plugin.MarkerStorageService.GetVisibleGroups();
        var availableGroups = groups.Where(g =>
            g.Id == editingGroupId ||
            g.Scope == MarkerScope.Personal ||
            !g.IsReadOnly ||
            (g.CreatorHash != null && currentHash != null && g.CreatorHash == currentHash)
        ).ToList();


        var currentGroupName = editingGroupId == null
            ? "Ungrouped"
            : groups.FirstOrDefault(g => g.Id == editingGroupId)?.Name ?? "Unknown";
        using (ImRaii.Disabled(!canEditGeneralFields))
        {
            using (var groupDrop = ImRaii.Combo($"###Group{identifier}", currentGroupName))
            {
                if (groupDrop.Success)
                {
                    if (ImGui.Selectable("Ungrouped", editingGroupId == null))
                    {
                        editingGroupId = null;
                    }

                    foreach (var group in availableGroups)
                    {
                        if (ImGui.Selectable(group.Name, editingGroupId == group.Id))
                        {
                            editingGroupId = group.Id;
                        }
                    }
                }
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Assigns a group to this marker. Markers in a group inherit the group's scope and read-only status.\nOnly personal groups and shared, non-read-only groups are available for assignment.";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.Text("Note:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(!canEditGeneralFields))
            ImGui.InputText($"###Note{identifier}", ref editingNote, 100);
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Additional notes for this marker.";
            ImGui.SetTooltip(tooltip);
        }

        // Scope dropdown
        ImGui.Text("Scope:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        var scopeDropPreview = Enum.GetName(editingScope) ?? "Unknown";
        using (ImRaii.Disabled(!canEditScope))
        {
            using (var scopeDrop = ImRaii.Combo($"###Scope{identifier}", scopeDropPreview))
            {
                if (scopeDrop.Success)
                {
                    if (ImGui.Selectable(MarkerScope.Personal.ToString(), editingScope == MarkerScope.Personal))
                        editingScope = MarkerScope.Personal;
                    if (ImGui.Selectable(MarkerScope.Shared.ToString(), editingScope == MarkerScope.Shared))
                        editingScope = MarkerScope.Shared;
                }
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = isGrouped
                ? "Marker scope is inherited from its group."
                : !isMarkerCreator
                    ? "Only the creator can change scope."
                    : editingReadOnly && selectedScope == MarkerScope.Shared
                        ? "Disable read-only before changing scope."
                        : "Sets the visibility of the marker to other characters on the same PC.\nPersonal markers are only visible to you, while shared markers are visible to any character that logs in to FFXIV from this PC.";
            ImGui.SetTooltip(tooltip);
        }

        using (ImRaii.Disabled(!canEditGeneralFields))
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

        // Visibility radius slider
        ImGui.Text("Visibility Radius:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(!canEditGeneralFields))
            ImGui.SliderFloat($"###VisRadius{identifier}", ref editingVisibilityRadius, 0f, 500f, editingVisibilityRadius == 0 ? "Always Visible" : "%.0f yalms");
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Set how far this marker is visible on the minimap/map before it de-renders. Set to 0 for always visible.";
            ImGui.SetTooltip(tooltip);
        }

        // Icon picker
        ImGui.Text("Icon (Overrides Shape):");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);

        using (ImRaii.Disabled(!canEditGeneralFields))
        {
            var currentIconName = "Select Icon...";
            var previewTex = editingIconId.HasValue && editingIconId.Value > 0 ? Plugin.TextureProvider.GetFromGameIcon(editingIconId.Value).GetWrapOrEmpty() : null;
            if (editingIconId.HasValue && editingIconId.Value > 0)
                currentIconName = plugin.IconBrowserService.AvailableIcons.FirstOrDefault(i => i.IconId == editingIconId.Value)?.Name ?? $"ID: {editingIconId.Value}";

            if (previewTex != null && previewTex.Handle != nint.Zero)
            {
                ImGui.Image(previewTex.Handle, new Vector2(24, 24));
                ImGui.SameLine();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
            }

            ImGui.SetNextItemWidth((previewTex != null ? 218 : 250) * ImGuiHelpers.GlobalScale);
            if (ImGui.Button($"{currentIconName}###IconBtn{identifier}", new Vector2((previewTex != null ? 218 : 250) * ImGuiHelpers.GlobalScale, 0)))
            {
                iconPickerModal.OpenPopup(marker.Name, identifier);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Assigns an in-game icon to the marker. This overrides the color and shape settings of the marker.";
            ImGui.SetTooltip(tooltip);
        }

        iconPickerModal.Draw(marker.Name, identifier);

        ImGui.Spacing();

        using (ImRaii.Disabled(!canSave))
            if (ImGui.Button("Save###EditMarkerSaveButton"))
            {
                var result = new MarkerEditResult
                {
                    Name = editingName,
                    Color = editingColor,
                    Shape = editingShape,
                    Notes = editingNote,
                    GroupId = editingGroupId,
                    VisibilityRadius = editingVisibilityRadius,
                    IconId = editingIconId,
                    IconSize = editingIconSize,
                    Scope = isGrouped ? parentGroup!.Scope : editingScope,
                    IsReadOnly = selectedScope == MarkerScope.Shared && editingReadOnly,
                    AppliesToAllWorlds = editingAppliesToAllWorlds,
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
public class MarkerEditResult
{
    public string Name { get; init; } = string.Empty;
    public Vector4 Color { get; init; } = Vector4.One;
    public MarkerShape Shape { get; init; }
    public string Notes { get; init; } = string.Empty;
    public Guid? GroupId { get; init; }
    public float VisibilityRadius { get; init; }
    public uint? IconId { get; init; }
    public float IconSize { get; init; }
    public MarkerScope Scope { get; init; }
    public bool IsReadOnly { get; init; }
    public bool AppliesToAllWorlds { get; init; }
}
