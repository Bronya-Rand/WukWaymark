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

    public void Draw(Waymark waymark)
    {
        var identifier = waymark.Id.ToString();
        using var editWaymarkPopup = ImRaii.Popup($"EditWaymark##{identifier}");

        if (waymark.IsReadOnly && waymark.CharacterHash != plugin.WaymarkStorageService.CurrentCharacterHash)
        {
            if (editWaymarkPopup)
            {
                ImGui.Text($"'{waymark.Name}' is read-only and cannot be edited.");
                ImGui.Spacing();
                if (ImGui.Button("Close"))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
            return;
        }

        if (!editWaymarkPopup) return;

        var isCreator = waymark.CharacterHash == plugin.WaymarkStorageService.CurrentCharacterHash;
        var canEdit = (waymark.Scope == WaymarkScope.Shared && (!waymark.IsReadOnly || isCreator)) ||
            (waymark.Scope == WaymarkScope.Personal && isCreator && !editingName.IsNullOrEmpty());

        ImGui.Text("Edit Waymark");
        ImGui.Separator();

        ImGui.Text("Name:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(editingReadOnly))
            ImGui.InputText($"###Name{identifier}", ref editingName, 100);

        ImGui.Text("Color:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(editingReadOnly))
            ImGui.ColorEdit4($"###Color{identifier}", ref editingColor);

        ImGui.Text("Shape:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        var shapeDropPreview = Enum.GetName(editingShape) ?? "Unknown";
        using (ImRaii.Disabled(editingReadOnly))
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
        using (ImRaii.Disabled(editingReadOnly))
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

        ImGui.Text("Note:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(editingReadOnly))
            ImGui.InputText($"###Note{identifier}", ref editingNote, 100);

        // Scope dropdown
        ImGui.Text("Scope:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        var scopeDropPreview = Enum.GetName(editingScope) ?? "Unknown";
        using (ImRaii.Disabled(editingReadOnly))
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

        // Read-only checkbox (only for shared waymarks and only editable by the creator)
        if (editingScope == WaymarkScope.Shared)
        {
            ImGui.Spacing();
            using (ImRaii.Disabled(waymark.CharacterHash != plugin.WaymarkStorageService.CurrentCharacterHash || editingName.IsNullOrEmpty()))
            {
                ImGui.Checkbox("Read-Only###WaymarkReadOnly", ref editingReadOnly);
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = waymark.CharacterHash != plugin.WaymarkStorageService.CurrentCharacterHash ?
                    "Only the creator can set this waymark to read-only." :
                    editingName.IsNullOrEmpty() ? "Waymarks must have a name before they can be set to read-only." :
                    "When enabled, only you can edit this shared waymark. Prevents deletion until disabled.";
                ImGui.SetTooltip(tooltip);
            }
        }

        // Visibility radius slider
        ImGui.Text("Visibility Radius:");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
        using (ImRaii.Disabled(editingReadOnly))
            ImGui.SliderFloat($"###VisRadius{identifier}", ref editingVisibilityRadius, 0f, 500f, editingVisibilityRadius == 0 ? "Always Visible" : "%.0f yalms");

        // Icon picker
        ImGui.Text("Icon (Overrides Shape):");
        ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);

        using (ImRaii.Disabled(editingReadOnly))
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

        iconPickerModal.Draw(waymark.Name, identifier);

        ImGui.Spacing();

        using (ImRaii.Disabled(!canEdit))
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
                    Scope = editingScope,
                    IsReadOnly = editingReadOnly,
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
