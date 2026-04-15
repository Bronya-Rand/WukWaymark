using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Services;
using WukLamark.Utils;

namespace WukLamark.Windows.Components;

internal class MarkerTableComponent
{
    private readonly Plugin plugin;
    private readonly GameStateReaderService gameStateReaderService;
    private readonly MarkerEditPopup editPopup;

    #region Selection State
    private Marker? pendingEditMarker;
    private MarkerGroup? pendingEditParentGroup;
    private bool pendingEditPopupOpenRequested;

    private int selectionAnchorIndex = -1;
    public HashSet<Guid> SelectedMarkerIds { get; } = [];
    public bool IsMultiSelect => SelectedMarkerIds.Count > 1;
    #endregion

    #region Callback Actions
    public Action<Marker, MarkerEditResult>? OnSaveRequested { get; set; }
    public Action<Marker>? OnFlagRequested { get; set; }
    public Action<List<Marker>>? OnExportRequested { get; set; }
    public Action<List<Marker>>? OnDeleteRequested { get; set; }
    #endregion

    public MarkerTableComponent(Plugin plugin, GameStateReaderService gameStateReaderService)
    {
        this.plugin = plugin;
        this.gameStateReaderService = gameStateReaderService;
        editPopup = new MarkerEditPopup(plugin)
        {
            OnSave = (marker, result) => OnSaveRequested?.Invoke(marker, result)
        };
    }

    public void Draw(List<Marker> markers, MarkerGroup? parentGroup = null)
    {
        using var markerTableMode = ImRaii.Table("MarkerTable", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable);
        if (!markerTableMode) return;

        ImGui.TableSetupColumn("Marker", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 100);
        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch, 160);
        ImGui.TableSetupColumn("Created", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();

        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];

            using (ImRaii.PushId(marker.Id.ToString()))
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                var rowStart = ImGui.GetCursorScreenPos();
                var rowHeight = Math.Max(ImGui.GetFrameHeight(), 20f * ImGuiHelpers.GlobalScale);

                var isSelected = SelectedMarkerIds.Contains(marker.Id);
                var rowClicked = ImGui.Selectable($"##row_{marker.Id}",
                    isSelected,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowItemOverlap,
                    new Vector2(0, rowHeight));

                ImGui.SetItemAllowOverlap();

                ImGui.OpenPopupOnItemClick($"MarkerContextMenu##{marker.Id}", ImGuiPopupFlags.MouseButtonRight);
                using (var popup = ImRaii.Popup($"MarkerContextMenu##{marker.Id}"))
                {
                    if (popup)
                        if (IsMultiSelect)
                            DrawMultiMarkerContextMenu();
                        else
                            DrawMarkerContextMenu(marker, parentGroup);
                }

                // Reset cursor so selectable acts as an overlay.
                ImGui.SetCursorScreenPos(rowStart);

                if (rowClicked)
                    HandleRowSelection(markers, i);

