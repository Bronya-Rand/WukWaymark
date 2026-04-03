using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Services;
using WukLamark.Utils;
using WukLamark.Windows.Components;

namespace WukLamark.Windows.Sections;

internal class GroupViewSection(GameStateReaderService gameStateReaderService, MarkerStorageService markerStorageService, MarkerTableComponent tableComponent)
{
    private readonly GameStateReaderService gameStateReaderService = gameStateReaderService;
    private readonly MarkerStorageService markerStorageService = markerStorageService;
    private readonly MarkerTableComponent tableComponent = tableComponent;

    public Action? OnCreateGroup { get; set; }
    public Action<MarkerGroup>? OnEditGroup { get; set; }
    public Action<MarkerGroup>? OnDeleteGroup { get; set; }
    public Action<MarkerGroup>? OnSaveToGroup { get; set; }

    public void Draw(List<Marker> filteredMarkers, string searchFilter, bool filterCurrentZone)
    {
        var groups = markerStorageService.GetVisibleGroups();

        // "+ New Group" button
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            OnCreateGroup?.Invoke();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Create New Group");
        }

        ImGui.Spacing();

        foreach (var group in groups)
        {
            var groupMarkers = filteredMarkers.Where(m => m.GroupId == group.Id).ToList();

            if (groupMarkers.Count == 0 && filterCurrentZone) continue;

            if (!string.IsNullOrEmpty(searchFilter))
            {
                var groupNameMatches = group.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase);
                if (groupMarkers.Count == 0 && !groupNameMatches)
                    continue;
            }

            var identifier = group.Id.ToString();
            using var groupId = ImRaii.PushId(identifier);

            var headerOpen = ImGui.CollapsingHeader($"{group.Name} ({groupMarkers.Count})###group_{identifier}", ImGuiTreeNodeFlags.AllowItemOverlap);

            DrawGroupHeaderButtons(group);

            if (headerOpen)
            {
                if (groupMarkers.Count == 0)
                {
                    using (ImRaii.PushIndent(10))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No markers in this group.");
                    }
                }
                else
                {
                    using var tableId = ImRaii.PushId($"GroupTable_{identifier}");
                    tableComponent.Draw(groupMarkers, group);
                }
                ImGui.Spacing();
            }
        }

        // Ungrouped markers section
        var ungroupedMarkers = filteredMarkers.Where(m => m.GroupId == null).ToList();

        if (!string.IsNullOrEmpty(searchFilter) && ungroupedMarkers.Count == 0)
            return;

        if (ungroupedMarkers.Count > 0 || string.IsNullOrEmpty(searchFilter))
        {
            var ungroupedOpen = ImGui.CollapsingHeader($"Ungrouped ({ungroupedMarkers.Count})###ungrouped", ImGuiTreeNodeFlags.DefaultOpen);
            if (ungroupedOpen)
            {
                if (ungroupedMarkers.Count == 0)
                {
                    using (ImRaii.PushIndent(10))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No ungrouped markers.");
                    }
                }
                else
                {
                    using var tableId = ImRaii.PushId("GroupTableUngrouped");
                    tableComponent.Draw(ungroupedMarkers);
                }
            }
        }
    }
    private void DrawGroupHeaderButtons(MarkerGroup group)
    {
        var isLoggedIn = gameStateReaderService.IsLoggedIn;
        var inPvP = gameStateReaderService.IsInPvP;
        var inCombat = gameStateReaderService.IsInCombat;
        var markersDisabled = gameStateReaderService.DisableMarkerActions();

        var buttonSize = 20.0f * ImGuiHelpers.GlobalScale;
        var spacing = 5.0f;
        var scopeIconSize = 18.0f * ImGuiHelpers.GlobalScale;
        var totalWidth = (buttonSize * 3) + (spacing * 2) + scopeIconSize + spacing + 8;

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - totalWidth + ImGui.GetCursorPosX());

        var groupScopeIcon = FontAwesomeIcon.EyeSlash;
        if (group.Scope == MarkerScope.Shared)
            groupScopeIcon = group.IsReadOnly ? FontAwesomeIcon.Lock : FontAwesomeIcon.Users;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (2 * ImGuiHelpers.GlobalScale));
            ImGui.TextDisabled(groupScopeIcon.ToIconString());
        }
        if (ImGui.IsItemHovered())
        {
            var tooltip = group.Scope == MarkerScope.Personal ? "Personal Group" :
                group.IsReadOnly ? "Shared Group (Read-Only)" :
                "Shared Group";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (2 * ImGuiHelpers.GlobalScale));
        ImGui.SameLine(0, spacing);

        var currentHash = markerStorageService.CurrentCharacterHash;
        var isCreator = group.CreatorHash != null && currentHash != null && group.CreatorHash == currentHash;

        var canEdit = (group.Scope == MarkerScope.Personal && isCreator) || (group.Scope == MarkerScope.Shared && (isCreator || !group.IsReadOnly));
        var canDelete = isCreator && !group.IsReadOnly;
        var canAdd = (group.Scope == MarkerScope.Personal && isCreator) || (group.Scope == MarkerScope.Shared && !group.IsReadOnly);

        // Quick-save to this group
        using (ImRaii.PushId("groupsave"))
        {
            using (ImRaii.Disabled(!canAdd || markersDisabled))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
                    OnSaveToGroup?.Invoke(group);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to save markers!" :
                inPvP ? "Saving markers is disabled in PvP zones." :
                inCombat ? "Saving markers is disabled in combat." :
                group.IsReadOnly ? "Cannot save markers to a read-only group." :
                "Save current location to this group.";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SameLine(0, spacing);

        // Edit group
        using (ImRaii.PushId("groupedit"))
        {
            using (ImRaii.Disabled(!canEdit || !isLoggedIn))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit))
                    OnEditGroup?.Invoke(group);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to manage this group!" :
                group.IsReadOnly && !isCreator ? "Only the group's creator can edit this group." :
                "Edit group properties";
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SameLine(0, spacing);

        // Delete group
        using (ImRaii.PushId("groupdelete"))
        {
            using (ImRaii.Disabled(!canDelete || !isLoggedIn))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                    OnDeleteGroup?.Invoke(group);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to manage this group!" :
                group.IsReadOnly && isCreator ? "Cannot delete a read-only group." :
                !isCreator ? "Only the group's creator can delete this group." :
                "Delete Group";
            ImGui.SetTooltip(tooltip);
        }
    }
}
