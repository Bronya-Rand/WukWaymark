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

internal class WaymarkEditPopup
{
    private readonly Plugin plugin;
    private readonly IconPickerModal iconPickerModal;

    #region Editing State

    private string editingName = string.Empty;
    private Vector4 editingColor = Vector4.One;
    private string editingNote = string.Empty;
    private WaymarkShape editingShape = WaymarkShape.Circle;
    private Guid? editingGroupId;
    private float editingVisibilityRadius;
    private uint? editingIconId = null;
    private WaymarkScope editingScope = WaymarkScope.Personal;
    private bool editingReadOnly = false;

    #endregion

    public Action<Waymark, WaymarkEditResult>? OnSave { get; set; }

    public WaymarkEditPopup(Plugin plugin)
    {
        this.plugin = plugin;
        iconPickerModal = new IconPickerModal(plugin)
        {
            OnIconSelected = iconId => editingIconId = iconId
        };
    }

    /// <summary>
    /// Loads editing state from the given waymark.
    /// </summary>
    /// <remarks>Call this before opening the popup.</remarks>
    public void LoadFromWaymark(Waymark waymark)
    {
        editingName = waymark.Name;
        editingColor = waymark.Color;
        editingShape = waymark.Shape;
        editingNote = waymark.Notes;
        editingGroupId = waymark.GroupId;
        editingVisibilityRadius = waymark.VisibilityRadius;
        editingIconId = waymark.IconId;
        editingScope = waymark.Scope;
        editingReadOnly = waymark.IsReadOnly;
    }

    public void Draw(Waymark waymark, WaymarkGroup? parentGroup = null)
    {
        var identifier = waymark.Id.ToString();
        using var editWaymarkPopup = ImRaii.Popup($"EditWaymark##{identifier}");

        if (!editWaymarkPopup) return;

        var currentCharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
        var isWaymarkCreator = waymark.CharacterHash != null &&
                               currentCharacterHash != null &&
                               waymark.CharacterHash == currentCharacterHash;

        // Inherit scope from group if waymark is in a group
        var isGrouped = parentGroup != null;
        var effectiveScope = isGrouped ? parentGroup!.Scope : waymark.Scope;

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
                                 currentCharacterHash != null &&
                                 parentGroup.CreatorHash == currentCharacterHash;

            if (parentGroup.Scope == WaymarkScope.Personal)
            {
                canOpenEdit = isGroupCreator;
                if (!canOpenEdit)
                    cannotEditReason = "Only the group's creator can edit waymarks in this personal group.";
            }
            else
            {
                if (parentGroup.IsReadOnly)
                {
                    canOpenEdit = isWaymarkCreator;
                    if (!canOpenEdit)
                        cannotEditReason = "This group is read-only and only the waymark creator can edit it.";
                }
                else
                {
                    canOpenEdit = !waymark.IsReadOnly || isWaymarkCreator;
                    if (!canOpenEdit)
                        cannotEditReason = $"'{waymark.Name}' is read-only and cannot be edited by non-creators";
                }
            }
        }
        else
        {
            // Validate individual waymark permissions
            canOpenEdit = (effectiveScope == WaymarkScope.Personal && isWaymarkCreator) ||
                          (effectiveScope == WaymarkScope.Shared && (!waymark.IsReadOnly || isWaymarkCreator));

            if (!canOpenEdit)
            {
                cannotEditReason = effectiveScope == WaymarkScope.Personal
                    ? $"Only the creator can edit '{waymark.Name}'."
                    : $"'{waymark.Name}' is read-only and cannot be edited.";
            }
        }

        // Exit early if user lacks permissions
        if (!canOpenEdit)
        {
            Plugin.Log.Warning($"Edit popup opened without permission for waymark '{waymark.Id}'. {cannotEditReason}.");
            ImGui.CloseCurrentPopup();
            return;
        }

        var inheritedReadOnly = isGrouped && parentGroup!.Scope == WaymarkScope.Shared && parentGroup.IsReadOnly;
        var isSharedReadOnly = selectedScope == WaymarkScope.Shared && (editingReadOnly || inheritedReadOnly);
        var canEditGeneralFields = !isSharedReadOnly;
        var canEditScope = !isGrouped && isWaymarkCreator && !editingReadOnly;
        var canEditReadOnly = selectedScope == WaymarkScope.Shared && isWaymarkCreator && !editingName.IsNullOrEmpty();

        var canSave = !editingName.IsNullOrEmpty();
        if (selectedScope == WaymarkScope.Shared && waymark.IsReadOnly && editingReadOnly)
        {
            canSave = isWaymarkCreator && editingReadOnly != waymark.IsReadOnly;
        }

