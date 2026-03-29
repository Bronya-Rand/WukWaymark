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

internal class WaymarkTableComponent
{
    private readonly Plugin plugin;
    private readonly GameStateReaderService gameStateReaderService;
    private readonly WaymarkEditPopup editPopup;

    #region Callback Actions
    public Action<Waymark>? OnDeleteRequested { get; set; }
    public Action<Waymark>? OnFlagRequested { get; set; }
    public Action<Waymark>? OnExportRequested { get; set; }
    public Action<Waymark, WaymarkEditResult>? OnSaveRequested { get; set; }

    #endregion

    public WaymarkTableComponent(Plugin plugin, GameStateReaderService gameStateReaderService)
    {
        this.plugin = plugin;
        this.gameStateReaderService = gameStateReaderService;
        editPopup = new WaymarkEditPopup(plugin)
        {
            OnSave = (waymark, result) => OnSaveRequested?.Invoke(waymark, result)
        };
    }

    public void Draw(List<Waymark> waymarks, WaymarkGroup? parentGroup = null)
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
                DrawActionsColumn(waymark, parentGroup);
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

    private void DrawActionsColumn(Waymark waymark, WaymarkGroup? parentGroup)
    {
        ImGui.TableNextColumn();

        var isLoggedIn = gameStateReaderService.IsLoggedIn;
        var currentCharacterHash = plugin.WaymarkStorageService.CurrentCharacterHash;
        var isWaymarkCreator = waymark.CharacterHash != null &&
                        currentCharacterHash != null &&
                        waymark.CharacterHash == currentCharacterHash;

        var canEdit = false;
        var canDelete = false;

        if (parentGroup != null)
        {
            var isGroupCreator = parentGroup.CreatorHash != null && currentCharacterHash != null && parentGroup.CreatorHash == currentCharacterHash;

            // Only creators of personal groups may edit/delete them
            if (parentGroup.Scope == WaymarkScope.Personal)
            {
                canEdit = isGroupCreator;
                canDelete = isGroupCreator;
            }
            else if (parentGroup.Scope == WaymarkScope.Shared)
            {
                // No one can edit/delete a waymark to a shared, read only group.
                if (parentGroup.IsReadOnly)
                {
                    canEdit = false;
                    canDelete = false;
                }
                else
                {
                    // Any person can edit/delete any waymarks added to it (even if said waymarks are not made by them)
                    canEdit = true;
                    canDelete = true;
                }
            }
        }
        else
        {
            // Only creators of personal waymarks may edit/delete them or
            // Creators of shared waymarks may delete them if they are not read-only or
            // Any person can edit waymarks in a shared group if they are not read-only
            canEdit = (waymark.Scope == WaymarkScope.Shared && (!waymark.IsReadOnly || isWaymarkCreator)) ||
                      (waymark.Scope == WaymarkScope.Personal && isWaymarkCreator);

            canDelete = (waymark.Scope == WaymarkScope.Shared && !waymark.IsReadOnly) ||
                        (waymark.Scope == WaymarkScope.Personal && isWaymarkCreator);
        }

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
            var tooltip = parentGroup != null && parentGroup.Scope == WaymarkScope.Personal && !canEdit ? "Only the group's creator can edit waymarks in this personal group." :
                          parentGroup != null && parentGroup.Scope == WaymarkScope.Shared && parentGroup.IsReadOnly ? "This group is read-only and waymarks cannot be edited." :
                          parentGroup == null && !canEdit && waymark.Scope == WaymarkScope.Personal ? "Only the creator can edit this waymark." :
                          parentGroup == null && !canEdit && waymark.Scope == WaymarkScope.Shared && waymark.IsReadOnly ? "This shared waymark is read-only and cannot be edited." :
                          "Edit Waymark";
            ImGui.SetTooltip(tooltip);
        }

        // Edit popup
        editPopup.Draw(waymark);

        // Flag button
        ImGui.SameLine();
        using (ImRaii.Disabled(!isLoggedIn))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Flag))
            {
                OnFlagRequested?.Invoke(waymark);
            }
        }
        if (ImWuk.IsItemHoveredWhenDisabled())
        {
            var tooltip = !isLoggedIn ? "Log in to flag waymarks!" :
                          "Flag Location on Map";
            ImGui.SetTooltip(tooltip);
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
            var tooltip = parentGroup != null && parentGroup.Scope == WaymarkScope.Personal && !canDelete ? "Only the group's creator can delete waymarks in this personal group." :
                          parentGroup != null && parentGroup.Scope == WaymarkScope.Shared && parentGroup.IsReadOnly ? "This group is read-only and waymarks cannot be deleted." :
                          parentGroup == null && !canDelete && waymark.Scope == WaymarkScope.Personal ? "Only the creator can delete this waymark." :
                          parentGroup == null && !canDelete && waymark.Scope == WaymarkScope.Shared && waymark.IsReadOnly ? "This shared waymark is read-only and cannot be deleted." :
                          "Delete Waymark";

            ImGui.SetTooltip(tooltip);
        }
    }
}
