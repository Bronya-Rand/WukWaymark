using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Services;

namespace WukLamark.Windows;

public partial class MainWindow
{
    private void DrawImportConflictModal()
    {
        if (!showImportConflictModal || pendingImport == null) return;

        ImGui.OpenPopup("Import Conflicts");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(480, 0), ImGuiCond.Always);

        using var importConflictModal = ImRaii.PopupModal("Import Conflicts", ref showImportConflictModal, ImGuiWindowFlags.AlwaysAutoResize);
        if (importConflictModal)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), "Some items already exist in your collection.");
            ImGui.Text("Choose how to handle each conflict:");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            foreach (var conflict in pendingImport.Conflicts)
            {
                var overwrite = importConflictChoices.TryGetValue(conflict.Id, out var v) && v;
                var label = conflict.IsGroup ? $" {conflict.Name} (Group)" : $" {conflict.Name} (Waymark)";
                if (ImGui.Checkbox($"Overwrite: {label}##import_{conflict.Id}", ref overwrite))
                    importConflictChoices[conflict.Id] = overwrite;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Overwrite All", new Vector2(140, 0)))
            {
                ApplyImport(pendingImport!, overwriteAll: true);
                pendingImport = null;
                showImportConflictModal = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Apply Choices", new Vector2(140, 0)))
            {
                ApplyImport(pendingImport!, overwriteAll: false);
                pendingImport = null;
                showImportConflictModal = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(80, 0)))
            {
                pendingImport = null;
                showImportConflictModal = false;
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private void ApplyImport(ImportResult result, bool overwriteAll)
    {
        if (result.Payload == null) return;

        var groupIdSwaps = new Dictionary<Guid, Guid>();
        var conflictIds = new HashSet<Guid>(result.Conflicts.Select(c => c.Id));
        var addedWaymarks = 0;
        var addedGroups = 0;

        // Apply groups first (waymarks may reference them)
        foreach (var importedGroup in result.Payload.Groups)
        {
            var isSharedGroup = importedGroup.Scope == WaymarkScope.Shared;
            if (conflictIds.Contains(importedGroup.Id))
            {
                var shouldOverwrite = overwriteAll || (importConflictChoices.TryGetValue(importedGroup.Id, out var v) && v);
                if (shouldOverwrite)
                {
                    // Remove entirely from both group collections first to safely overwrite
                    plugin.WaymarkStorageService.PersonalGroups.RemoveAll(g => g.Id == importedGroup.Id);
                    plugin.WaymarkStorageService.SharedGroups.RemoveAll(g => g.Id == importedGroup.Id);
                    importedGroup.CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                    if (isSharedGroup)
                    {
                        plugin.WaymarkStorageService.SharedGroups.Add(importedGroup);
                    }
                    else
                    {
                        plugin.WaymarkStorageService.PersonalGroups.Add(importedGroup);
                    }
                    addedGroups++;
                }
                else
                {
                    // User chose NOT to overwrite. Apply specific resolution rules.
                    var localGroup = plugin.WaymarkStorageService.PersonalGroups.FirstOrDefault(g => g.Id == importedGroup.Id)
                                  ?? plugin.WaymarkStorageService.SharedGroups.FirstOrDefault(g => g.Id == importedGroup.Id);

                    // Rules 1 & 3: If local group is Shared and Read-Only, generate a new Guid so we don't merge into a locked group.
                    if (localGroup != null && localGroup.Scope == WaymarkScope.Shared && localGroup.IsReadOnly)
                    {
                        var oldId = importedGroup.Id;
                        importedGroup.Id = Guid.NewGuid();
                        groupIdSwaps[oldId] = importedGroup.Id;

                        importedGroup.CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        if (isSharedGroup)
                        {
                            plugin.WaymarkStorageService.SharedGroups.Add(importedGroup);
                        }
                        else
                        {
                            plugin.WaymarkStorageService.PersonalGroups.Add(importedGroup);
                        }
                        addedGroups++;
                    }
                    // Rules 2, 4, 5: Merge into existing. Group is skipped, waymarks will attach to existing local group.
                }
            }
            else
            {
                importedGroup.CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                if (isSharedGroup)
                {
                    plugin.WaymarkStorageService.SharedGroups.Add(importedGroup);
                }
                else
                {
                    plugin.WaymarkStorageService.PersonalGroups.Add(importedGroup);
                }
                addedGroups++;
            }
        }

        // Apply waymarks
        foreach (var importedWaymark in result.Payload.Waymarks)
        {
            if (importedWaymark.GroupId.HasValue && groupIdSwaps.TryGetValue(importedWaymark.GroupId.Value, out var newGroupId))
            {
                importedWaymark.GroupId = newGroupId;
                // Since the group's ID changed (Rules 1 & 3), we clone the waymark so it doesn't conflict
                // and gets safely appended to the new group.
                importedWaymark.Id = Guid.NewGuid();
            }

            var isShared = importedWaymark.Scope == WaymarkScope.Shared;
            if (conflictIds.Contains(importedWaymark.Id))
            {
                var shouldOverwrite = overwriteAll || (importConflictChoices.TryGetValue(importedWaymark.Id, out var v) && v);
                if (shouldOverwrite)
                {
                    // Remove entirely from both first to safely overwrite
                    plugin.WaymarkStorageService.PersonalWaymarks.RemoveAll(w => w.Id == importedWaymark.Id);
                    plugin.WaymarkStorageService.SharedWaymarks.RemoveAll(w => w.Id == importedWaymark.Id);

                    if (isShared)
                    {
                        importedWaymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        plugin.WaymarkStorageService.SharedWaymarks.Add(importedWaymark);
                    }
                    else
                    {
                        importedWaymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        plugin.WaymarkStorageService.PersonalWaymarks.Add(importedWaymark);
                    }
                    addedWaymarks++;
                }
                // else skip
            }
            else
            {
                if (isShared)
                {
                    importedWaymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                    plugin.WaymarkStorageService.SharedWaymarks.Add(importedWaymark);
                }
                else
                {
                    importedWaymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                    plugin.WaymarkStorageService.PersonalWaymarks.Add(importedWaymark);
                }
                addedWaymarks++;
            }
        }

        plugin.WaymarkStorageService.SavePersonalWaymarks();
        plugin.WaymarkStorageService.SaveSharedWaymarks();
        importFeedback = $"Imported {addedWaymarks} waymark(s) and {addedGroups} group(s).";
        importFeedbackTicks = 240;
        Plugin.Log.Information(importFeedback);
    }

    private void DrawDeleteWaypointModal()
    {
        if (!showDeleteWaymarkConfirmation || waymarkToDelete == null) return;
        if ((waymarkToDelete.Scope == WaymarkScope.Shared &&
            waymarkToDelete.IsReadOnly) ||
            (waymarkToDelete.Scope == WaymarkScope.Personal &&
            (waymarkToDelete.CharacterHash == null ||
            plugin.WaymarkStorageService.CurrentCharacterHash == null ||
            waymarkToDelete.CharacterHash != plugin.WaymarkStorageService.CurrentCharacterHash)))
        {
            // This should never happen since the delete button is disabled in this case, but just in case:
            Plugin.Log.Warning("Attempted to delete a waymark that doesn't belong to the current character. Action blocked.");
            showDeleteWaymarkConfirmation = false;
            waymarkToDelete = null;
            return;
        }

        ImGui.OpenPopup("Delete Waypoint?##WWDeleteWaypointModal");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var deleteWaypointModal = ImRaii.PopupModal("Delete Waypoint?##WWDeleteWaypointModal", ref showDeleteWaymarkConfirmation, ImGuiWindowFlags.AlwaysAutoResize);
        if (deleteWaypointModal)
        {
            ImGui.Text($"Are you sure you want to delete the waymark '{waymarkToDelete.Name}'?");
            ImGui.Text("This action can be undone with the Undo button.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Center buttons
            var buttonWidth = 120f;
            var spacing = 10f;
            var totalWidth = (buttonWidth * 2) + spacing;
            var windowWidth = ImGui.GetContentRegionAvail().X;
            var padding = (windowWidth - totalWidth) / 2;

            if (padding > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padding);

            if (ImGui.Button("Yes", new Vector2(buttonWidth, 0)))
            {
                plugin.WaymarkService.DeleteWaymark(waymarkToDelete);
                showDeleteWaymarkConfirmation = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, spacing);

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                showDeleteWaymarkConfirmation = false;
                ImGui.CloseCurrentPopup();
            }
        }
    }

    private void DrawDeleteGroupConfirmation()
    {
        if (!showDeleteGroupConfirmation || groupToDelete == null) return;
        var isCreator = groupToDelete.CreatorHash != null &&
                        plugin.WaymarkStorageService.CurrentCharacterHash != null &&
                        groupToDelete.CreatorHash == plugin.WaymarkStorageService.CurrentCharacterHash;

        if (groupToDelete.IsReadOnly || !isCreator)
        {
            // This should never happen since the delete button is disabled, but just in case:
            Plugin.Log.Warning("Attempted to delete a group that doesn't belong to the current character. Action blocked.");
            showDeleteGroupConfirmation = false;
            groupToDelete = null;
            return;
        }

        ImGui.OpenPopup("Delete Group?##WWDeleteGroupModal");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        using var deleteGroupModal = ImRaii.PopupModal("Delete Group?##WWDeleteGroupModal", ref showDeleteGroupConfirmation, ImGuiWindowFlags.AlwaysAutoResize);
        if (deleteGroupModal)
        {
            var waymarksInGroup = plugin.WaymarkStorageService.GetVisibleWaymarks().Count(w => w.GroupId == groupToDelete.Id);
            ImGui.Text($"Are you sure you want to delete the group '{groupToDelete.Name}'?");
            if (waymarksInGroup > 0)
            {
                ImGui.Text($"This group contains {waymarksInGroup} waymark(s).");
                ImGui.Checkbox("Keep waymarks (move to Ungrouped)", ref keepWaymarksOnGroupDelete);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Delete", new Vector2(120, 0)))
            {
                if (!keepWaymarksOnGroupDelete)
                {
                    var allWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();
                    var toDelete = allWaymarks.Where(w => w.GroupId == groupToDelete.Id).ToList();

                    foreach (var w in toDelete)
                        plugin.WaymarkService.DeleteWaymark(w);
                }
                else
                {
                    // Move waymarks to unbound
                    var allWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();
                    var toMove = allWaymarks.Where(w => w.GroupId == groupToDelete.Id).ToList();

                    foreach (var w in toMove)
                    {
                        w.GroupId = null;
                    }
                    plugin.WaymarkStorageService.SavePersonalWaymarks();
                    plugin.WaymarkStorageService.SaveSharedWaymarks();
                }

                // Remove from correct storage based on scope
                if (groupToDelete.Scope == WaymarkScope.Shared)
                    plugin.WaymarkStorageService.SharedGroups.Remove(groupToDelete);
                else
                    plugin.WaymarkStorageService.PersonalGroups.Remove(groupToDelete);

                plugin.WaymarkStorageService.SavePersonalWaymarks();
                plugin.WaymarkStorageService.SaveSharedWaymarks();
                showDeleteGroupConfirmation = false;
                groupToDelete = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, 10);

            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                showDeleteGroupConfirmation = false;
                groupToDelete = null;
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