        ImGui.Text("Edit Waymark");
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
            var tooltip = "Sets the color of the waymark on the minimap/map. This is overridden by the icon if one is selected.";
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
                    var shapeNames = Enum.GetNames<WaymarkShape>();
                    foreach (var shapeName in shapeNames)
                    {
                        if (ImGui.Selectable(shapeName, shapeName == shapeDropPreview))
                        {
                            editingShape = Enum.Parse<WaymarkShape>(shapeName);
                        }
                    }
                }
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Assigns a shape to the waymark. This is overridden by the icon if one is selected.";
            ImGui.SetTooltip(tooltip);
        }

        // Group assignment dropdown
        ImGui.Text("Group:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        var groups = plugin.WaymarkStorageService.GetVisibleGroups();
        var currentHash = plugin.WaymarkStorageService.CurrentCharacterHash;
        var availableGroups = groups.Where(g =>
            g.Id == editingGroupId ||
            g.Scope == WaymarkScope.Personal ||
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
            var tooltip = "Assigns a group to this waymark. Waymarks in a group inherit the group's scope and read-only status.\nOnly personal groups and shared, non-read-only groups are available for assignment.";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.Text("Note:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(!canEditGeneralFields))
            ImGui.InputText($"###Note{identifier}", ref editingNote, 100);
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Additional notes for this waymark.";
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
                    if (ImGui.Selectable(WaymarkScope.Personal.ToString(), editingScope == WaymarkScope.Personal))
                        editingScope = WaymarkScope.Personal;
                    if (ImGui.Selectable(WaymarkScope.Shared.ToString(), editingScope == WaymarkScope.Shared))
                        editingScope = WaymarkScope.Shared;
                }
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = isGrouped
                ? "Waymark scope is inherited from its group."
                : !isWaymarkCreator
                    ? "Only the creator can change scope."
                    : editingReadOnly && selectedScope == WaymarkScope.Shared
                        ? "Disable read-only before changing scope."
                        : "Sets the visibility of the waymark to other players on the same PC.\nPersonal waymarks are only visible to you, while shared waymarks are visible to anyone who logs in to XIV from this PC";
            ImGui.SetTooltip(tooltip);
        }

        // Read-only checkbox (only for shared waymarks and only editable by the creator)
        if (selectedScope == WaymarkScope.Shared)
        {
            ImGui.Spacing();
            using (ImRaii.Disabled(!canEditReadOnly))
            {
                ImGui.Checkbox("Read-Only###WaymarkReadOnly", ref editingReadOnly);
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = !isWaymarkCreator ?
                    "Only the creator can set this waymark to read-only." :
                    editingName.IsNullOrEmpty() ? "Waymarks must have a name before they can be set to read-only." :
                    "When enabled, all fields are locked and waymark deletion is blocked. Only the creator can disable read-only.";
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
            var tooltip = "Set how far this waymark is visible on the minimap/map before it de-renders. Set to 0 for always visible.";
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
                iconPickerModal.OpenPopup(waymark.Name, identifier);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = "Assigns an in-game icon to the waymark. This overrides the color and shape settings of the waymark.";
            ImGui.SetTooltip(tooltip);
        }

        iconPickerModal.Draw(waymark.Name, identifier);

        ImGui.Spacing();

        using (ImRaii.Disabled(!canSave))
            if (ImGui.Button("Save###EditWaymarkSaveButton"))
            {
                var result = new WaymarkEditResult
                {
                    Name = editingName,
                    Color = editingColor,
                    Shape = editingShape,
                    Notes = editingNote,
                    GroupId = editingGroupId,
                    VisibilityRadius = editingVisibilityRadius,
                    IconId = editingIconId,
                    Scope = isGrouped ? parentGroup!.Scope : editingScope,
                    IsReadOnly = selectedScope == WaymarkScope.Shared ? editingReadOnly : false,
                };
                OnSave?.Invoke(waymark, result);
                ImGui.CloseCurrentPopup();
            }

        ImGui.SameLine();

        if (ImGui.Button("Cancel###EditWaymarkCancel"))
        {
            ImGui.CloseCurrentPopup();
        }
    }
}

/// <summary>
/// Represents the edited values from a waymark edit session.
/// </summary>
public class WaymarkEditResult
{
    public string Name { get; init; } = string.Empty;
    public Vector4 Color { get; init; } = Vector4.One;
    public WaymarkShape Shape { get; init; }
    public string Notes { get; init; } = string.Empty;
    public Guid? GroupId { get; init; }
    public float VisibilityRadius { get; init; }
    public uint? IconId { get; init; }
    public WaymarkScope Scope { get; init; }
    public bool IsReadOnly { get; init; }
}
