using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using System;
using System.Linq;
using System.Numerics;
using WukWaymark.Helpers;
using WukWaymark.Models;

namespace WukWaymark.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private Waymark? selectedWaymark;
    private Waymark? waymarkToDelete;
    private string editingName = string.Empty;
    private Vector4 editingColor = Vector4.One;
    private string editingNote = string.Empty;
    private WaymarkShape editingShape = WaymarkShape.Circle;
    private bool showDeleteWaypointConfirmation;

    public MainWindow(Plugin plugin)
        : base("WukWaymark - Saved Locations##MainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
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
        // Draw the delete confirmation modal if needed
        DrawDeleteWaypointModal();

        // Header section with buttons on the right
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Custom Waymark Locations");

        // Position buttons on the right side of the header
        float buttonWidth = 30.0f;
        float buttonSpacing = 5.0f;
        float totalButtonWidth = (buttonWidth * 2) + buttonSpacing + 8;
        ImGui.SameLine(ImGui.GetWindowWidth() - totalButtonWidth);

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

        // Settings button (gear icon)
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            plugin.ToggleConfigUi();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Settings");
        }

        // Main content area with scrolling
        using (var child = ImRaii.Child("WaymarkListChild", Vector2.Zero, true))
        {
            if (!child.Success) return;

            var waymarks = plugin.Configuration.Waymarks;

            if (waymarks.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No waymarks saved yet. Create one!");
                ImGui.Indent(5);
                ImGui.Text("Use '/waymark here' or the");
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.MapPin))
                {
                    plugin.WaymarkService.SaveCurrentLocation();
                }
                ImGui.SameLine();
                ImGui.Text("button above to save your current location as a waymark.");

                return;
            }

            ImGui.Text($"Total Waymarks: {waymarks.Count}");
            ImGui.Spacing();

            // Display waymarks as a table
            if (ImGui.BeginTable("WaymarkTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
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

                    // Color preview
                    ImGui.TableNextColumn();
                    var colorU32 = ImGui.ColorConvertFloat4ToU32(waymark.Color);
                    WaymarkRenderer.RenderWaymarkShape(
                        ImGui.GetWindowDrawList(),
                        ImGui.GetCursorScreenPos() + new Vector2(20, 10),
                        waymark.Shape,
                        8f,
                        colorU32
                    );
                    ImGui.Dummy(new Vector2(40, 20));

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
                        selectedWaymark = waymark;
                        editingName = waymark.Name;
                        editingColor = waymark.Color;
                        editingShape = waymark.Shape;
                        editingNote = waymark.Notes;
                        ImGui.OpenPopup($"EditWaymark##{waymark.Id}");
                    }

                    // Edit popup
                    DrawEditPopup(waymark);

                    // Flag button
                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Flag))
                    {
                        MapHelper.FlagMapLocation(waymark.Position, waymark.Name);
                    }

                    // Delete button
                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                    {
                        waymarkToDelete = waymark;
                        showDeleteWaypointConfirmation = true;
                    }
                }

                ImGui.EndTable();
            }
        }
    }
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
            var shapeIndex = (int)editingShape;
            if (ImGui.Combo($"##Shape{waymark.Id}", ref shapeIndex, "Circle\0Square\0Triangle\0Diamond\0Star\0", 5))
            {
                editingShape = (WaymarkShape)shapeIndex;
            }

            ImGui.Text("Note:");
            ImGui.SetNextItemWidth(250);
            ImGui.InputText($"##Note{waymark.Id}", ref editingNote, 100);

            ImGui.Spacing();

            if (ImGui.Button("Save"))
            {
                waymark.Name = editingName;
                waymark.Color = editingColor;
                waymark.Shape = editingShape;
                waymark.Notes = editingNote;
                plugin.Configuration.Save();
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
    private void DrawDeleteWaypointModal()
    {
        if (!showDeleteWaypointConfirmation || waymarkToDelete == null) return;

        ImGui.OpenPopup("Delete Waypoint?");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Delete Waypoint?", ref showDeleteWaypointConfirmation, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Are you sure you want to delete the waymark '{waymarkToDelete.Name}'?");
            ImGui.Text("This action cannot be undone!");

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
                plugin.Configuration.Waymarks.Remove(waymarkToDelete);
                plugin.Configuration.Save();
                showDeleteWaypointConfirmation = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, spacing);

            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                showDeleteWaypointConfirmation = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private string GetLocationName(ushort territoryId, uint worldId)
    {
        var territoryName = GetTerritoryName(territoryId);
        var worldName = GetWorldName(worldId);

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player != null && player.CurrentWorld.RowId == worldId)
            return territoryName;
        else
            return $"{territoryName} ({worldName})";
    }
    private string GetTerritoryName(ushort territoryId)
    {
        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            return territoryRow.PlaceName.Value.Name.ToString();
        }
        return $"Unknown (ID: {territoryId})";
    }
    private string GetWorldName(uint worldId)
    {
        if (Plugin.DataManager.GetExcelSheet<World>().TryGetRow(worldId, out var worldRow))
        {
            return worldRow.Name.ToString();
        }
        return $"Unknown (ID: {worldId})";
    }
}

