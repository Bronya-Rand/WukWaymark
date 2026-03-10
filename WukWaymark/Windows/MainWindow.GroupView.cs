using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukWaymark.Models;

namespace WukWaymark.Windows;

public partial class MainWindow
{
    private void DrawGroupView(IEnumerable<Waymark> waymarks)
    {
        var groups = plugin.WaymarkStorageService.GetVisibleGroups();

        // Draw create group popup if needed
        DrawCreateGroupPopup();
        DrawDeleteGroupConfirmation();

        // "+ New Group" button
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            groupEditName = string.Empty;
            groupEditScope = WaymarkScope.Personal; // Default to personal
            groupEditIsReadOnly = false; // Default to not read-only
            showCreateGroupPopup = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Create New Group");
        }

        ImGui.Spacing();

        foreach (var group in groups)
        {
            var groupWaymarks = FilterWaymarks(waymarks.Where(w => w.GroupId == group.Id));

            // Recompute count once properly instead of .Count which might enumerate early if not a List
            var groupWaymarksList = groupWaymarks.ToList();

            if (groupWaymarksList.Count == 0 && filterCurrentZone) continue; // Skip empty groups that are not relevant to current zone filter

            // If searching and no waymarks match AND group name doesn't match, hide this group
            if (!string.IsNullOrEmpty(searchFilter))
            {
                var groupNameMatches = group.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase);
                if (groupWaymarksList.Count == 0 && !groupNameMatches)
                    continue;
            }

            var identifier = group.Id.ToString();
            using var groupId = ImRaii.PushId(identifier);

            // Group header with collapsing
            var headerOpen = ImGui.CollapsingHeader($"{group.Name} ({groupWaymarksList.Count})###group_{identifier}", ImGuiTreeNodeFlags.AllowItemOverlap);

            // Right-aligned buttons on the header line
            DrawGroupHeaderButtons(group);

            if (headerOpen)
            {
                if (groupWaymarksList.Count == 0)
                {
                    using (ImRaii.PushIndent(10))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No waymarks in this group.");
                    }
                }
                else
                {
                    using var tableId = ImRaii.PushId("GroupTable");
                    DrawWaymarkTable(groupWaymarksList);
                }
                ImGui.Spacing();
            }
        }

        // Ungrouped waymarks section
        var ungroupedWaymarksList = FilterWaymarks(waymarks.Where(w => w.GroupId == null)).ToList();

        if (!string.IsNullOrEmpty(searchFilter) && ungroupedWaymarksList.Count == 0)
            return; // Hide ungrouped section when search yields no results

        if (ungroupedWaymarksList.Count > 0 || string.IsNullOrEmpty(searchFilter))
        {
            var ungroupedOpen = ImGui.CollapsingHeader($"Ungrouped ({ungroupedWaymarksList.Count})###ungrouped", ImGuiTreeNodeFlags.DefaultOpen);
            if (ungroupedOpen)
            {
                if (ungroupedWaymarksList.Count == 0)
                {
                    using (ImRaii.PushIndent(10))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No ungrouped waymarks.");
                    }
                }
                else
                {
                    using var tableId = ImRaii.PushId("GroupTableUngrouped");
                    DrawWaymarkTable(ungroupedWaymarksList);
                }
            }
        }
    }

    private void DrawGroupHeaderButtons(WaymarkGroup group)
    {
        // Calculate right-aligned position for buttons
        var buttonSize = 20.0f * ImGuiHelpers.GlobalScale;
        var spacing = 5.0f;
        var scopeIconSize = 18.0f * ImGuiHelpers.GlobalScale;
        var totalWidth = (buttonSize * 3) + (spacing * 2) + scopeIconSize + spacing + 8;

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - totalWidth + ImGui.GetCursorPosX());

        var groupScopeIcon = FontAwesomeIcon.EyeSlash;
        if (group.Scope == WaymarkScope.Shared)
            groupScopeIcon = group.IsReadOnly ? FontAwesomeIcon.Lock : FontAwesomeIcon.Users;

        using (ImRaii.PushFont(Plugin.PluginInterface.UiBuilder.FontIcon))
        {
            // Move it slightly down to align with icon buttons vertically
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (2 * ImGuiHelpers.GlobalScale));
            ImGui.TextDisabled(groupScopeIcon.ToIconString());
        }
        if (ImGui.IsItemHovered())
        {
            if (group.Scope == WaymarkScope.Personal) ImGui.SetTooltip("Scope: Personal");
            else if (group.IsReadOnly) ImGui.SetTooltip("Scope: Shared (Read-Only)");
            else ImGui.SetTooltip("Scope: Shared");
        }

        // Move cursor back up if we shifted it down
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (2 * ImGuiHelpers.GlobalScale));
        ImGui.SameLine(0, spacing);

        // Check if this group can be edited by the current user
        var currentHash = plugin.WaymarkStorageService.CurrentCharacterHash;
        var isCreator = group.CreatorHash != null && currentHash != null && group.CreatorHash == currentHash;

        // Owner check for Edit Group
        var canEdit = isCreator;
        // Delete check: Owner AND not read-only
        var canDelete = isCreator && !group.IsReadOnly;
        // Read-Only check for Save Current Location
        var canAdd = !group.IsReadOnly || isCreator;

        // Quick-save to this group
        using (ImRaii.PushId("groupsave"))
        {
            using (ImRaii.Disabled(!canAdd))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
                {
                    plugin.WaymarkService.SaveCurrentLocation(group, group.Scope);
                }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Save Current Location to '{group.Name}'");
            }
        }

        ImGui.SameLine(0, spacing);

        // Edit group
        using (ImRaii.PushId("groupedit"))
        {
            using (ImRaii.Disabled(!canEdit))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit))
                {
                    groupEditName = group.Name;
                    groupEditScope = group.Scope;
                    groupEditIsReadOnly = group.IsReadOnly;
                    editingGroup = group;
                    showCreateGroupPopup = true;
                }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Edit Group Properties");
            }
        }

        ImGui.SameLine(0, spacing);

        // Delete group
        using (ImRaii.PushId("groupdelete"))
        {
            using (ImRaii.Disabled(!canDelete))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                {
                    groupToDelete = group;
                    showDeleteGroupConfirmation = true;
                }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Delete Group");
            }
        }
    }
}
