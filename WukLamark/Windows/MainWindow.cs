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

    private readonly MarkerTableComponent markerTableComponent;
    private readonly HeaderSection headerSection;
    private readonly SearchBarSection searchBarSection;
    private readonly TableViewSection tableViewSection;
    private readonly GroupViewSection groupViewSection;
    private readonly EmptyStateSection emptyStateSection;

    #endregion

    #region Modals

    private readonly DeleteMarkerModal deleteMarkerModal;
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
        deleteMarkerModal = new DeleteMarkerModal
        {
            OnConfirmDelete = marker =>
            {
                plugin.MarkerService.DeleteMarker(marker);
            },
        };

        deleteGroupModal = new DeleteGroupModal
        {
            OnConfirmDelete = (group, keepMarkers) =>
            {
                if (!keepMarkers)
                {
                    var allMarkers = plugin.MarkerStorageService.GetVisibleMarkers();
                    var toDelete = allMarkers.Where(w => w.GroupId == group.Id).ToList();

                    foreach (var w in toDelete)
                        plugin.MarkerService.DeleteMarker(w);
                }
                else
                {
                    // Move markers to unbound
                    var allMarkers = plugin.MarkerStorageService.GetVisibleMarkers();
                    var toMove = allMarkers.Where(w => w.GroupId == group.Id).ToList();

                    foreach (var w in toMove)
                    {
                        w.GroupId = null;
                    }
                    plugin.MarkerStorageService.SavePersonalMarkers();
                    plugin.MarkerStorageService.SaveSharedMarkers();
                }

                // Remove from correct storage based on scope
                if (group.Scope == MarkerScope.Shared)
                    plugin.MarkerStorageService.SharedGroups.Remove(group);
                else
                    plugin.MarkerStorageService.PersonalGroups.Remove(group);

                plugin.MarkerStorageService.SavePersonalMarkers();
                plugin.MarkerStorageService.SaveSharedMarkers();
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
        markerTableComponent = new MarkerTableComponent(plugin, plugin.GameStateReaderService)
        {
            OnDeleteRequested = marker =>
            {
                deleteMarkerModal.Open(marker);
            },
            OnFlagRequested = marker =>
            {
                MapHelper.FlagMapLocation(marker.Position, marker.TerritoryId, marker.MapId, marker.Name);
            },
            OnExportRequested = marker =>
            {
                MarkerExportService.ExportToClipboard(marker);
                importFeedback = $"Copied '{marker.Name}' to clipboard!";
                importFeedbackTicks = 180;
            },
            OnSaveRequested = HandleMarkerSave,
        };

        // Initialize sections
        headerSection = new HeaderSection(plugin.Configuration, plugin.GameStateReaderService, plugin.MarkerStorageService)
        {
            OnImport = HandleImportResult,
            OnSaveLocation = () => plugin.MarkerService.SaveCurrentLocation(),
            OnToggleView = () =>
            {
                plugin.Configuration.UseGroupView = !plugin.Configuration.UseGroupView;
                plugin.Configuration.Save();
            },
            OnSettingsClicked = () => plugin.ToggleConfigUi(),
        };

        searchBarSection = new SearchBarSection(plugin.GameStateReaderService, plugin.MarkerService);

        tableViewSection = new TableViewSection(markerTableComponent);

        groupViewSection = new GroupViewSection(plugin.GameStateReaderService, plugin.MarkerStorageService, markerTableComponent)
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
                plugin.MarkerService.SaveCurrentLocation(group, group.Scope);
            },
        };

        emptyStateSection = new EmptyStateSection(plugin.GameStateReaderService)
        {
            OnSaveLocation = () => plugin.MarkerService.SaveCurrentLocation(),
        };
    }

    #region Event Handlers

    /// <summary>
    /// Handles saving a marker after it has been edited in the MarkerEditPopup.
    /// </summary>
    private void HandleMarkerSave(Marker marker, MarkerEditResult result)
    {
        var oldScope = marker.Scope;
        marker.Name = result.Name;
        marker.Color = result.Color;
        marker.Shape = result.Shape;
        marker.Notes = result.Notes;
        marker.GroupId = result.GroupId;
        marker.VisibilityRadius = result.VisibilityRadius;
        marker.IconId = result.IconId;
        marker.Scope = result.Scope;
        marker.IsReadOnly = result.IsReadOnly;
        marker.AppliesToAllWorlds = result.AppliesToAllWorlds;

        if (oldScope != marker.Scope)
        {
            if (marker.Scope == MarkerScope.Personal)
            {
                plugin.MarkerStorageService.SharedMarkers.Remove(marker);
                marker.CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash;
                marker.IsReadOnly = false;
                plugin.MarkerStorageService.PersonalMarkers.Add(marker);
            }
            else
            {
                plugin.MarkerStorageService.PersonalMarkers.Remove(marker);
                marker.CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash;
                plugin.MarkerStorageService.SharedMarkers.Add(marker);
            }
        }

        plugin.MarkerStorageService.SavePersonalMarkers();
        plugin.MarkerStorageService.SaveSharedMarkers();
    }

    /// <summary>
    /// Handles saving a group after it has been edited in the GroupEditorModal.
    /// </summary>
    private void HandleGroupSave(MarkerGroup group, bool isEditing)
    {
        if (isEditing && group.Id != Guid.Empty)
        {
            // Find the existing group
            var existing = plugin.MarkerStorageService.PersonalGroups.FirstOrDefault(g => g.Id == group.Id)
                        ?? plugin.MarkerStorageService.SharedGroups.FirstOrDefault(g => g.Id == group.Id);

            if (existing != null)
            {
                var oldScope = existing.Scope;
                existing.Name = group.Name;
                existing.Scope = group.Scope;
                existing.IsReadOnly = group.IsReadOnly;

                // If scope changed, move between collections
                if (oldScope != group.Scope)
                {
                    if (group.Scope == MarkerScope.Shared)
                    {
                        plugin.MarkerStorageService.PersonalGroups.Remove(existing);
                        existing.CreatorHash = plugin.MarkerStorageService.CurrentCharacterHash;
                        plugin.MarkerStorageService.SharedGroups.Add(existing);

                        // Move all child markers from Personal to Shared
                        var childMarkers = plugin.MarkerStorageService.PersonalMarkers.Where(m => m.GroupId == existing.Id).ToList();
                        foreach (var m in childMarkers)
                        {
                            plugin.MarkerStorageService.PersonalMarkers.Remove(m);
                            m.Scope = MarkerScope.Shared;
                            m.IsReadOnly = existing.IsReadOnly;
                            m.CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash;
                            plugin.MarkerStorageService.SharedMarkers.Add(m);
                        }
                    }
                    else
                    {
                        plugin.MarkerStorageService.SharedGroups.Remove(existing);
                        existing.CreatorHash = plugin.MarkerStorageService.CurrentCharacterHash;
                        plugin.MarkerStorageService.PersonalGroups.Add(existing);

                        // Move all child markers from Shared to Personal
                        var childMarkers = plugin.MarkerStorageService.SharedMarkers.Where(m => m.GroupId == existing.Id).ToList();
                        foreach (var m in childMarkers)
                        {
                            plugin.MarkerStorageService.SharedMarkers.Remove(m);
                            m.Scope = MarkerScope.Personal;
                            m.IsReadOnly = false;
                            m.CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash;
                            plugin.MarkerStorageService.PersonalMarkers.Add(m);
                        }
                    }
                }
            }
        }
        else
        {
            // Create new group
            var newGroup = new MarkerGroup
            {
                Id = group.Id == Guid.Empty ? Guid.NewGuid() : group.Id,
                Name = group.Name,
                CreatorHash = plugin.MarkerStorageService.CurrentCharacterHash,
                IsReadOnly = group.IsReadOnly,
                Scope = group.Scope
            };

            if (group.Scope == MarkerScope.Shared)
                plugin.MarkerStorageService.SharedGroups.Add(newGroup);
            else
                plugin.MarkerStorageService.PersonalGroups.Add(newGroup);
        }

        plugin.MarkerStorageService.SavePersonalMarkers();
        plugin.MarkerStorageService.SaveSharedMarkers();
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
            ApplyImport(result, [], overwriteAll: false);
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
        var addedMarkers = 0;
        var addedGroups = 0;

        // Apply groups first (markers may reference them)
        foreach (var importedGroup in result.Payload.Groups)
        {
            var isSharedGroup = importedGroup.Scope == MarkerScope.Shared;
            if (conflictIds.Contains(importedGroup.Id))
            {
                var shouldOverwrite = overwriteAll || (importConflictChoices.TryGetValue(importedGroup.Id, out var v) && v);
                if (shouldOverwrite)
                {
                    // Remove entirely from both group collections first to safely overwrite
                    plugin.MarkerStorageService.PersonalGroups.RemoveAll(g => g.Id == importedGroup.Id);
                    plugin.MarkerStorageService.SharedGroups.RemoveAll(g => g.Id == importedGroup.Id);
                    importedGroup.CreatorHash = plugin.MarkerStorageService.CurrentCharacterHash;
                    if (isSharedGroup)
                    {
                        plugin.MarkerStorageService.SharedGroups.Add(importedGroup);
                    }
                    else
                    {
                        plugin.MarkerStorageService.PersonalGroups.Add(importedGroup);
                    }
                    addedGroups++;
                }
                else
                {
                    // User chose NOT to overwrite. Apply specific resolution rules.
                    var localGroup = plugin.MarkerStorageService.PersonalGroups.FirstOrDefault(g => g.Id == importedGroup.Id)
                                  ?? plugin.MarkerStorageService.SharedGroups.FirstOrDefault(g => g.Id == importedGroup.Id);

                    // Rules 1 & 3: If local group is Shared and Read-Only, generate a new Guid so we don't merge into a locked group.
                    if (localGroup != null && localGroup.Scope == MarkerScope.Shared && localGroup.IsReadOnly)
                    {
                        var oldId = importedGroup.Id;
                        importedGroup.Id = Guid.NewGuid();
                        groupIdSwaps[oldId] = importedGroup.Id;

                        importedGroup.CreatorHash = plugin.MarkerStorageService.CurrentCharacterHash;
                        if (isSharedGroup)
                        {
                            plugin.MarkerStorageService.SharedGroups.Add(importedGroup);
                        }
                        else
                        {
                            plugin.MarkerStorageService.PersonalGroups.Add(importedGroup);
                        }
                        addedGroups++;
                    }
                    // Rules 2, 4, 5: Merge into existing. Group is skipped, markers will attach to existing local group.
                }
            }
            else
            {
                importedGroup.CreatorHash = plugin.MarkerStorageService.CurrentCharacterHash;
                if (isSharedGroup)
                {
                    plugin.MarkerStorageService.SharedGroups.Add(importedGroup);
                }
                else
                {
                    plugin.MarkerStorageService.PersonalGroups.Add(importedGroup);
                }
                addedGroups++;
            }
        }

        // Apply imported markers
        foreach (var importedMarker in result.Payload.Waymarks)
        {
            if (importedMarker.GroupId.HasValue && groupIdSwaps.TryGetValue(importedMarker.GroupId.Value, out var newGroupId))
            {
                importedMarker.GroupId = newGroupId;
                // Since the group's ID changed (Rules 1 & 3), we clone the marker so it doesn't conflict
                // and gets safely appended to the new group.
                importedMarker.Id = Guid.NewGuid();
            }

            var isShared = importedMarker.Scope == MarkerScope.Shared;
            if (conflictIds.Contains(importedMarker.Id))
            {
                var shouldOverwrite = overwriteAll || (importConflictChoices.TryGetValue(importedMarker.Id, out var v) && v);
                if (shouldOverwrite)
                {
                    // Remove entirely from both first to safely overwrite
                    plugin.MarkerStorageService.PersonalMarkers.RemoveAll(w => w.Id == importedMarker.Id);
                    plugin.MarkerStorageService.SharedMarkers.RemoveAll(w => w.Id == importedMarker.Id);

                    if (isShared)
                    {
                        importedMarker.CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash;
                        plugin.MarkerStorageService.SharedMarkers.Add(importedMarker);
                    }
                    else
                    {
                        importedMarker.CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash;
                        plugin.MarkerStorageService.PersonalMarkers.Add(importedMarker);
                    }
                    addedMarkers++;
                }
                // else skip
            }
            else
            {
                if (isShared)
                {
                    importedMarker.CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash;
                    plugin.MarkerStorageService.SharedMarkers.Add(importedMarker);
                }
                else
                {
                    importedMarker.CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash;
                    plugin.MarkerStorageService.PersonalMarkers.Add(importedMarker);
                }
                addedMarkers++;
            }
        }

        plugin.MarkerStorageService.SavePersonalMarkers();
        plugin.MarkerStorageService.SaveSharedMarkers();
        importFeedback = $"Imported {addedMarkers} marker(s) and {addedGroups} group(s).";
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
        deleteMarkerModal.Draw(plugin);
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
        using var child = ImRaii.Child("MarkerListChild", Vector2.Zero, true);

        var visibleMarkers = plugin.MarkerStorageService.GetVisibleMarkers();

        if (!child.Success) return;

        if (visibleMarkers.Count == 0)
        {
            emptyStateSection.Draw();
            return;
        }

        // Filter markers using the search bar's state
        var filteredMarkers = FilterMarkers(visibleMarkers);

        // Draw the appropriate view
        if (plugin.Configuration.UseGroupView)
            groupViewSection.Draw(filteredMarkers, searchBarSection.SearchFilter, searchBarSection.FilterCurrentZone);
        else
            tableViewSection.Draw(filteredMarkers, visibleMarkers.Count);
    }


    #region Search Filtering
    private List<Marker> FilterMarkers(List<Marker> markers)
    {
        var filtered = markers;

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
