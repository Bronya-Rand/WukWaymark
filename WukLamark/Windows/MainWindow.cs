using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Windows;

/// <summary>
/// Main window for viewing and managing all saved waymarks.
/// Supports two view modes: Table View (flat list) and Group View (collapsible groups).
/// </summary>
public partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    // ═══════════════════════════════════════════════════════════════
    // GAME STATE
    // ═══════════════════════════════════════════════════════════════
    private bool isLoggedIn => plugin.GameStateReaderService.IsLoggedIn;
    private bool inPvP => plugin.GameStateReaderService.IsInPvP;
    private bool inCombat => plugin.GameStateReaderService.IsInCombat;
    private bool waymarksDisabled => plugin.GameStateReaderService.DisableWaymarkActions();

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

    /// <summary>
    /// Whether to make the waymark read-only (only applies to shared waymarks)
    /// </summary>
    private bool editingReadOnly = false;

    /// <summary>Whether the icon picker modal is open</summary>
    private bool showIconPickerModal = false;

    // ═══════════════════════════════════════════════════════════════
    // GROUP EDITING STATE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Name for new/editing group</summary>
    private string groupEditName = string.Empty;

    /// <summary>Scope being edited for the group</summary>
    private WaymarkScope groupEditScope = WaymarkScope.Personal;

    /// <summary>Read-only flag being edited for the group</summary>
    private bool groupEditIsReadOnly = false;

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
        : base("WukLamark - Saved Locations##WWMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
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

        var visibleWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();

        if (!child.Success) return;

        if (visibleWaymarks.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        // Draw the appropriate view
        if (plugin.Configuration.UseGroupView)
            DrawGroupView(visibleWaymarks);
        else
            DrawTableView(visibleWaymarks);
    }

    // ═══════════════════════════════════════════════════════════════
    // HEADER
    // ═══════════════════════════════════════════════════════════════

    private void DrawHeader()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Custom Waymark Locations");

        // Position buttons on the right side of the header
        var buttonWidth = ImGui.GetFrameHeight();
        var buttonSpacing = 5.0f * ImGuiHelpers.GlobalScale;
        var totalButtonWidth = (buttonWidth * 4) + (buttonSpacing * 4);
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - totalButtonWidth);

        // Import from clipboard
        using (ImRaii.Disabled(!isLoggedIn))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.FileImport))
            {
                var allKnownWaymarks = plugin.WaymarkStorageService.GetVisibleWaymarks();
                var allKnownGroups = plugin.WaymarkStorageService.GetVisibleGroups();

                var result = WaymarkExportService.ImportFromClipboard(
                    allKnownWaymarks,
                    allKnownGroups);

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
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to import waymarks!" :
                          "Import waymarks from clipboard";
            ImGui.SetTooltip(tooltip);
        }
        ImGui.SameLine(0, buttonSpacing);

        // Save Location button (pin icon)
        using (ImRaii.Disabled(waymarksDisabled))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
            {
                plugin.WaymarkService.SaveCurrentLocation();
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to save waymarks" :
                          inPvP ? "Saving waymarks is disabled in PvP zones" :
                          inCombat ? "Saving waymarks is disabled in combat" :
                          "Save Current Location as Waymark";
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1.0f);
            ImGui.SetTooltip(tooltip);
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
        // Calculate the width of the elements that will be placed to the right of the search bar
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        // "Current Zone" checkbox width
        // A checkbox is composed of the square frame + inner spacing + text width
        var checkboxTextWidth = ImGui.CalcTextSize("Current Zone").X;
        var checkboxFramePadded = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        var checkboxTotalWidth = checkboxFramePadded + checkboxTextWidth;

        var rightElementsWidth = checkboxTotalWidth + spacing;

        // Undo button width
        if (plugin.WaymarkService.CanUndo)
        {
            var undoButtonWidth = ImGui.GetFrameHeight(); // Icon buttons are square based on frame height
            rightElementsWidth += undoButtonWidth + spacing;
        }

        // Search input
        ImGui.SetNextItemWidth(Math.Max(50, ImGui.GetContentRegionAvail().X - rightElementsWidth));
        ImGui.InputTextWithHint("##Search", "Search waymarks...", ref searchFilter, 200);

        ImGui.SameLine();

        // Zone filter toggle
        using (ImRaii.Disabled(waymarksDisabled))
            ImGui.Checkbox("Current Zone", ref filterCurrentZone);
        if (ImWuk.IsItemHoveredWhenDisabled())
            ImGui.SetTooltip(!isLoggedIn ? "Log in to filter waymarks!" :
                             inPvP ? "Filtering waymarks is disabled in PvP zones" :
                             inCombat ? "Filtering waymarks is disabled in combat" :
                             "Only show waymarks in the current zone");

        // Undo button (only when deletions exist)
        if (plugin.WaymarkService.CanUndo)
        {
            ImGui.SameLine();
            using (ImRaii.Disabled(!isLoggedIn))
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
                {
                    plugin.WaymarkService.UndoDelete();
                }
            }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = !isLoggedIn ? "Log in to undo deletions!" :
                              $"Undo Deleted Waymark ({plugin.WaymarkService.UndoCount})";
                ImGui.SetTooltip(tooltip);
            }
        }

        ImGui.Spacing();
    }

    // ═══════════════════════════════════════════════════════════════
    // EMPTY STATE
    // ═══════════════════════════════════════════════════════════════

    private void DrawEmptyState()
    {
        var coloredText = "No waymarks saved yet. Create one!";
        if (!isLoggedIn)
            coloredText = "Log in first to save waymarks!";
        var coloredTextSize = ImGui.CalcTextSize(coloredText);
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X - coloredTextSize.X) / 2);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), coloredText);
        if (isLoggedIn)
        {
            ImGui.Indent(5);
            ImGui.Text("Use '/wlmark here' or the");
            ImGui.SameLine();
            using (ImRaii.Disabled(waymarksDisabled))
                if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
                {
                    plugin.WaymarkService.SaveCurrentLocation();
                }
            if (ImWuk.IsItemHoveredWhenDisabled())
            {
                var tooltip = inPvP ? "Saving waymarks is disabled in PvP zones" :
                              inCombat ? "Saving waymarks is disabled in combat" :
                              "Save Current Location as Waymark";
                ImGui.SetTooltip(tooltip);
            }
            ImGui.SameLine();
            ImGui.Text("button above to save your current location as a waymark.");
        }
    }
}
