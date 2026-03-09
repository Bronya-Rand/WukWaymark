using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukWaymark.Helpers;
using WukWaymark.Models;
using WukWaymark.Services;

namespace WukWaymark.Windows;

/// <summary>
/// Main window for viewing and managing all saved waymarks.
/// Supports two view modes: Table View (flat list) and Group View (collapsible groups).
/// </summary>
public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    // ═══════════════════════════════════════════════════════════════
    // UI STATE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Search filter text for filtering waymarks by name/notes/territory.</summary>
    private string searchFilter = string.Empty;

    /// <summary>When true, only show waymarks in the current zone.</summary>
    private bool filterCurrentZone;

    /// <summary>Waymark pending deletion (confirmation pending)</summary>
    private Waymark? waymarkToDelete;

    /// <summary>Tracks whether the delete confirmation dialog is displayed</summary>
    private bool showDeleteWaymarkConfirmation;

    // ═══════════════════════════════════════════════════════════════
    // WAYMARK EDITING STATE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Name being edited in the current edit session</summary>
    private string editingName = string.Empty;

    /// <summary>Color being edited in the current edit session</summary>
    private Vector4 editingColor = Vector4.One;

    /// <summary>Notes being edited in the current edit session</summary>
    private string editingNote = string.Empty;

    /// <summary>Shape being edited in the current edit session</summary>
    private WaymarkShape editingShape = WaymarkShape.Circle;

    /// <summary>Group assignment being edited</summary>
    private Guid? editingGroupId;

    /// <summary>Visibility radius being edited</summary>
    private float editingVisibilityRadius;

    /// <summary>Icon ID being edited</summary>
    private uint? editingIconId = null;

    /// <summary>Search filter for the icon picker dropdown</summary>
    private string editingIconSearch = string.Empty;
    private WaymarkScope editingScope = WaymarkScope.Personal;

    /// <summary>Whether the icon picker modal is open</summary>
    private bool showIconPickerModal = false;

    // ═══════════════════════════════════════════════════════════════
    // GROUP EDITING STATE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Name for new/editing group</summary>
    private string groupEditName = string.Empty;

    /// <summary>Whether the create group popup should open</summary>
    private bool showCreateGroupPopup;

    /// <summary>Whether the group editor modal is currently open</summary>
    private bool groupEditorOpen;

    /// <summary>Group being edited (null = creating new)</summary>
    private WaymarkGroup? editingGroup;

    /// <summary>Whether the delete group confirmation dialog is shown</summary>
    private bool showDeleteGroupConfirmation;

    /// <summary>Group pending deletion</summary>
    private WaymarkGroup? groupToDelete;

    /// <summary>Whether to keep waymarks when deleting a group</summary>
    private bool keepWaymarksOnGroupDelete = true;

    // ═══════════════════════════════════════════════════════════════
    // IMPORT / EXPORT STATE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Pending import result (populated after clipboard/file parse, waits for conflict resolution)</summary>
    private ImportResult? pendingImport;

    /// <summary>Whether the import conflict resolution modal is displayed</summary>
    private bool showImportConflictModal;

    /// <summary>Per-conflict user decision: true = overwrite, false = skip</summary>
    private readonly Dictionary<Guid, bool> importConflictChoices = [];

    /// <summary>Feedback message to show after import (success/error string)</summary>
    private string importFeedback = string.Empty;

    /// <summary>Ticks remaining to display import feedback banner</summary>
    private int importFeedbackTicks;

    public MainWindow(Plugin plugin)
        : base("WukWaymark - Saved Locations##WWMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public override void Draw()
    {
        // Draw modal dialogs
        DrawDeleteWaypointModal();
        DrawImportConflictModal();

        // Import feedback banner
        if (importFeedbackTicks > 0)
        {
            importFeedbackTicks--;
            ImGui.TextColored(new Vector4(0.2f, 1f, 0.4f, 1f), importFeedback);
        }

        // Header
        DrawHeader();

        // Search bar and filters
        DrawSearchBar();

        // Main content area with scrolling
        using var child = ImRaii.Child("WaymarkListChild", Vector2.Zero, true);
        ImGui.Separator();

        var visibleWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();
        var waymarksToRender = plugin.Configuration.UseGroupView
            ? visibleWaymarks
            : FilterWaymarks(visibleWaymarks);

        if (!child.Success) return;

        if (waymarksToRender.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        // Draw the appropriate view
        if (plugin.Configuration.UseGroupView)
            DrawGroupView(waymarksToRender);
        else
            DrawTableView(waymarksToRender);
    }

    // ═══════════════════════════════════════════════════════════════
    // HEADER
    // ═══════════════════════════════════════════════════════════════

    private void DrawHeader()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Custom Waymark Locations");

        // Position buttons on the right side of the header
        var buttonWidth = 30.0f;
        var buttonSpacing = 5.0f;
        var totalButtonWidth = (buttonWidth * 4) + (buttonSpacing * 3) + 8;
        ImGui.SameLine(ImGui.GetWindowWidth() - totalButtonWidth);

        // Import from clipboard
        if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport))
        {
            var result = WaymarkExportService.ImportFromClipboard(
                plugin.Configuration.Waymarks,
                plugin.Configuration.WaymarkGroups);

            if (!result.Success)
            {
                importFeedback = $"Import failed: {result.ErrorMessage}";
                importFeedbackTicks = 240;
            }
            else if (result.Conflicts.Count > 0)
            {
                // Show conflict resolution modal
                pendingImport = result;
                importConflictChoices.Clear();
                foreach (var c in result.Conflicts)
                    importConflictChoices[c.Id] = false; // default: skip
                showImportConflictModal = true;
                ImGui.OpenPopup("Import Conflicts");
            }
            else
            {
                // No conflicts - apply directly
                ApplyImport(result, overwriteAll: false);
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Import Waymarks from Clipboard");
        }

        ImGui.SameLine(0, buttonSpacing);

        // Save Location button (pin icon)
        if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
        {
            plugin.WaymarkService.SaveCurrentLocation();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Save Current Location");
        }

        ImGui.SameLine(0, buttonSpacing);

        // View toggle button
        var viewIcon = plugin.Configuration.UseGroupView ? FontAwesomeIcon.List : FontAwesomeIcon.FolderOpen;
        var viewTooltip = plugin.Configuration.UseGroupView ? "Switch to Table View" : "Switch to Group View";
        if (ImGuiComponents.IconButton(viewIcon))
        {
            plugin.Configuration.UseGroupView = !plugin.Configuration.UseGroupView;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(viewTooltip);
        }

        ImGui.SameLine(0, buttonSpacing);

        // Settings button (gear icon)
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            plugin.ToggleConfigUi();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Settings");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SEARCH BAR & FILTERS
    // ═══════════════════════════════════════════════════════════════

    private void DrawSearchBar()
    {
        // Search input
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 160);
        ImGui.InputTextWithHint("##Search", "Search waymarks...", ref searchFilter, 200);

        ImGui.SameLine();

        // Zone filter toggle
        ImGui.Checkbox("Current Zone", ref filterCurrentZone);

        // Undo button (only when deletions exist)
        if (plugin.WaymarkService.CanUndo)
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
            {
                plugin.WaymarkService.UndoDelete();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Undo Delete ({plugin.WaymarkService.UndoCount})");
            }
        }

        ImGui.Spacing();
    }

    // ═══════════════════════════════════════════════════════════════
    // TABLE VIEW
    // ═══════════════════════════════════════════════════════════════

    private void DrawTableView(List<Waymark> waymarks)
    {
        var filteredWaymarks = FilterWaymarks(waymarks);

        ImGui.Text($"Showing {filteredWaymarks.Count} of {waymarks.Count} waymarks");
        ImGui.Spacing();

        DrawWaymarkTable("WaymarkTableAll", filteredWaymarks);
    }

    // ═══════════════════════════════════════════════════════════════
    // GROUP VIEW
    // ═══════════════════════════════════════════════════════════════

    private void DrawGroupView(List<Waymark> waymarks)
    {
        var groups = plugin.Configuration.WaymarkGroups;

        // Draw create group popup if needed
        DrawCreateGroupPopup();
        DrawDeleteGroupConfirmation();

        // "+ New Group" button
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus))
        {
            groupEditName = string.Empty;
            showCreateGroupPopup = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Create New Group");
        }

        ImGui.Spacing();

        // Render each group as a collapsible header
        foreach (var group in groups.ToList())
        {
            var groupWaymarks = FilterWaymarks(waymarks.Where(w => w.GroupId == group.Id).ToList());
            if (groupWaymarks.Count == 0 && filterCurrentZone) continue; // Skip empty groups that are not relevant to current zone filter

            // If searching and no waymarks match AND group name doesn't match, hide this group
            if (!string.IsNullOrEmpty(searchFilter))
            {
                var groupNameMatches = group.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase);
                if (groupWaymarks.Count == 0 && !groupNameMatches)
                    continue;
            }

            using var groupId = ImRaii.PushId(group.Id.ToString());

            // Group header with collapsing
            var headerOpen = ImGui.CollapsingHeader($"{group.Name} ({groupWaymarks.Count})###group_{group.Id}", ImGuiTreeNodeFlags.AllowItemOverlap);

            // Right-aligned buttons on the header line
            DrawGroupHeaderButtons(group);

            if (headerOpen)
            {
                if (groupWaymarks.Count == 0)
                {
                    using (ImRaii.PushIndent(10))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No waymarks in this group.");
                    }
                }
                else
                {
                    DrawWaymarkTable($"GroupTable##{group.Id}", groupWaymarks);
                }
                ImGui.Spacing();
            }
        }

        // Ungrouped waymarks section
        var ungroupedWaymarks = FilterWaymarks(waymarks.Where(w => w.GroupId == null).ToList());

        if (!string.IsNullOrEmpty(searchFilter) && ungroupedWaymarks.Count == 0)
            return; // Hide ungrouped section when search yields no results

        if (ungroupedWaymarks.Count > 0 || string.IsNullOrEmpty(searchFilter))
        {
            var ungroupedOpen = ImGui.CollapsingHeader($"Ungrouped ({ungroupedWaymarks.Count})###ungrouped", ImGuiTreeNodeFlags.DefaultOpen);
            if (ungroupedOpen)
            {
                if (ungroupedWaymarks.Count == 0)
                {
                    using (ImRaii.PushIndent(10))
                    {

                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No ungrouped waymarks.");
                    }
                }
                else
                {
                    DrawWaymarkTable("GroupTableUngrouped", ungroupedWaymarks);
                }
            }
        }
    }

    private void DrawGroupHeaderButtons(WaymarkGroup group)
    {
        // Calculate right-aligned position for buttons
        var buttonSize = 20.0f * ImGuiHelpers.GlobalScale;
        var spacing = 5.0f;
        var totalWidth = (buttonSize * 3) + (spacing * 2) + 8;

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - totalWidth + ImGui.GetCursorPosX());

        // Quick-save to this group
        using (ImRaii.PushId("groupsave"))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
            {
                plugin.WaymarkService.SaveCurrentLocation(group.Id);
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
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit))
            {
                groupEditName = group.Name;
                editingGroup = group;
                showCreateGroupPopup = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Edit Group");
            }
        }

        ImGui.SameLine(0, spacing);

        // Delete group
        using (ImRaii.PushId("groupdelete"))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                groupToDelete = group;
                showDeleteGroupConfirmation = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Delete Group");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // SHARED WAYMARK TABLE
    // ═══════════════════════════════════════════════════════════════

    private void DrawWaymarkTable(string tableId, List<Waymark> waymarks)
    {
        if (ImGui.BeginTable(tableId, 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Marker", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 100);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch, 140);
            ImGui.TableSetupColumn("Created", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableHeadersRow();

            foreach (var waymark in waymarks.ToList())
            {
                using var rowId = ImRaii.PushId(waymark.Id.ToString());
                ImGui.TableNextRow();

                // Marker preview (icon or shape)
                ImGui.TableNextColumn();
                var colorU32 = ImGui.ColorConvertFloat4ToU32(waymark.Color);
                var globalScale = ImGuiHelpers.GlobalScale;
                WaymarkRenderer.RenderWaymark(
                    ImGui.GetWindowDrawList(),
                    ImGui.GetCursorScreenPos() + new Vector2(20 * globalScale, 10 * globalScale),
                    waymark.Shape,
                    8f * globalScale,
                    colorU32,
                    waymark.IconId
                );
                ImGui.Dummy(new Vector2(40 * globalScale, 20 * globalScale));

                // Name
                ImGui.TableNextColumn();
                ImGui.Text(waymark.Name);
                if (waymark.Notes.Length > 0)
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text(waymark.Notes);
                        ImGui.EndTooltip();
                    }

                // Location
                ImGui.TableNextColumn();
                var locationText = GetLocationName(waymark.TerritoryId, waymark.WorldId);
                ImGui.Text(locationText);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Position: X: {waymark.Position.X:F2}, Y: {waymark.Position.Y:F2}, Z: {waymark.Position.Z:F2}");
                    ImGui.Text($"Territory ID: {waymark.TerritoryId}");
                    ImGui.Text($"Map ID: {waymark.MapId}");
                    ImGui.Text($"World ID: {waymark.WorldId}");
                    ImGui.EndTooltip();
                }

                // Created timestamp
                ImGui.TableNextColumn();
                ImGui.Text(waymark.CreatedAt.ToString("yyyy-MM-dd HH:mm"));

                // Actions
                ImGui.TableNextColumn();

                // Edit button
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit))
                {
                    editingName = waymark.Name;
                    editingColor = waymark.Color;
                    editingShape = waymark.Shape;
                    editingNote = waymark.Notes;
                    editingGroupId = waymark.GroupId;
                    editingVisibilityRadius = waymark.VisibilityRadius;
                    editingIconId = waymark.IconId;
                    editingScope = waymark.Scope;
                    ImGui.OpenPopup($"EditWaymark##{waymark.Id}");
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Edit Waymark");
                }

                // Edit popup
                DrawEditPopup(waymark);

                // Flag button
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Flag))
                {
                    MapHelper.FlagMapLocation(waymark.Position, waymark.TerritoryId, waymark.MapId, waymark.Name);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Flag Location on Map");
                }

                // Delete button
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                {
                    waymarkToDelete = waymark;
                    showDeleteWaymarkConfirmation = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Delete Waymark");
                }

                // Export to clipboard button (single waymark)
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Clipboard))
                {
                    WaymarkExportService.ExportToClipboard(waymark);
                    importFeedback = $"Copied '{waymark.Name}' to clipboard!";
                    importFeedbackTicks = 180;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Copy to Clipboard");
                }
            }

            ImGui.EndTable();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // FILTERING
    // ═══════════════════════════════════════════════════════════════

    private List<Waymark> FilterWaymarks(List<Waymark> waymarks)
    {
        var filtered = waymarks.AsEnumerable();

        // Zone filter
        if (filterCurrentZone)
        {
            var currentMapId = Plugin.ClientState.MapId;
            filtered = filtered.Where(w => w.MapId == currentMapId);
        }

        // Text search filter
        if (!string.IsNullOrEmpty(searchFilter))
        {
            filtered = filtered.Where(w =>
                w.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                w.Notes.Contains(searchFilter, StringComparison.OrdinalIgnoreCase) ||
                GetTerritoryName(w.TerritoryId).Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
            );
        }

        return filtered.ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // EDIT POPUP
    // ═══════════════════════════════════════════════════════════════

    private void DrawEditPopup(Waymark waymark)
    {
        if (ImGui.BeginPopup($"EditWaymark##{waymark.Id}"))
        {
            ImGui.Text($"Edit Waymark");
            ImGui.Separator();

            ImGui.Text("Name:");
            ImGui.SetNextItemWidth(250);
            ImGui.InputText($"##Name{waymark.Id}", ref editingName, 100);

            ImGui.Text("Color:");
            ImGui.SetNextItemWidth(250);
            ImGui.ColorEdit4($"##Color{waymark.Id}", ref editingColor);

            ImGui.Text("Shape:");
            ImGui.SetNextItemWidth(250);
            var shapeDropPreview = Enum.GetName(editingShape) ?? "Unknown";
            using (var shapeDrop = ImRaii.Combo($"##Shape{waymark.Id}", shapeDropPreview))
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
            var groups = plugin.Configuration.WaymarkGroups;
            var currentGroupName = editingGroupId == null
                ? "Ungrouped"
                : groups.FirstOrDefault(g => g.Id == editingGroupId)?.Name ?? "Unknown";
            using (var groupDrop = ImRaii.Combo($"##Group{waymark.Id}", currentGroupName))
            {
                if (groupDrop.Success)
                {
                    // "Ungrouped" option
                    if (ImGui.Selectable("Ungrouped", editingGroupId == null))
                    {
                        editingGroupId = null;
                    }

                    // Group options
                    foreach (var group in groups)
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
            ImGui.InputText($"##Note{waymark.Id}", ref editingNote, 100);

            // Scope dropdown
            ImGui.Text("Scope:");
            ImGui.SetNextItemWidth(250);
            var scopeDropPreview = Enum.GetName(editingScope) ?? "Unknown";
            using (var scopeDrop = ImRaii.Combo($"##Scope{waymark.Id}", scopeDropPreview))
            {
                if (scopeDrop.Success)
                {
                    if (ImGui.Selectable(WaymarkScope.Personal.ToString(), editingScope == WaymarkScope.Personal))
                        editingScope = WaymarkScope.Personal;
                    if (ImGui.Selectable(WaymarkScope.Shared.ToString(), editingScope == WaymarkScope.Shared))
                        editingScope = WaymarkScope.Shared;
                }
            }

            // Visibility radius slider
            ImGui.Text("Visibility Radius:");
            ImGui.SetNextItemWidth(250);
            ImGui.SliderFloat($"##VisRadius{waymark.Id}", ref editingVisibilityRadius, 0f, 500f, editingVisibilityRadius == 0 ? "Always Visible" : "%.0f yalms");

            // Icon picker
            ImGui.Text("Icon:");
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
            if (ImGui.Button($"{currentIconName}##IconBtn{waymark.Id}", new Vector2(previewTex != null ? 218 : 250, 0)))
            {
                showIconPickerModal = true;
                ImGui.OpenPopup($"Icon Picker ({waymark.Name})###{waymark.Id}");
            }

            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);

            if (ImGui.BeginPopupModal($"Icon Picker ({waymark.Name})###{waymark.Id}", ref showIconPickerModal, ImGuiWindowFlags.NoSavedSettings))
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
                        string[] categories = ["Map", "Item", "Action", "Status", "Macro"];
                        string[] categoryNames = ["Map Symbols", "Items", "Actions", "Status Effects", "Macros"];

                        for (var c = 0; c < categories.Length; c++)
                        {
                            if (ImGui.BeginTabItem(categoryNames[c]))
                            {
                                DrawIconGrid(plugin.IconBrowserService.AvailableIcons, categories[c], editingIconSearch);
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

                if (oldScope != waymark.Scope)
                {
                    // Move between lists if scope changed
                    if (waymark.Scope == WaymarkScope.Personal)
                    {
                        plugin.WaymarkStorageService.SharedWaymarks.Remove(waymark);
                        waymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        plugin.Configuration.Waymarks.Add(waymark);
                    }
                    else
                    {
                        plugin.Configuration.Waymarks.Remove(waymark);
                        waymark.CharacterHash = null;
                        plugin.WaymarkStorageService.SharedWaymarks.Add(waymark);
                    }
                }

                plugin.Configuration.Save();
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
        var filteredIcons = allIcons
            .Where(i => i.Source == category)
            .Where(i => string.IsNullOrEmpty(searchLower) ||
                        i.Name.ToLowerInvariant().Contains(searchLower) ||
                        i.IconId.ToString().Contains(searchLower))
            .Take(200) // Cap results to prevent UI freezing
            .ToList();

        if (filteredIcons.Count == 0)
        {
            ImGui.TextDisabled("No matching icons found.");
            return;
        }

        if (ImGui.BeginChild($"IconGrid_{category}", new Vector2(0, -1), true)) // -1 stretch to bottom
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
                    var tex = Plugin.TextureProvider.GetFromGameIcon(icon.IconId).GetWrapOrEmpty();
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
            ImGui.EndChild();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // IMPORT CONFLICT MODAL
    // ═══════════════════════════════════════════════════════════════

    private void DrawImportConflictModal()
    {
        if (!showImportConflictModal || pendingImport == null) return;

        ImGui.OpenPopup("Import Conflicts");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(480, 0), ImGuiCond.Always);

        if (ImGui.BeginPopupModal("Import Conflicts", ref showImportConflictModal, ImGuiWindowFlags.AlwaysAutoResize))
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

            ImGui.EndPopup();
        }
    }

    private void ApplyImport(ImportResult result, bool overwriteAll)
    {
        if (result.Payload == null) return;

        var config = plugin.Configuration;
        var conflictIds = new HashSet<Guid>(result.Conflicts.Select(c => c.Id));
        var addedWaymarks = 0;
        var addedGroups = 0;

        // Apply groups first (waymarks may reference them)
        foreach (var importedGroup in result.Payload.Groups)
        {
            if (conflictIds.Contains(importedGroup.Id))
            {
                var shouldOverwrite = overwriteAll || (importConflictChoices.TryGetValue(importedGroup.Id, out var v) && v);
                if (shouldOverwrite)
                {
                    var existing = config.WaymarkGroups.FirstOrDefault(g => g.Id == importedGroup.Id);
                    if (existing != null)
                    {
                        existing.Name = importedGroup.Name;
                        existing.IconId = importedGroup.IconId;
                        existing.Scope = importedGroup.Scope;
                        addedGroups++;
                    }
                }
                // else skip
            }
            else
            {
                config.WaymarkGroups.Add(importedGroup);
                addedGroups++;
            }
        }

        // Apply waymarks
        foreach (var importedWaymark in result.Payload.Waymarks)
        {
            var isShared = importedWaymark.Scope == WaymarkScope.Shared;
            if (conflictIds.Contains(importedWaymark.Id))
            {
                var shouldOverwrite = overwriteAll || (importConflictChoices.TryGetValue(importedWaymark.Id, out var v) && v);
                if (shouldOverwrite)
                {
                    // Remove entirely from both first to safely overwrite
                    config.Waymarks.RemoveAll(w => w.Id == importedWaymark.Id);
                    plugin.WaymarkStorageService.SharedWaymarks.RemoveAll(w => w.Id == importedWaymark.Id);

                    if (isShared)
                    {
                        importedWaymark.CharacterHash = null;
                        plugin.WaymarkStorageService.SharedWaymarks.Add(importedWaymark);
                    }
                    else
                    {
                        importedWaymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                        config.Waymarks.Add(importedWaymark);
                    }
                    addedWaymarks++;
                }
                // else skip
            }
            else
            {
                if (isShared)
                {
                    importedWaymark.CharacterHash = null;
                    plugin.WaymarkStorageService.SharedWaymarks.Add(importedWaymark);
                }
                else
                {
                    importedWaymark.CharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                    config.Waymarks.Add(importedWaymark);
                }
                addedWaymarks++;
            }
        }

        config.Save();
        plugin.WaymarkStorageService.SaveSharedWaymarks();
        importFeedback = $"Imported {addedWaymarks} waymark(s) and {addedGroups} group(s).";
        importFeedbackTicks = 240;
        Plugin.Log.Information(importFeedback);
    }

    // ═══════════════════════════════════════════════════════════════

    private void DrawDeleteWaypointModal()
    {
        if (!showDeleteWaymarkConfirmation || waymarkToDelete == null) return;

        ImGui.OpenPopup("Delete Waypoint?");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Delete Waypoint?", ref showDeleteWaymarkConfirmation, ImGuiWindowFlags.AlwaysAutoResize))
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

            ImGui.EndPopup();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // GROUP CRUD
    // ═══════════════════════════════════════════════════════════════

    private void DrawCreateGroupPopup()
    {
        if (showCreateGroupPopup)
        {
            ImGui.OpenPopup("GroupEditor");
            showCreateGroupPopup = false;
            groupEditorOpen = true;
        }

        if (!groupEditorOpen) return;

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("GroupEditor", ref groupEditorOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var isEditing = editingGroup != null;
            ImGui.Text(isEditing ? "Edit Group" : "Create New Group");
            ImGui.Separator();

            ImGui.Text("Group Name:");
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("##GroupName", ref groupEditName, 100);

            ImGui.Spacing();

            if (ImGui.Button("Save"))
            {
                if (!string.IsNullOrWhiteSpace(groupEditName))
                {
                    if (isEditing)
                    {
                        editingGroup!.Name = groupEditName;
                    }
                    else
                    {
                        plugin.Configuration.WaymarkGroups.Add(new WaymarkGroup
                        {
                            Name = groupEditName
                        });
                    }
                    plugin.Configuration.Save();
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

    private void DrawDeleteGroupConfirmation()
    {
        if (!showDeleteGroupConfirmation || groupToDelete == null) return;

        ImGui.OpenPopup("Delete Group?");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Delete Group?", ref showDeleteGroupConfirmation, ImGuiWindowFlags.AlwaysAutoResize))
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
                    plugin.Configuration.Save();
                    plugin.WaymarkStorageService.SaveSharedWaymarks();
                }

                plugin.Configuration.WaymarkGroups.Remove(groupToDelete);
                plugin.Configuration.Save();
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

            ImGui.EndPopup();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // EMPTY STATE
    // ═══════════════════════════════════════════════════════════════

    private void DrawEmptyState()
    {
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No waymarks saved yet. Create one!");
        ImGui.Indent(5);
        ImGui.Text("Use '/wwmark here' or the");
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
        {
            plugin.WaymarkService.SaveCurrentLocation();
        }
        ImGui.SameLine();
        ImGui.Text("button above to save your current location as a waymark.");
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static string GetLocationName(ushort territoryId, uint worldId)
    {
        var territoryName = GetTerritoryName(territoryId);
        var worldName = GetWorldName(worldId);

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player != null && player.CurrentWorld.RowId == worldId)
            return territoryName;
        else
            return $"{territoryName} ({worldName})";
    }

    private static string GetTerritoryName(ushort territoryId)
    {
        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            return territoryRow.PlaceName.Value.Name.ToString();
        }
        return $"Unknown (ID: {territoryId})";
    }

    private static string GetWorldName(uint worldId)
    {
        if (Plugin.DataManager.GetExcelSheet<World>().TryGetRow(worldId, out var worldRow))
        {
            return worldRow.Name.ToString();
        }
        return $"Unknown (ID: {worldId})";
    }
}