                if (SelectedMarkerIds.Contains(marker.Id) && IsMultiSelect)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.ButtonActive));

                DrawMarkerColumn(marker);
                DrawNameColumn(marker);
                DrawLocationColumn(marker);
                DrawCreatedColumn(marker);
            }
        }

        if (pendingEditMarker != null)
        {
            var popupId = $"EditMarker##{pendingEditMarker.Id}";

            if (pendingEditPopupOpenRequested)
            {
                ImGui.OpenPopup(popupId);
                pendingEditPopupOpenRequested = false;
            }
            editPopup.Draw(pendingEditMarker, pendingEditParentGroup);

            if (!ImGui.IsPopupOpen(popupId))
            {
                pendingEditMarker = null;
                pendingEditParentGroup = null;
                pendingEditPopupOpenRequested = false;
            }
        }
    }

    private static void DrawMarkerColumn(Marker marker)
    {
        ImGui.TableSetColumnIndex(0);

        var colorU32 = ImGui.ColorConvertFloat4ToU32(marker.Color);
        var globalScale = ImGuiHelpers.GlobalScale;
        MarkerRenderer.RenderMarker(
            ImGui.GetWindowDrawList(),
            ImGui.GetCursorScreenPos() + new Vector2(20 * globalScale, 10 * globalScale),
            marker.Shape,
            8f * globalScale,
            colorU32,
            marker.IconId
        );
        ImGui.Dummy(new Vector2(40 * globalScale, 20 * globalScale));
    }

    private static void DrawNameColumn(Marker marker)
    {
        ImGui.TableSetColumnIndex(1);

        if (marker.GroupId == null)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                var icon = marker.Scope == MarkerScope.Personal ? FontAwesomeIcon.EyeSlash
                    : marker.Scope == MarkerScope.Shared && marker.IsReadOnly ? FontAwesomeIcon.Lock
                    : marker.Scope == MarkerScope.Shared ? FontAwesomeIcon.Users : FontAwesomeIcon.Question;
                ImGui.TextDisabled(icon.ToIconString());
            }
            if (ImGui.IsItemHovered())
            {
                var tooltip = marker.Scope == MarkerScope.Personal ? "Personal Marker" :
                              marker.Scope == MarkerScope.Shared && marker.IsReadOnly ? "Shared Read-Only Marker" :
                              marker.Scope == MarkerScope.Shared ? "Shared Marker" :
                              "Unknown Scope Marker";
                ImGui.SetTooltip(tooltip);
            }
            ImGui.SameLine();
        }
        ImGui.Text(marker.Name);

        if (marker.Notes.Length > 0)
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(marker.Notes);
    }

    private static void DrawLocationColumn(Marker marker)
    {
        ImGui.TableSetColumnIndex(2);

        var locationText = LocationHelper.GetLocationName(marker.TerritoryId, marker.WorldId, marker.WardId, marker.AppliesToAllWorlds);
        ImGui.Text(locationText);
        if (ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            if (tooltip)
            {
                ImGui.Text($"Position: X: {marker.Position.X:F2}, Y: {marker.Position.Y:F2}, Z: {marker.Position.Z:F2}");
                ImGui.Text($"Territory ID: {marker.TerritoryId}");
                ImGui.Text($"Map ID: {marker.MapId}");
                ImGui.Text(marker.AppliesToAllWorlds ? "World ID: All Worlds/Data Centers" : $"World ID: {marker.WorldId}");
                if (marker.WardId != -1)
                    ImGui.Text($"Ward ID: {marker.WardId}");
            }
        }
    }

    private static void DrawCreatedColumn(Marker marker)
    {
        ImGui.TableSetColumnIndex(3);
        ImGui.Text(marker.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
    }

    /// <summary>
    /// Single-select context menu for individual marker actions. For multi-select actions, see <see cref="DrawMultiMarkerContextMenu"/>.
    /// </summary>
    /// <param name="marker"></param>
    private void DrawMarkerContextMenu(Marker marker, MarkerGroup? parentGroup)
    {
        if (IsMultiSelect) return;

        GetPermissionsForMarker(marker, parentGroup, out var isLoggedIn, out var isCreator, out var canEdit, out var canDelete);

        using (ImRaii.Disabled(!canEdit))
            if (ImGui.MenuItem("Edit Marker"))
            {
                pendingEditMarker = marker;
                pendingEditParentGroup = parentGroup;
                pendingEditPopupOpenRequested = true;
                editPopup.LoadFromMarker(marker);
            }
        if (!canEdit)
        {
            var tooltip = GetEditMarkerTooltipText(marker, parentGroup);
            if (ImWuk.IsItemHoveredWhenDisabled() && !tooltip.IsNullOrEmpty())
                ImGui.SetTooltip(tooltip);
        }

        using (ImRaii.Disabled(!isLoggedIn))
        {
            if (ImGui.MenuItem("Flag Marker on Map"))
                OnFlagRequested?.Invoke(marker);
        }

        if (ImGui.MenuItem("Copy to Clipboard"))
        {
            var temp = new List<Marker> { marker };
            OnExportRequested?.Invoke(temp);
        }

        using (ImRaii.Disabled(!canDelete))
        {
            if (ImGui.MenuItem("Delete"))
            {
                var temp = new List<Marker> { marker };
                OnDeleteRequested?.Invoke(temp);
            }
        }
        if (!canDelete)
        {
            var tooltip = GetDeleteMarkerTooltipText(marker, parentGroup);
            if (ImWuk.IsItemHoveredWhenDisabled() && !tooltip.IsNullOrEmpty())
                ImGui.SetTooltip(tooltip);
        }
    }

    /// <summary>
    /// Multi-select context menu for batch marker actions. For individual marker actions, see <see cref="DrawMarkerContextMenu"/>.
    /// </summary>
    private void DrawMultiMarkerContextMenu()
    {
        if (!IsMultiSelect) return;

        if (ImGui.MenuItem("Export All"))
        {
            var allMarkers = plugin.MarkerStorageService.GetVisibleMarkers();
            var markersToExport = allMarkers.FindAll(m => SelectedMarkerIds.Contains(m.Id));
            OnExportRequested?.Invoke(markersToExport);
        }
        if (ImGui.MenuItem("Delete All"))
        {
            var allMarkers = plugin.MarkerStorageService.GetVisibleMarkers();
            var markersToDelete = allMarkers.FindAll(m => SelectedMarkerIds.Contains(m.Id));
            OnDeleteRequested?.Invoke(markersToDelete);
        }
    }
    private void HandleRowSelection(List<Marker> markers, int clickedIndex)
    {
        var io = ImGui.GetIO();
        var clickedId = markers[clickedIndex].Id;

        // Shift actions.
        if (io.KeyShift && selectionAnchorIndex >= 0)
        {
            SelectedMarkerIds.Clear();

            var start = Math.Min(selectionAnchorIndex, clickedIndex);
            var end = Math.Max(selectionAnchorIndex, clickedIndex);

            for (var i = start; i <= end; i++)
                SelectedMarkerIds.Add(markers[i].Id);
            return;
        }

        // Ctrl actions
        if (io.KeyCtrl)
        {
            if (SelectedMarkerIds.Contains(clickedId))
                SelectedMarkerIds.Remove(clickedId);
            else
                SelectedMarkerIds.Add(clickedId);

            selectionAnchorIndex = clickedIndex;
            return;
        }

        SelectedMarkerIds.Clear();
        SelectedMarkerIds.Add(clickedId);
        selectionAnchorIndex = clickedIndex;
    }
    private void GetPermissionsForMarker(Marker marker, MarkerGroup? parentGroup, out bool isLoggedIn, out bool isCreator, out bool canEdit, out bool canDelete)
    {
        var currentPlayerHash = plugin.MarkerStorageService.CurrentCharacterHash;

        isLoggedIn = gameStateReaderService.IsLoggedIn;
        isCreator = marker.CharacterHash != null &&
                    currentPlayerHash != null &&
                    marker.CharacterHash == currentPlayerHash;
        canEdit = false;
        canDelete = false;

        // Group permissions checks
        if (parentGroup != null)
        {
            var isGroupCreator = parentGroup.CreatorHash != null && currentPlayerHash != null && parentGroup.CreatorHash == currentPlayerHash;

            // Only creators of personal groups may edit/delete them
            if (parentGroup.Scope == MarkerScope.Personal)
            {
                canEdit = isGroupCreator;
                canDelete = isGroupCreator;
            }
            else if (parentGroup.Scope == MarkerScope.Shared)
            {
                // Shared read-only groups: only marker creators can open edit to disable read-only.
                if (parentGroup.IsReadOnly)
                {
                    canEdit = isCreator;
                    canDelete = false;
                }
                else
                {
                    // Shared non-read-only markers: editable by anyone unless the marker itself is read-only.
                    canEdit = !marker.IsReadOnly || isCreator;
                    canDelete = !marker.IsReadOnly;
                }
            }
        }
        else
        {
            // Marker permission checks
            canEdit = (marker.Scope == MarkerScope.Shared && (!marker.IsReadOnly || isCreator)) ||
                        (marker.Scope == MarkerScope.Personal && isCreator);

            canDelete = (marker.Scope == MarkerScope.Shared && !marker.IsReadOnly) ||
                        (marker.Scope == MarkerScope.Personal && isCreator);
        }
    }
    private static string GetEditMarkerTooltipText(Marker marker, MarkerGroup? parentGroup)
    {
        if (parentGroup != null && parentGroup.Scope == MarkerScope.Personal)
            return "Only the group's creator can edit markers in this personal group.";
        if (parentGroup != null && parentGroup.Scope == MarkerScope.Shared && parentGroup.IsReadOnly)
            return "This group is read-only. Only the marker creator can edit it.";
        if (parentGroup != null && parentGroup.Scope == MarkerScope.Shared && marker.IsReadOnly)
            return "This shared marker is read-only and only the creator can edit it.";
        if (parentGroup == null && marker.Scope == MarkerScope.Personal)
            return "Only the creator can edit this marker.";
        if (parentGroup == null && marker.Scope == MarkerScope.Shared && marker.IsReadOnly)
            return "This shared marker is read-only and cannot be edited.";
        return "";
    }
    private static string GetDeleteMarkerTooltipText(Marker marker, MarkerGroup? parentGroup)
    {
        if (parentGroup != null && parentGroup.Scope == MarkerScope.Personal)
            return "Only the group's creator can delete markers in this personal group.";
        if (parentGroup != null && parentGroup.Scope == MarkerScope.Shared && parentGroup.IsReadOnly)
            return "This group is read-only and markers cannot be deleted.";
        if (parentGroup != null && parentGroup.Scope == MarkerScope.Shared && marker.IsReadOnly)
            return "This shared marker is read-only and cannot be deleted.";
        if (parentGroup == null && marker.Scope == MarkerScope.Personal)
            return "Only the creator can delete this marker.";
        if (parentGroup == null && marker.Scope == MarkerScope.Shared && marker.IsReadOnly)
            return "This shared marker is read-only and cannot be deleted.";
        return "";
    }
}
