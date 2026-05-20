using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using WukLamark.Helpers;
using WukLamark.Models;
using WukLamark.Services;
using WukLamark.Utils;
using WukLamark.Windows.Components;
using WukLamark.Windows.Sections;
using WukLamark.Windows.Sections.Modals;

namespace WukLamark.Windows.Tabs.MarkerList
{
    internal sealed class MarkerListTab
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
        private readonly CustomIconImageUploadModal customIconImageUploadModal;

        #endregion
        public MarkerListTab(Plugin plugin)
        {
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
                    var groupMarkers = plugin.MarkerStorageService.GetMarkersInGroup(group.Id);
                    if (!keepMarkers)
                    {
                        foreach (var w in groupMarkers)
                            plugin.MarkerService.DeleteMarker(w);
                    }
                    else
                    {
                        foreach (var w in groupMarkers)
                            plugin.MarkerStorageService.MoveMarkerToGroup(w.Id, null);
                    }

                    // Delete group using storage service
                    plugin.MarkerStorageService.DeleteGroup(group.Id);
                },
            };

            importConflictModal = new ImportConflictModal
            {
                OnApplyImport = (result, choices, overwriteAll, importGroup) =>
                {
                    ApplyImport(result, choices, overwriteAll, importGroup);
                },
            };

            groupEditorModal = new GroupEditorModal
            {
                OnSave = HandleGroupSave,
            };

            customIconImageUploadModal = new CustomIconImageUploadModal
            {
                OnImageUpload = HandleImageUpload,
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
                        ResultNotifications.SendSuccessMessage($"Copied marker '{marker.First().Name}' to clipboard!");
                    else
                        ResultNotifications.SendSuccessMessage($"Copied {marker.Count} markers to clipboard!");
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
                OnImageUploadClicked = () => customIconImageUploadModal.Open(),
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
                OnImportGroupMarkers = HandleImportResult,
                OnExportGroupMarkers = markers =>
                {
                    MarkerExportService.ExportShareToClipboard(markers);
                    ResultNotifications.SendSuccessMessage($"Copied {markers.Count} marker(s) to clipboard!");
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
            marker.Notes = result.Notes;
            marker.IsReadOnly = result.IsReadOnly;
            marker.TemplateId = result.TemplateId;

            // If no template aka Custom, apply custom icon and scope settings.
            if (result.TemplateId == null)
            {
                marker.Scope = result.Scope;
                marker.AppliesToAllWorlds = result.AppliesToAllWorlds;
                marker.Icon = result.Icon;
                marker.GroupId = result.GroupId;

                if (oldScope != marker.Scope)
                {
                    plugin.MarkerStorageService.ChangeMarkerScope(marker, marker.Scope);
                }
                else
                {
                    plugin.MarkerStorageService.SaveMarker(marker);
                }

                plugin.MarkerStorageService.MoveMarkerToGroup(marker.Id, result.GroupId);
            }
            else
            {
                // For markers using a template, we save template ID and apply group changes
                // if group change is needed.
                var template = plugin.MarkerStorageService.FindTemplateById(result.TemplateId.Value);
                if (template != null)
                {
                    plugin.MarkerStorageService.SaveMarker(marker);
                    plugin.MarkerStorageService.MoveMarkerToGroup(marker.Id, template.GroupId);
                }
            }
        }

        /// <summary>
        /// Handles saving a group after it has been edited in the GroupEditorModal.
        /// </summary>
        private void HandleGroupSave(MarkerGroup group, bool isEditing)
        {
            if (isEditing && group.Id != Guid.Empty)
            {
                // Find the existing group
                var existing = plugin.MarkerStorageService.FindGroupById(group.Id);

                if (existing != null)
                {
                    var oldScope = existing.Scope;
                    existing.Name = group.Name;
                    existing.Scope = group.Scope;
                    existing.IsReadOnly = group.IsReadOnly;

                    // If scope changed, move between collections
                    if (oldScope != group.Scope)
                    {
                        plugin.MarkerStorageService.ChangeGroupScope(existing, group.Scope);
                    }
                    else
                    {
                        plugin.MarkerStorageService.SaveGroup(existing);
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

                plugin.MarkerStorageService.SaveGroup(newGroup);
            }
        }

        /// <summary>
        /// Handles an import result from the HeaderSection.
        /// </summary>
        private void HandleImportResult(ImportResult result, MarkerGroup? importGroup)
        {
            if (!result.Success)
            {
                ResultNotifications.SendErrorMessage($"Failed to import map markers: {result.ErrorMessage}");
            }
            else if (result.Conflicts.Count > 0)
            {
                importConflictModal.Open(result, importGroup);
            }
            else
            {
                ApplyImport(result, [], false, importGroup);
            }
        }
        private void HandleExportSelected()
        {
            var allVisible = plugin.MarkerStorageService.GetVisibleMarkers();
            var selected = allVisible.Where(m => markerTableComponent.SelectedMarkerIds.Contains(m.Id)).ToList();

            if (selected.Count == 0)
            {
                ResultNotifications.SendErrorMessage("No markers selected for export.");
                return;
            }

            MarkerExportService.ExportShareToClipboard(selected);
            ResultNotifications.SendSuccessMessage($"Copied {selected.Count} marker(s) to clipboard.");
        }

        /// <summary>
        /// Applies an import result to the storage service.
        /// Handles conflict resolution based on user choices.
        /// </summary>
        private void ApplyImport(ImportResult result, Dictionary<Guid, bool> importConflictChoices, bool overwriteAll, MarkerGroup? importGroup)
        {
            if (result.Payload == null) return;

            var existingMarkers = plugin.MarkerStorageService.GetVisibleMarkers();
            var existingById = existingMarkers.ToDictionary(m => m.Id);
            var addedMarkers = 0;
            var overwrittenMarkers = 0;

            // Apply imported markers
            foreach (var importedMarker in result.Payload.Markers)
            {
                Marker? existing = null;
                if (importedMarker.SourceId is Guid sourceId && existingById.TryGetValue(sourceId, out var match))
                    existing = match;

                if (existing != null)
                {
                    var shouldOverwrite = overwriteAll ||
                        (importConflictChoices.TryGetValue(existing.Id, out var choice) && choice);

                    if (!shouldOverwrite) continue;

                    existing.Name = importedMarker.Name;
                    existing.Position = importedMarker.Position;
                    existing.TerritoryId = importedMarker.TerritoryId;
                    existing.WardId = importedMarker.WardId;
                    existing.MapId = importedMarker.MapId;
                    existing.WorldId = importedMarker.WorldId;
                    existing.AppliesToAllWorlds = importedMarker.AppliesToAllWorlds;
                    existing.Icon = importedMarker.Icon;
                    plugin.MarkerStorageService.SaveMarker(existing);
                    overwrittenMarkers++;
                    continue;
                }

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
                    Icon = importedMarker.Icon,
                    CreatedAt = DateTime.Now,
                    Scope = MarkerScope.Personal,
                    CharacterHash = plugin.MarkerStorageService.CurrentCharacterHash,
                };

                plugin.MarkerStorageService.SaveMarker(newMarker);
                if (importGroup != null)
                    plugin.MarkerStorageService.AddMarkerToGroup(newMarker.Id, importGroup.Id);

                addedMarkers++;
            }

            if (overwrittenMarkers > 0)
            {
                var msg = $"Imported {addedMarkers} marker(s), overwritten {overwrittenMarkers} marker(s) to WukLamark.";
                ResultNotifications.SendSuccessMessage(msg, false, true);
            }
            else
            {
                var msg = $"Imported {addedMarkers} marker(s) to WukLamark.";
                ResultNotifications.SendSuccessMessage(msg, false, true);
            }

        }
        private void HandleImageUpload(string path)
        {
            // If valid, add to custom icons directory
            (var success, var message) = Plugin.CustomIconService.SavePNGToCustomIconsDir(path);
            if (!success)
            {
                ResultNotifications.SendErrorMessage($"Failed to upload custom icon: {message}");
                return;
            }
            ResultNotifications.SendSuccessMessage($"Successfully uploaded custom icon: {Path.GetFileName(path)}");
        }

        #endregion

        public void Draw()
        {
            // Draw modal dialogs
            deleteMarkerModal.Draw(plugin);
            deleteGroupModal.Draw(plugin);
            importConflictModal.Draw();
            groupEditorModal.Draw(plugin);
            customIconImageUploadModal.Draw();

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
}
