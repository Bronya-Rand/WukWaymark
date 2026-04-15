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
            OnConfirmDelete = markers =>
            {
                foreach (var marker in markers)
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
                MarkerExportService.ExportShareToClipboard(marker);
                if (marker.Count == 1)
                    importFeedback = $"Copied marker to clipboard!";
                else
                    importFeedback = $"Copied {marker.Count} markers to clipboard!";
                importFeedbackTicks = 180;
            },
            OnSaveRequested = HandleMarkerSave,
        };

        // Initialize sections
        headerSection = new HeaderSection(plugin.Configuration, plugin.GameStateReaderService, plugin.MarkerStorageService)
        {
            OnImport = HandleImportResult,
            OnExportSelected = HandleExportSelected,
            CanExportMarkers = () => markerTableComponent.IsMultiSelect,
            OnSaveLocation = (isShiftHeld) => plugin.MarkerService.SaveCurrentLocation(crossworld: isShiftHeld),
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
            IsMultiSelectActive = () => markerTableComponent.IsMultiSelect,
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
            OnSaveToGroup = (group, isShiftHeld) =>
            {
                plugin.MarkerService.SaveCurrentLocation(group, group.Scope, isShiftHeld);
            },
            OnExportGroupMarkers = markers =>
            {
                MarkerExportService.ExportShareToClipboard(markers);
                importFeedback = $"Copied {markers.Count} marker(s) from group to clipboard!";
                importFeedbackTicks = 180;
            }
        };

        emptyStateSection = new EmptyStateSection(plugin.GameStateReaderService)
        {
            OnSaveLocation = (isShiftHeld) => plugin.MarkerService.SaveCurrentLocation(crossworld: isShiftHeld),
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
        marker.IconSize = result.IconSize;
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
    private void HandleExportSelected()
    {
        var allVisible = plugin.MarkerStorageService.GetVisibleMarkers();
        var selected = allVisible.Where(m => markerTableComponent.SelectedMarkerIds.Contains(m.Id)).ToList();

        if (selected.Count == 0)
        {
            importFeedback = "No markers selected for export.";
            importFeedbackTicks = 180;
            return;
        }

        MarkerExportService.ExportShareToClipboard(selected);
        importFeedback = $"Copied {selected.Count} marker(s) to clipboard.";
        importFeedbackTicks = 180;
    }

    /// <summary>
    /// Applies an import result to the storage service.
    /// Handles conflict resolution based on user choices.
    /// </summary>
    private void ApplyImport(ImportResult result, Dictionary<Guid, bool> importConflictChoices, bool overwriteAll)
    {
        if (result.Payload == null) return;

        var addedMarkers = 0;

        // Apply imported markers
        foreach (var importedMarker in result.Payload.Markers)
        {
            var newMarker = new Marker
            {
                Id = Guid.NewGuid(),
                Name = importedMarker.Name,
                Position = importedMarker.Position,
                TerritoryId = importedMarker.TerritoryId,
                WardId = importedMarker.WardId,
                MapId = importedMarker.MapId,
                WorldId = importedMarker.WorldId,
                AppliesToAllWorlds = importedMarker.AppliesToAllWorlds,
                Color = importedMarker.Color,
                CreatedAt = DateTime.Now,
                Shape = importedMarker.Shape,
                IconId = importedMarker.IconId,
                Scope = MarkerScope.Personal,
                CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash,
            };
            plugin.MarkerStorageService.PersonalMarkers.Add(newMarker);
            addedMarkers++;
        }

        plugin.MarkerStorageService.SavePersonalMarkers();
        importFeedback = $"Imported {addedMarkers} marker(s) to WukLamark.";
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
