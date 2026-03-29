using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Numerics;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Components;

internal class WaymarkTableComponent
{
    private readonly Plugin plugin;
    private readonly WaymarkEditPopup editPopup;

    #region Callback Actions
    public Action<Waymark>? OnDeleteRequested { get; set; }
    public Action<Waymark>? OnFlagRequested { get; set; }
    public Action<Waymark>? OnExportRequested { get; set; }
    public Action<Waymark, WaymarkEditResult>? OnSaveRequested { get; set; }

    #endregion

    public WaymarkTableComponent(Plugin plugin)
    {
        this.plugin = plugin;
        editPopup = new WaymarkEditPopup(plugin)
        {
            OnSave = (waymark, result) => OnSaveRequested?.Invoke(waymark, result)
        };
    }

    public void Draw(List<Waymark> waymarks)
    {
        using var waymarkTableMode = ImRaii.Table("WaymarkTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable);
        if (!waymarkTableMode) return;

        ImGui.TableSetupColumn("Marker", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 100);
        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch, 160);
        ImGui.TableSetupColumn("Created", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableHeadersRow();

        foreach (var waymark in waymarks)
        {
            using (ImRaii.PushId(waymark.Id.ToString()))
            {
                ImGui.TableNextRow();

                DrawMarkerColumn(waymark);
                DrawNameColumn(waymark);
                DrawLocationColumn(waymark);
                DrawCreatedColumn(waymark);
                DrawActionsColumn(waymark);
            }
        }
    }

    private static void DrawMarkerColumn(Waymark waymark)
    {
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
    }

    private static void DrawNameColumn(Waymark waymark)
    {
        ImGui.TableNextColumn();
        ImGui.Text(waymark.Name);
        if (waymark.Notes.Length > 0)
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(waymark.Notes);
    }

    private static void DrawLocationColumn(Waymark waymark)
    {
        ImGui.TableNextColumn();
        var locationText = LocationHelper.GetLocationName(waymark.TerritoryId, waymark.WorldId, waymark.WardId);
        ImGui.Text(locationText);
        if (ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            if (tooltip)
            {
                ImGui.Text($"Position: X: {waymark.Position.X:F2}, Y: {waymark.Position.Y:F2}, Z: {waymark.Position.Z:F2}");
                ImGui.Text($"Territory ID: {waymark.TerritoryId}");
                ImGui.Text($"Map ID: {waymark.MapId}");
                ImGui.Text($"World ID: {waymark.WorldId}");
                if (waymark.WardId != -1)
                    ImGui.Text($"Ward ID: {waymark.WardId}");
            }
        }
    }

    private static void DrawCreatedColumn(Waymark waymark)
    {
        ImGui.TableNextColumn();
        ImGui.Text(waymark.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
    }

    private void DrawActionsColumn(Waymark waymark)
    {
        ImGui.TableNextColumn();

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
                editPopup.LoadFromWaymark(waymark);
                ImGui.OpenPopup($"EditWaymark##{waymark.Id.ToString()}");
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !canEdit && waymark.Scope == WaymarkScope.Personal ? "Only the creator can edit this waymark." :
                          !canEdit && waymark.Scope == WaymarkScope.Shared && waymark.IsReadOnly ? "This shared waymark is read-only and cannot be edited." :
                          "Edit Waymark";
            ImGui.SetTooltip(tooltip);
        }

        // Edit popup
        editPopup.Draw(waymark);

        // Flag button
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Flag))
        {
            OnFlagRequested?.Invoke(waymark);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Flag Location on Map");
        }

        // Export to clipboard button
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Clipboard))
        {
            OnExportRequested?.Invoke(waymark);
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
                OnDeleteRequested?.Invoke(waymark);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !canDelete && waymark.Scope == WaymarkScope.Personal ? "Only the creator can delete this waymark." :
                !canDelete && waymark.Scope == WaymarkScope.Shared && waymark.IsReadOnly ? "This shared waymark is read-only and cannot be deleted." :
                          "Delete Waymark";

            ImGui.SetTooltip(tooltip);
        }
    }
}
