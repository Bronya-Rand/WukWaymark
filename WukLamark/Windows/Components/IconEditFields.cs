using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Components
{
    /// <summary>
    /// Provides a container for marker icon editing fields and related UI logic, supporting the configuration and
    /// display of marker icon properties within the editor.
    /// </summary>
    /// <remarks>
    /// This class encapsulates the state and user interface interactions for editing marker icon
    /// properties, such as color, shape, icon source, and visibility radius.
    /// </remarks>
    internal sealed class IconEditFields
    {
        public Vector4 Color;
        public MarkerIconType IconSourceType;
        public MarkerShape Shape;
        public float VisibilityRadius;
        public uint? GameIconId;
        public string? CustomIconName;
        public float IconSize;
        public bool UseShapeColorOnIcon;

        private readonly Plugin plugin;
        private readonly IconPickerModal iconPickerModal;

        public IconEditFields(Plugin plugin)
        {
            this.plugin = plugin;
            iconPickerModal = new IconPickerModal(plugin)
            {
                OnIconSelected = iconDataResult =>
                {
                    if (iconDataResult == null)
                    {
                        IconSourceType = MarkerIconType.Shape;
                        GameIconId = null;
                        CustomIconName = null;
                    }
                    else
                    {
                        IconSourceType = iconDataResult.SourceType;
                        GameIconId = iconDataResult.GameIconId;
                        CustomIconName = iconDataResult.CustomIconName;
                    }
                }
            };
        }

        /// <summary>
        /// Copies the property values from the specified marker icon to the current instance.
        /// </summary>
        /// <param name="icon">The marker icon whose property values are used to update the current instance. Cannot be null.</param>
        public void LoadFrom(MarkerIcon icon)
        {
            Color = icon.Color;
            IconSourceType = icon.SourceType;
            Shape = icon.Shape;
            VisibilityRadius = icon.VisibilityRadius;
            GameIconId = icon.GameIconId;
            CustomIconName = icon.CustomIconName;
            IconSize = icon.Size;
            UseShapeColorOnIcon = icon.UseShapeColor;
        }

        /// <summary>
        /// Creates a new MarkerIcon instance that represents the current configuration of this object.
        /// </summary>
        /// <returns>A MarkerIcon object initialized with the current property values.</returns>
        public MarkerIcon ToMarkerIcon()
        {
            return new MarkerIcon
            {
                Color = Color,
                SourceType = IconSourceType,
                Shape = Shape,
                VisibilityRadius = VisibilityRadius,
                GameIconId = GameIconId,
                CustomIconName = CustomIconName,
                Size = IconSize,
                UseShapeColor = UseShapeColorOnIcon
            };
        }

        public void Draw(string identifier, string entityName, bool disabled = false)
        {
            ImGui.Text("Marker Type:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            using (ImRaii.Disabled(disabled))
            {
                var markerTypeNames = Enum.GetNames<MarkerIconType>();
                var markerTypePreview = Enum.GetName(IconSourceType) ?? "Unknown";
                using (var markerTypeDrop = ImRaii.Combo($"###MarkerType{identifier}", markerTypePreview))
                {
                    if (markerTypeDrop.Success)
                    {
                        foreach (var markerTypeName in markerTypeNames)
                        {
                            if (ImGui.Selectable(markerTypeName, markerTypeName == markerTypePreview))
                            {
                                IconSourceType = Enum.Parse<MarkerIconType>(markerTypeName);
                                switch (IconSourceType)
                                {
                                    case MarkerIconType.Shape:
                                        GameIconId = null;
                                        CustomIconName = null;
                                        break;
                                    case MarkerIconType.Game:
                                        CustomIconName = null;
                                        break;
                                    case MarkerIconType.Custom:
                                        GameIconId = null;
                                        break;
                                }
                            }
                        }
                    }
                }
            }

            ImGui.Text("Shape Color:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            using (ImRaii.Disabled(disabled))
                ImGui.ColorEdit4($"###Color{identifier}", ref Color);
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = "Sets the color of the marker on the minimap/map.\nThis is overridden if using an icon unless 'Apply Shape Color To Icon' is enabled.";
                ImGui.SetTooltip(tooltip);
            }

            ImGui.Text("Shape/Icon Size:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            using (ImRaii.Disabled(disabled))
                ImGui.SliderFloat($"###Size{identifier}", ref IconSize, 0f, 24.0f, IconSize == 0 ? "Global Icon/Shape Size" : "%.1f");

            var shapeText = IconSourceType == MarkerIconType.Shape ? "Shape:" : "Shape (fallback if no icon):";
            ImGui.Text(shapeText);
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            var shapeDropPreview = Enum.GetName(Shape) ?? "Unknown";
            using (ImRaii.Disabled(disabled))
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
                                Shape = Enum.Parse<MarkerShape>(shapeName);
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

            if (IconSourceType != MarkerIconType.Shape)
            {
                ImGui.Text("Icon:");
                ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);

                using (ImRaii.Disabled(disabled))
                {
                    var currentIconName = "Select Icon...";
                    var previewTex = !CustomIconName.IsNullOrWhitespace() ? Plugin.CustomIconService.GetWrapOrEmpty(CustomIconName)
                        : GameIconId.HasValue && GameIconId.Value > 0 ? Plugin.TextureProvider.GetFromGameIcon(GameIconId.Value).GetWrapOrEmpty() : null;

                    if (IconSourceType == MarkerIconType.Custom && !CustomIconName.IsNullOrWhitespace())
                        currentIconName = CustomIconName!;
                    else if (IconSourceType == MarkerIconType.Game && GameIconId.HasValue && GameIconId.Value > 0)
                        currentIconName = plugin.IconBrowserService.AvailableIcons.FirstOrDefault(i => i.IconId == GameIconId.Value)?.Name ?? $"ID: {GameIconId.Value}";

                    if (previewTex != null && previewTex.Handle != nint.Zero)
                    {
                        ImGui.Image(previewTex.Handle, new Vector2(24, 24));
                        ImGui.SameLine();
                        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
                    }

                    ImGui.SetNextItemWidth((previewTex != null ? 218 : 250) * ImGuiHelpers.GlobalScale);
                    if (ImGui.Button($"{currentIconName}###IconBtn{identifier}", new Vector2((previewTex != null ? 218 : 250) * ImGuiHelpers.GlobalScale, 0)))
                    {
                        iconPickerModal.OpenPopup(entityName, identifier);
                    }
                }
                if (ImWuk.IsItemHoveredWhenDisabled())
                {
                    var tooltip = "Assigns an in-game icon to the marker.\nThis overrides the color of the marker unless 'Apply Shape Color To Icon' is enabled.";
                    ImGui.SetTooltip(tooltip);
                }

                iconPickerModal.Draw(entityName, identifier, IconSourceType);

                using (ImRaii.Disabled(disabled))
                    ImGui.Checkbox($"Apply Shape Color To Icon###UseShapeColorOnIcon{identifier}", ref UseShapeColorOnIcon);
                if (ImWuk.IsItemHoveredWhenDisabled())
                    ImGui.SetTooltip("When enabled, the shape color is applied to the icon rendering.");
            }

            ImGui.Text("Visibility Radius:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            using (ImRaii.Disabled(disabled))
                ImGui.SliderFloat($"###VisRadius{identifier}", ref VisibilityRadius, 0f, 500f, VisibilityRadius == 0 ? "Always Visible" : "%.0f yalms");
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = "Set how far this marker is visible on the minimap/map before it de-renders. Set to 0 for always visible.";
                ImGui.SetTooltip(tooltip);
            }
        }

        public static void DrawNameField(string identifier, ref string name, bool disabled = false)
        {
            ImGui.Text("Name:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            using (ImRaii.Disabled(disabled))
                ImGui.InputText($"###Name{identifier}", ref name, 100);
        }

        public static void DrawNotesField(string identifier, ref string notes, bool disabled = false)
        {
            ImGui.Text("Notes:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            using (ImRaii.Disabled(disabled))
                ImGui.InputTextMultiline(
                    $"###Note{identifier}",
                    ref notes,
                    1000,
                    new Vector2(250 * ImGuiHelpers.GlobalScale, 100 * ImGuiHelpers.GlobalScale));
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = "Additional notes for this marker.";
                ImGui.SetTooltip(tooltip);
            }
        }

        public static Guid? DrawGroupPicker(string identifier, Guid? groupId, IReadOnlyList<MarkerGroup> groups, string? currentHash, bool disabled = false)
        {
            ImGui.Text("Group:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);

            var availableGroups = groups.Where(g =>
                g.Id == groupId ||
                g.Scope == MarkerScope.Personal ||
                !g.IsReadOnly ||
                (g.CreatorHash != null && currentHash != null && g.CreatorHash == currentHash))
                .ToList();

            var currentGroupName = groupId == null
                ? "Ungrouped"
                : groups.FirstOrDefault(g => g.Id == groupId)?.Name ?? "Unknown";
            using (ImRaii.Disabled(disabled))
            {
                using (var groupDrop = ImRaii.Combo($"###Group{identifier}", currentGroupName))
                {
                    if (groupDrop.Success)
                    {
                        if (ImGui.Selectable("Ungrouped", groupId == null))
                            groupId = null;

                        foreach (var group in availableGroups)
                        {
                            if (ImGui.Selectable(group.Name, groupId == group.Id))
                                groupId = group.Id;
                        }
                    }
                }
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = "Assigns a group to this marker. Markers in a group inherit the group's scope and read-only status.\nOnly personal groups and shared, non-read-only groups are available for assignment.";
                ImGui.SetTooltip(tooltip);
            }

            return groupId;
        }

        public static MarkerScope DrawScopePicker(string identifier, MarkerScope scope, bool disabled = false, string? tooltip = null)
        {
            ImGui.Text("Scope:");
            ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
            var scopeDropPreview = Enum.GetName(scope) ?? "Unknown";
            using (ImRaii.Disabled(disabled))
            {
                using (var scopeDrop = ImRaii.Combo($"###Scope{identifier}", scopeDropPreview))
                {
                    if (scopeDrop.Success)
                    {
                        if (ImGui.Selectable(MarkerScope.Personal.ToString(), scope == MarkerScope.Personal))
                            scope = MarkerScope.Personal;
                        if (ImGui.Selectable(MarkerScope.Shared.ToString(), scope == MarkerScope.Shared))
                            scope = MarkerScope.Shared;
                    }
                }
            }
            if (ImWuk.IsItemHoveredWhenDisabled() && !tooltip.IsNullOrEmpty())
            {
                ImGui.SetTooltip(tooltip);
            }

            return scope;
        }
    }
}
