using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukWaymark.Helpers;
using WukWaymark.Models;
using WukWaymark.Services;

namespace WukWaymark.Windows;

public partial class MainWindow
{
    private void DrawTableView(IEnumerable<Waymark> waymarks)
    {
        var filteredWaymarks = FilterWaymarks(waymarks).ToList();

        ImGui.Text($"Showing {filteredWaymarks.Count} of {waymarks.Count()} waymarks");
        ImGui.Spacing();

        DrawWaymarkTable(filteredWaymarks);
    }

    private void DrawWaymarkTable(IEnumerable<Waymark> waymarks)
    {
        // Use a persistent numeric ID for the table instead of string. Assuming we just need one table active at a time, or passing an explicit ID.
        // For multiple tables per frame (e.g. group view), we will use ImGui.PushID around this method call from the caller.
        if (ImGui.BeginTable("WaymarkTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Marker", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 100);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch, 140);
            ImGui.TableSetupColumn("Created", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableHeadersRow();

            foreach (var waymark in waymarks)
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

                // Check if this waymark can be edited by the current user
                var currentCharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
                var isCreator = waymark.CharacterHash != null &&
                                currentCharacterHash != null &&
                                waymark.CharacterHash == currentCharacterHash;

                var canEdit = (waymark.Scope == WaymarkScope.Shared && (!waymark.IsReadOnly || isCreator)) ||
                              (waymark.Scope == WaymarkScope.Personal && isCreator);

                var canDelete = (waymark.Scope == WaymarkScope.Shared && !waymark.IsReadOnly) ||
                                (waymark.Scope == WaymarkScope.Personal && isCreator);

                // Edit button
                using (ImRaii.Disabled(!canEdit))
                {
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
                        editingReadOnly = waymark.IsReadOnly;
                        ImGui.OpenPopup($"EditWaymark##{waymark.Id.ToString()}");
                    }
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

                // Delete button
                ImGui.SameLine();
                using (ImRaii.Disabled(!canDelete))
                {
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                    {
                        waymarkToDelete = waymark;
                        showDeleteWaymarkConfirmation = true;
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Delete Waymark");
                }
            }

            ImGui.EndTable();
        }
    }
}
