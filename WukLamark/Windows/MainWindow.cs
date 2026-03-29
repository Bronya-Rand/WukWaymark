using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukLamark.Helpers;
using WukLamark.Models;
using WukLamark.Services;
using WukLamark.Utils;
using WukLamark.Windows.Components;
using WukLamark.Windows.Sections;
using WukLamark.Windows.Sections.Modals;

namespace WukLamark.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    #region Components and Sections

    private readonly WaymarkTableComponent waymarkTableComponent;
    private readonly HeaderSection headerSection;
    private readonly SearchBarSection searchBarSection;
    private readonly TableViewSection tableViewSection;
    private readonly GroupViewSection groupViewSection;
    private readonly EmptyStateSection emptyStateSection;

    #endregion

    #region Modals

    private readonly DeleteWaymarkModal deleteWaymarkModal;
    private readonly DeleteGroupModal deleteGroupModal;
    private readonly ImportConflictModal importConflictModal;
    private readonly GroupEditorModal groupEditorModal;

    #endregion

    #region UI States

    private string importFeedback = string.Empty;
    private int importFeedbackTicks;

    #endregion

    public MainWindow(Plugin plugin)
        : base("WukLamark - Saved Locations##WWMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;

        // Initialize modals
        deleteWaymarkModal = new DeleteWaymarkModal
        {
            OnConfirmDelete = waymark =>
            {
                plugin.WaymarkService.DeleteWaymark(waymark);
            },
        };

        deleteGroupModal = new DeleteGroupModal
        {
            OnConfirmDelete = (group, keepWaymarks) =>
            {
                if (!keepWaymarks)
                {
                    var allWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();
                    var toDelete = allWaymarks.Where(w => w.GroupId == group.Id).ToList();

                    foreach (var w in toDelete)
                        plugin.WaymarkService.DeleteWaymark(w);
                }
                else
                {
                    // Move waymarks to unbound
                    var allWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();
                    var toMove = allWaymarks.Where(w => w.GroupId == group.Id).ToList();

                    foreach (var w in toMove)
                    {
                        w.GroupId = null;
                    }
                    plugin.WaymarkStorageService.SavePersonalWaymarks();
                    plugin.WaymarkStorageService.SaveSharedWaymarks();
                }

                // Remove from correct storage based on scope
                if (group.Scope == WaymarkScope.Shared)
                    plugin.WaymarkStorageService.SharedGroups.Remove(group);
                else
                    plugin.WaymarkStorageService.PersonalGroups.Remove(group);

                plugin.WaymarkStorageService.SavePersonalWaymarks();
                plugin.WaymarkStorageService.SaveSharedWaymarks();
            },
        };

        importConflictModal = new ImportConflictModal
        {
            OnApplyImport = (result, choices, overwriteAll) =>
            {
                ApplyImport(result, choices, overwriteAll);
            },
        };

        groupEditorModal = new GroupEditorModal
        {
            OnSave = (group, isEditing) =>
            {
                HandleGroupSave(group, isEditing);
            },
        };

        // Initialize components
        waymarkTableComponent = new WaymarkTableComponent(plugin, plugin.GameStateReaderService)
        {
            OnDeleteRequested = waymark =>
            {
                deleteWaymarkModal.Open(waymark);
            },
            OnFlagRequested = waymark =>
            {
                MapHelper.FlagMapLocation(waymark.Position, waymark.TerritoryId, waymark.MapId, waymark.Name);
            },
            OnExportRequested = waymark =>
            {
                WaymarkExportService.ExportToClipboard(waymark);
                importFeedback = $"Copied '{waymark.Name}' to clipboard!";
                importFeedbackTicks = 180;
            },
            OnSaveRequested = HandleWaymarkSave,
        };

        // Initialize sections
        headerSection = new HeaderSection(plugin.Configuration, plugin.GameStateReaderService, plugin.WaymarkStorageService)
        {
            OnImport = HandleImportResult,
            OnSaveLocation = () => plugin.WaymarkService.SaveCurrentLocation(),
            OnToggleView = () =>
            {
                plugin.Configuration.UseGroupView = !plugin.Configuration.UseGroupView;
                plugin.Configuration.Save();
            },
            OnSettingsClicked = () => plugin.ToggleConfigUi(),
        };

        searchBarSection = new SearchBarSection(plugin.GameStateReaderService, plugin.WaymarkService);

        tableViewSection = new TableViewSection(waymarkTableComponent);

        groupViewSection = new GroupViewSection(plugin.GameStateReaderService, plugin.WaymarkStorageService, waymarkTableComponent)
        {
            OnCreateGroup = () =>
            {
                groupEditorModal.Open(null);
            },
            OnEditGroup = group =>
            {
                groupEditorModal.Open(group);
            },
            OnDeleteGroup = group =>
            {
                deleteGroupModal.Open(group);
            },
            OnSaveToGroup = group =>
            {
                plugin.WaymarkService.SaveCurrentLocation(group, group.Scope);
            },
        };

        emptyStateSection = new EmptyStateSection(plugin.GameStateReaderService)
        {
            OnSaveLocation = () => plugin.WaymarkService.SaveCurrentLocation(),
        };
    }

    #region Event Handlers

    /// <summary>
    /// Handles saving a waymark after it has been edited in the WaymarkEditPopup.
    /// </summary>
    private void HandleWaymarkSave(Waymark waymark, WaymarkEditResult result)
    {
        var oldScope = waymark.Scope;
        waymark.Name = result.Name;
        waymark.Color = result.Color;
        waymark.Shape = result.Shape;
        waymark.Notes = result.Notes;
        waymark.GroupId = result.GroupId;
        waymark.VisibilityRadius = result.VisibilityRadius;
        waymark.IconId = result.IconId;
        waymark.Scope = result.Scope;
        waymark.IsReadOnly = result.IsReadOnly;

        if (oldScope != waymark.Scope)
        {
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
    }

    /// <summary>
    /// Handles saving a group after it has been edited in the GroupEditorModal.
    /// </summary>
    private void HandleGroupSave(WaymarkGroup group, bool isEditing)
    {
        if (isEditing && group.Id != Guid.Empty)
        {
            // Find the existing group
            var existing = plugin.WaymarkStorageService.PersonalGroups.FirstOrDefault(g => g.Id == group.Id)
                        ?? plugin.WaymarkStorageService.SharedGroups.FirstOrDefault(g => g.Id == group.Id);

            if (existing != null)
            {
                var oldScope = existing.Scope;
                existing.Name = group.Name;
                existing.Scope = group.Scope;
                existing.IsReadOnly = group.IsReadOnly;

                // If scope changed, move between collections
                if (oldScope != group.Scope)
                {
                    if (group.Scope == WaymarkScope.Shared)
                    {
                        plugin.WaymarkStorageService.PersonalGroups.Remove(existing);
                        existing.CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        plugin.WaymarkStorageService.SharedGroups.Add(existing);

                        // Move all child waymarks from Personal to Shared
                        var childWaymarks = plugin.WaymarkStorageService.PersonalWaymarks.Where(w => w.GroupId == existing.Id).ToList();
                        foreach (var w in childWaymarks)
                        {
                            plugin.WaymarkStorageService.PersonalWaymarks.Remove(w);
                            w.Scope = WaymarkScope.Shared;
                            w.IsReadOnly = existing.IsReadOnly;
                            w.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                            plugin.WaymarkStorageService.SharedWaymarks.Add(w);
                        }
                    }
                    else
                    {
                        plugin.WaymarkStorageService.SharedGroups.Remove(existing);
                        existing.CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        plugin.WaymarkStorageService.PersonalGroups.Add(existing);

                        // Move all child waymarks from Shared to Personal
                        var childWaymarks = plugin.WaymarkStorageService.SharedWaymarks.Where(w => w.GroupId == existing.Id).ToList();
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
        }
        else
        {
            // Create new group
            var newGroup = new WaymarkGroup
            {
                Id = group.Id == Guid.Empty ? Guid.NewGuid() : group.Id,
                Name = group.Name,
                CreatorHash = plugin.WaymarkStorageService.CurrentCharacterHash,
                IsReadOnly = group.IsReadOnly,
                Scope = group.Scope
            };

            if (group.Scope == WaymarkScope.Shared)
                plugin.WaymarkStorageService.SharedGroups.Add(newGroup);
            else
                plugin.WaymarkStorageService.PersonalGroups.Add(newGroup);
        }

        plugin.WaymarkStorageService.SavePersonalWaymarks();
        plugin.WaymarkStorageService.SaveSharedWaymarks();
    }

    /// <summary>
    /// Handles an import result from the HeaderSection.
    /// </summary>
    private void HandleImportResult(ImportResult result)
    {
        if (!result.Success)
        {
            importFeedback = $"Import failed: {result.ErrorMessage}";
            importFeedbackTicks = 240;
        }
        else if (result.Conflicts.Count > 0)
        {
            importConflictModal.Open(result);
        }
        else
        {
            ApplyImport(result, new Dictionary<Guid, bool>(), overwriteAll: false);
        }
    }

    /// <summary>
    /// Applies an import result to the storage service.
    /// Handles conflict resolution based on user choices.
    /// </summary>
    private void ApplyImport(ImportResult result, Dictionary<Guid, bool> importConflictChoices, bool overwriteAll)
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

    #endregion
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public override void Draw()
    {
        // Draw modal dialogs
        deleteWaymarkModal.Draw(plugin);
        deleteGroupModal.Draw(plugin);
        importConflictModal.Draw();
        groupEditorModal.Draw(plugin);

        // Import feedback banner
        if (importFeedbackTicks > 0)
        {
            importFeedbackTicks--;
            ImGui.TextColored(new Vector4(0.2f, 1f, 0.4f, 1f), importFeedback);
        }

        // Header
        headerSection.Draw();

        // Search bar and filters
        searchBarSection.Draw();

        // Main content area with scrolling
        using var child = ImRaii.Child("WaymarkListChild", Vector2.Zero, true);

        var visibleWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();

        if (!child.Success) return;

        if (visibleWaymarks.Count == 0)
        {
            emptyStateSection.Draw();
            return;
        }

        // Filter waymarks using the search bar's state
        var filteredWaymarks = FilterWaymarks(visibleWaymarks);

        // Draw the appropriate view
        if (plugin.Configuration.UseGroupView)
            groupViewSection.Draw(filteredWaymarks, searchBarSection.SearchFilter, searchBarSection.FilterCurrentZone);
        else
            tableViewSection.Draw(filteredWaymarks, visibleWaymarks.Count);
    }


    #region Search Filtering
    private List<Waymark> FilterWaymarks(List<Waymark> waymarks)
    {
        var filtered = waymarks;

        // Zone filter
        if (searchBarSection.FilterCurrentZone)
        {
            var currentMapId = Plugin.ClientState.MapId;
            filtered = filtered.Where(w => w.MapId == currentMapId).ToList();
        }

        // Text search filter
        if (!string.IsNullOrEmpty(searchBarSection.SearchFilter))
        {
            filtered = filtered.Where(w =>
                w.Name.Contains(searchBarSection.SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                w.Notes.Contains(searchBarSection.SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                LocationHelper.GetTerritoryName(w.TerritoryId).Contains(searchBarSection.SearchFilter, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        return filtered;
    }

    #endregion
}
