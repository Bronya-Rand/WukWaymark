using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
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

    #region Callback Actions
    public Action<Marker>? OnDeleteRequested { get; set; }
    public Action<Marker>? OnFlagRequested { get; set; }
    public Action<Marker>? OnExportRequested { get; set; }
    public Action<Marker, MarkerEditResult>? OnSaveRequested { get; set; }

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
        using var markerTableMode = ImRaii.Table("MarkerTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable);
        if (!markerTableMode) return;

        ImGui.TableSetupColumn("Marker", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 100);
        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch, 160);
        ImGui.TableSetupColumn("Created", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();

        foreach (var marker in markers)
        {
            using (ImRaii.PushId(marker.Id.ToString()))
            {
                ImGui.TableNextRow();

                DrawMarkerColumn(marker);
                DrawNameColumn(marker);
                DrawLocationColumn(marker);
                DrawCreatedColumn(marker);
                DrawActionsColumn(marker, parentGroup);
            }
        }
    }

    private static void DrawMarkerColumn(Marker marker)
    {
        ImGui.TableNextColumn();
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
        ImGui.TableNextColumn();

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
        ImGui.TableNextColumn();
        var locationText = LocationHelper.GetLocationName(marker.TerritoryId, marker.WorldId, marker.WardId);
        ImGui.Text(locationText);
        if (ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            if (tooltip)
            {
                ImGui.Text($"Position: X: {marker.Position.X:F2}, Y: {marker.Position.Y:F2}, Z: {marker.Position.Z:F2}");
                ImGui.Text($"Territory ID: {marker.TerritoryId}");
                ImGui.Text($"Map ID: {marker.MapId}");
                ImGui.Text($"World ID: {marker.WorldId}");
                if (marker.WardId != -1)
                    ImGui.Text($"Ward ID: {marker.WardId}");
            }
        }
    }

    private static void DrawCreatedColumn(Marker marker)
    {
        ImGui.TableNextColumn();
        ImGui.Text(marker.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
    }

    private void DrawActionsColumn(Marker marker, MarkerGroup? parentGroup)
    {
        ImGui.TableNextColumn();

        var isLoggedIn = gameStateReaderService.IsLoggedIn;
        var currentHash = plugin.MarkerStorageService.CurrentCharacterHash;
        var isMarkerCreator = marker.CharacterHash != null &&
                        currentHash != null &&
                        marker.CharacterHash == currentHash;

        var canEdit = false;
        var canDelete = false;

        if (parentGroup != null)
        {
            var isGroupCreator = parentGroup.CreatorHash != null && currentHash != null && parentGroup.CreatorHash == currentHash;

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
                    canEdit = isMarkerCreator;
                    canDelete = false;
                }
                else
                {
                    // Shared non-read-only markers: editable by anyone unless the marker itself is read-only.
                    canEdit = !marker.IsReadOnly || isMarkerCreator;
                    canDelete = !marker.IsReadOnly;
                }
            }
        }
        else
        {
            // Only creators of personal markers may edit/delete them or
            // shared non-read-only markers are editable by anyone.
            canEdit = (marker.Scope == MarkerScope.Shared && (!marker.IsReadOnly || isMarkerCreator)) ||
                        (marker.Scope == MarkerScope.Personal && isMarkerCreator);

            canDelete = (marker.Scope == MarkerScope.Shared && !marker.IsReadOnly) ||
                        (marker.Scope == MarkerScope.Personal && isMarkerCreator);
        }

        // Edit button
        using (ImRaii.Disabled(!canEdit))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Edit))
            {
                editPopup.LoadFromMarker(marker);
                ImGui.OpenPopup($"EditMarker##{marker.Id}");
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = parentGroup != null && parentGroup.Scope == MarkerScope.Personal && !canEdit ? "Only the group's creator can edit markers in this personal group." :
                          parentGroup != null && parentGroup.Scope == MarkerScope.Shared && parentGroup.IsReadOnly ? "This group is read-only. Only the marker creator can edit it." :
                          parentGroup != null && parentGroup.Scope == MarkerScope.Shared && marker.IsReadOnly && !canEdit ? "This shared marker is read-only and only the creator can edit it." :
                          parentGroup == null && !canEdit && marker.Scope == MarkerScope.Personal ? "Only the creator can edit this marker." :
                          parentGroup == null && !canEdit && marker.Scope == MarkerScope.Shared && marker.IsReadOnly ? "This shared marker is read-only and cannot be edited." :
                          "Edit Marker";
            ImGui.SetTooltip(tooltip);
        }

        // Edit popup
        editPopup.Draw(marker, parentGroup);

        // Flag button
        ImGui.SameLine();
        using (ImRaii.Disabled(!isLoggedIn))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Flag))
            {
                OnFlagRequested?.Invoke(marker);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to flag markers!" :
                          "Flag Location on Map";
            ImGui.SetTooltip(tooltip);
        }

        // Export to clipboard button
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Clipboard))
        {
            OnExportRequested?.Invoke(marker);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Copy to Clipboard");
        }

        // Delete button
        ImGui.SameLine();
        using (ImRaii.Disabled(!canDelete))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                OnDeleteRequested?.Invoke(marker);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = parentGroup != null && parentGroup.Scope == MarkerScope.Personal && !canDelete ? "Only the group's creator can delete markers in this personal group." :
                          parentGroup != null && parentGroup.Scope == MarkerScope.Shared && parentGroup.IsReadOnly ? "This group is read-only and markers cannot be deleted." :
                          parentGroup != null && parentGroup.Scope == MarkerScope.Shared && marker.IsReadOnly && !canDelete ? "This shared marker is read-only and cannot be deleted." :
                          parentGroup == null && !canDelete && marker.Scope == MarkerScope.Personal ? "Only the creator can delete this marker." :
                          parentGroup == null && !canDelete && marker.Scope == MarkerScope.Shared && marker.IsReadOnly ? "This shared marker is read-only and cannot be deleted." :
                          "Delete Marker";

            ImGui.SetTooltip(tooltip);
        }
    }
}
