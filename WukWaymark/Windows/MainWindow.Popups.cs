using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukWaymark.Models;
using WukWaymark.Services;

namespace WukWaymark.Windows;

public partial class MainWindow
{
    private readonly (string Id, string Name)[] iconCategories = [
        ("Map", "Map Symbols"), ("Quest", "Quest Markers"), ("Item", "Items"), ("Action", "Actions"), ("Status", "Status Effects"),
        ("Macro", "Macros"), ("Emote", "Emotes"), ("Perform", "Performance"), ("General", "General"),
        ("Main", "Main Commands"), ("Extra", "Extra")
    ];
    private void DrawEditPopup(Waymark waymark)
    {
        var identifier = waymark.Id.ToString();

        if (waymark.IsReadOnly && waymark.CharacterHash != plugin.WaymarkStorageService.CurrentCharacterHash)
        {
            // Show read-only notice if this waymark is shared and read-only
            if (ImGui.BeginPopup($"EditWaymark##{identifier}"))
            {
                ImGui.Text($"'{waymark.Name}' is read-only and cannot be edited.");
                ImGui.Spacing();
                if (ImGui.Button("Close"))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            return;
        }

        if (ImGui.BeginPopup($"EditWaymark##{identifier}"))
        {
            ImGui.Text($"Edit Waymark");
            ImGui.Separator();

            ImGui.Text("Name:");
            ImGui.SetNextItemWidth(250);
            ImGui.InputText($"##Name{identifier}", ref editingName, 100);

            ImGui.Text("Color:");
            ImGui.SetNextItemWidth(250);
            ImGui.ColorEdit4($"##Color{identifier}", ref editingColor);

            ImGui.Text("Shape:");
            ImGui.SetNextItemWidth(250);
            var shapeDropPreview = Enum.GetName(editingShape) ?? "Unknown";
            using (var shapeDrop = ImRaii.Combo($"##Shape{identifier}", shapeDropPreview))
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

            // Group assignment dropdown
            ImGui.Text("Group:");
            ImGui.SetNextItemWidth(250);
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
            using (var groupDrop = ImRaii.Combo($"##Group{identifier}", currentGroupName))
            {
                if (groupDrop.Success)
                {
                    // "Ungrouped" option
                    if (ImGui.Selectable("Ungrouped", editingGroupId == null))
                    {
                        editingGroupId = null;
                    }

                    // Group options
                    foreach (var group in availableGroups)
                    {
                        if (ImGui.Selectable(group.Name, editingGroupId == group.Id))
                        {
                            editingGroupId = group.Id;
                        }
                    }
                }
            }

            ImGui.Text("Note:");
            ImGui.SetNextItemWidth(250);
            ImGui.InputText($"##Note{identifier}", ref editingNote, 100);

            // Scope dropdown
            ImGui.Text("Scope:");
            ImGui.SetNextItemWidth(250);
            var scopeDropPreview = Enum.GetName(editingScope) ?? "Unknown";
            using (var scopeDrop = ImRaii.Combo($"##Scope{identifier}", scopeDropPreview))
            {
                if (scopeDrop.Success)
                {
                    if (ImGui.Selectable(WaymarkScope.Personal.ToString(), editingScope == WaymarkScope.Personal))
                        editingScope = WaymarkScope.Personal;
                    if (ImGui.Selectable(WaymarkScope.Shared.ToString(), editingScope == WaymarkScope.Shared))
                        editingScope = WaymarkScope.Shared;
                }
            }

            // Read-only checkbox (only shown for shared waymarks)
            if (editingScope == WaymarkScope.Shared)
            {
                ImGui.Spacing();
                using (ImRaii.Disabled(waymark.CharacterHash != plugin.WaymarkStorageService.CurrentCharacterHash))
                {
                    if (ImGui.Checkbox("Read-Only##WaymarkReadOnly", ref editingReadOnly))
                        waymark.IsReadOnly = editingReadOnly;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("When enabled, only you can edit this shared waymark. Prevents deletion until disabled.");
                }
            }

            // Visibility radius slider
            ImGui.Text("Visibility Radius:");
            ImGui.SetNextItemWidth(250);
            ImGui.SliderFloat($"##VisRadius{identifier}", ref editingVisibilityRadius, 0f, 500f, editingVisibilityRadius == 0 ? "Always Visible" : "%.0f yalms");

            // Icon picker
            ImGui.Text("Icon (Overrides Shape):");
            ImGui.SetNextItemWidth(250);

            var currentIconName = "Select Icon...";
            var previewTex = editingIconId.HasValue && editingIconId.Value > 0 ? Plugin.TextureProvider.GetFromGameIcon(editingIconId.Value).GetWrapOrEmpty() : null;
            if (editingIconId.HasValue && editingIconId.Value > 0)
                currentIconName = plugin.IconBrowserService.AvailableIcons.FirstOrDefault(i => i.IconId == editingIconId.Value)?.Name ?? $"ID: {editingIconId.Value}";

            // Draw a preview image next to the combo if an icon is selected
            if (previewTex != null && previewTex.Handle != nint.Zero)
            {
                ImGui.Image(previewTex.Handle, new Vector2(24, 24));
                ImGui.SameLine();
                // Adjust Y to center the combo box with the image
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
            }

            ImGui.SetNextItemWidth(previewTex != null ? 218 : 250);
            if (ImGui.Button($"{currentIconName}##IconBtn{identifier}", new Vector2(previewTex != null ? 218 : 250, 0)))
            {
                showIconPickerModal = true;
                ImGui.OpenPopup($"Icon Picker ({waymark.Name})###{identifier}");
            }

            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);

            if (ImGui.BeginPopupModal($"Icon Picker ({waymark.Name})###{identifier}", ref showIconPickerModal, ImGuiWindowFlags.NoSavedSettings))
            {
                if (!plugin.IconBrowserService.IsLoaded)
                {
                    ImGui.TextDisabled("Loading game icons...");
                }
                else
                {
                    // Search box
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint("##IconSearch", "Search by name or ID...", ref editingIconSearch, 50);

                    if (ImGui.Button("None (Use Shape)"))
                    {
                        editingIconId = null;
                        showIconPickerModal = false;
                        ImGui.CloseCurrentPopup();
                    }

                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    if (ImGui.BeginTabBar("IconCategoryTabs"))
                    {
                        foreach (var category in iconCategories)
                        {
                            if (ImGui.BeginTabItem(category.Name))
                            {
                                DrawIconGrid(plugin.IconBrowserService.AvailableIcons, category.Id, editingIconSearch);
                                ImGui.EndTabItem();
                            }
                        }
                        ImGui.EndTabBar();
                    }
                }
                ImGui.EndPopup();
            }

            ImGui.Spacing();

            if (ImGui.Button("Save"))
            {
                var oldScope = waymark.Scope;
                waymark.Name = editingName;
                waymark.Color = editingColor;
                waymark.Shape = editingShape;
                waymark.Notes = editingNote;
                waymark.GroupId = editingGroupId;
                waymark.VisibilityRadius = editingVisibilityRadius;
                waymark.IconId = editingIconId;
                waymark.Scope = editingScope;
                waymark.IsReadOnly = editingReadOnly;

                if (oldScope != waymark.Scope)
                {
                    // Move between lists if scope changed
                    if (waymark.Scope == WaymarkScope.Personal)
                    {
                        plugin.WaymarkStorageService.SharedWaymarks.Remove(waymark);
                        waymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        waymark.IsReadOnly = false;
                        plugin.WaymarkStorageService.PersonalWaymarks.Add(waymark);
                    }
                    else
                    {
                        plugin.WaymarkStorageService.PersonalWaymarks.Remove(waymark);
                        waymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        plugin.WaymarkStorageService.SharedWaymarks.Add(waymark);
                    }
                }

                plugin.WaymarkStorageService.SavePersonalWaymarks();
                plugin.WaymarkStorageService.SaveSharedWaymarks();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawIconGrid(IEnumerable<IconInfo> allIcons, string category, string searchStr)
    {
        var searchLower = searchStr.ToLowerInvariant();
        var query = allIcons
            .Where(i => i.Source == category)
            .Where(i => string.IsNullOrEmpty(searchLower) ||
                        i.Name.ToLowerInvariant().Contains(searchLower) ||
                        i.IconId.ToString().Contains(searchLower));

        var totalCount = query.Count();
        var filteredIcons = query.Take(200).ToList();

        if (filteredIcons.Count == 0)
        {
            ImGui.TextDisabled("No matching icons found.");
            return;
        }

        if (totalCount > 200)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"Showing 200 of {totalCount} icons. Please refine your search.");
            ImGui.Spacing();
        }

        var childVisible = ImGui.BeginChild($"IconGrid_{category}", new Vector2(0, -1), true); // -1 stretch to bottom
        if (childVisible)
        {
            var contentRegion = ImGui.GetContentRegionAvail().X;
            var iconSize = 40f * ImGuiHelpers.GlobalScale;
            var padding = ImGui.GetStyle().ItemSpacing.X;
            var columns = Math.Max(1, (int)(contentRegion / (iconSize + padding)));

            if (ImGui.BeginTable($"IconTable_{category}", columns))
            {
                foreach (var icon in filteredIcons)
                {
                    ImGui.TableNextColumn();

                    // Some icons seem to be missing. Handle gracefully if such happens.
                    IDalamudTextureWrap? tex;
                    try
                    {
                        tex = Plugin.TextureProvider.GetFromGameIcon(icon.IconId).GetWrapOrEmpty();
                    }
                    catch (IconNotFoundException)
                    {
                        Plugin.Log.Warning($"Icon ID {icon.IconId} not found in game resources.");
                        continue;
                    }

                    if (tex != null && tex.Handle != nint.Zero)
                    {
                        ImGui.PushID($"IconBtn_{icon.IconId}");
                        if (ImGui.ImageButton(tex.Handle, new Vector2(iconSize, iconSize)))
                        {
                            editingIconId = icon.IconId;
                            showIconPickerModal = false;
                            ImGui.CloseCurrentPopup();
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.TextUnformatted(icon.Name);
                            ImGui.TextDisabled($"ID: {icon.IconId}");
                            ImGui.EndTooltip();
                        }
                        ImGui.PopID();
                    }
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private void DrawCreateGroupPopup()
    {
        if (showCreateGroupPopup)
        {
            ImGui.OpenPopup("Group Editor##WWGroupEditorModal");
            showCreateGroupPopup = false;
            groupEditorOpen = true;
        }

        if (!groupEditorOpen) return;
        if (editingGroup != null && editingGroup.IsReadOnly && editingGroup.CreatorHash != plugin.WaymarkStorageService.CurrentCharacterHash)
        {
            Plugin.Log.Warning("Attempted to edit a read-only group. Action blocked.");
            editingGroup = null;
            groupEditorOpen = false;
            return;
        }

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Group Editor##WWGroupEditorModal", ref groupEditorOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var isEditing = editingGroup != null;
            ImGui.Text(isEditing ? "Edit Group" : "Create New Group");
            ImGui.Separator();

            ImGui.Text("Group Name:");
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("##GroupName", ref groupEditName, 100);

            ImGui.Spacing();

            // Scope dropdown
            ImGui.Text("Scope:");
            ImGui.SetNextItemWidth(250);
            var scopeDropPreview = Enum.GetName(groupEditScope) ?? "Unknown";
            using (var scopeDrop = ImRaii.Combo("##GroupScope", scopeDropPreview))
            {
                if (scopeDrop.Success)
                {
                    if (ImGui.Selectable(WaymarkScope.Personal.ToString(), groupEditScope == WaymarkScope.Personal))
                        groupEditScope = WaymarkScope.Personal;
                    if (ImGui.Selectable(WaymarkScope.Shared.ToString(), groupEditScope == WaymarkScope.Shared))
                        groupEditScope = WaymarkScope.Shared;
                }
            }

            // Read-only checkbox (only shown for shared groups)
            if (groupEditScope == WaymarkScope.Shared)
            {
                ImGui.Spacing();
                ImGui.Checkbox("Read-Only (Prevents deletion)", ref groupEditIsReadOnly);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("When enabled, only you can edit this shared group. Prevents deletion until disabled.");
                }
            }

            ImGui.Spacing();

            if (ImGui.Button("Save"))
            {
                if (!string.IsNullOrWhiteSpace(groupEditName))
                {
                    if (isEditing)
                    {
                        var oldScope = editingGroup!.Scope;
                        editingGroup.Name = groupEditName;
                        editingGroup.Scope = groupEditScope;
                        editingGroup.IsReadOnly = groupEditScope == WaymarkScope.Shared && groupEditIsReadOnly;

                        // If scope changed, move between collections
                        if (oldScope != groupEditScope)
                        {
                            if (groupEditScope == WaymarkScope.Shared)
                            {
                                plugin.WaymarkStorageService.PersonalGroups.Remove(editingGroup);
                                editingGroup.CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                                plugin.WaymarkStorageService.SharedGroups.Add(editingGroup);

                                // Move all child waymarks from Personal to Shared
                                var childWaymarks = plugin.WaymarkStorageService.PersonalWaymarks.Where(w => w.GroupId == editingGroup.Id).ToList();
                                foreach (var w in childWaymarks)
                                {
                                    plugin.WaymarkStorageService.PersonalWaymarks.Remove(w);
                                    w.Scope = WaymarkScope.Shared;
                                    w.IsReadOnly = editingGroup.IsReadOnly; // Inherit read-only status from group
                                    w.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                                    plugin.WaymarkStorageService.SharedWaymarks.Add(w);
                                }
                            }
                            else
                            {
                                plugin.WaymarkStorageService.SharedGroups.Remove(editingGroup);
                                editingGroup.CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                                plugin.WaymarkStorageService.PersonalGroups.Add(editingGroup);

                                // Move all child waymarks from Shared to Personal
                                var childWaymarks = plugin.WaymarkStorageService.SharedWaymarks.Where(w => w.GroupId == editingGroup.Id).ToList();
                                foreach (var w in childWaymarks)
                                {
                                    plugin.WaymarkStorageService.SharedWaymarks.Remove(w);
                                    w.Scope = WaymarkScope.Personal;
                                    w.IsReadOnly = false;
                                    w.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                                    plugin.WaymarkStorageService.PersonalWaymarks.Add(w);
                                }
                            }
                        }
                    }
                    else
                    {
                        var newGroup = new WaymarkGroup
                        {
                            Name = groupEditName,
                            CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash,
                            IsReadOnly = groupEditScope == WaymarkScope.Shared && groupEditIsReadOnly,
                            Scope = groupEditScope
                        };

                        if (groupEditScope == WaymarkScope.Shared)
                            plugin.WaymarkStorageService.SharedGroups.Add(newGroup);
                        else
                            plugin.WaymarkStorageService.PersonalGroups.Add(newGroup);
                    }
                    plugin.WaymarkStorageService.SavePersonalWaymarks();
                    plugin.WaymarkStorageService.SaveSharedWaymarks();
                    editingGroup = null;
                    groupEditorOpen = false;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel"))
            {
                editingGroup = null;
                groupEditorOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
