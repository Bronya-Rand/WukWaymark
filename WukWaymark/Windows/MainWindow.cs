using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using System;
using System.Linq;
using System.Numerics;
using WukWaymark.Models;

namespace WukWaymark.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private Waymark? selectedWaymark;
    private string editingName = string.Empty;
    private Vector4 editingColor = Vector4.One;

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
        // Header section with buttons on the right
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Custom Waymark Locations");

        // Position buttons on the right side of the header
        float buttonWidth = 30.0f;
        float buttonSpacing = 5.0f;
        float totalButtonWidth = (buttonWidth * 2) + buttonSpacing + 8; // +8 for padding
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

        ImGui.Separator();

        // Main content area with scrolling
        using (var child = ImRaii.Child("WaymarkListChild", Vector2.Zero, true))
        {
            if (!child.Success) return;

            var waymarks = plugin.Configuration.Waymarks;

            if (waymarks.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No waymarks saved yet.");
                ImGui.Text("Use '/waymark here' or the button above to save your current location.");
                return;
            }

            ImGui.Text($"Total Waymarks: {waymarks.Count}");
            ImGui.Spacing();

            // Display waymarks as a table
            if (ImGui.BeginTable("WaymarkTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Created", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("##Delete", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableHeadersRow();

                Waymark? waymarkToDelete = null;

                foreach (var waymark in waymarks.ToList())
                {
                    ImGui.TableNextRow();

                    // Color preview
                    ImGui.TableNextColumn();
                    var colorU32 = ImGui.ColorConvertFloat4ToU32(waymark.Color);
                    ImGui.GetWindowDrawList().AddCircleFilled(
                        ImGui.GetCursorScreenPos() + new Vector2(20, 10),
                        8f,
                        colorU32
                    );
                    ImGui.Dummy(new Vector2(40, 20));

                    // Name
                    ImGui.TableNextColumn();
                    ImGui.Text(waymark.Name);

                    // Location
                    ImGui.TableNextColumn();
                    var locationText = GetLocationName(waymark.TerritoryId);
                    ImGui.Text(locationText);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Position: X: {waymark.Position.X:F2}, Y: {waymark.Position.Y:F2}, Z: {waymark.Position.Z:F2}");
                        ImGui.Text($"Territory ID: {waymark.TerritoryId}");
                        ImGui.Text($"Map ID: {waymark.MapId}");
                        ImGui.EndTooltip();
                    }

                    // Created timestamp
                    ImGui.TableNextColumn();
                    ImGui.Text(waymark.CreatedAt.ToString("yyyy-MM-dd HH:mm"));

                    // Actions
                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Edit##{waymark.Id}"))
                    {
                        selectedWaymark = waymark;
                        editingName = waymark.Name;
                        editingColor = waymark.Color;
                        ImGui.OpenPopup($"EditWaymark##{waymark.Id}");
                    }

                    // Edit popup
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

                        ImGui.Spacing();

                        if (ImGui.Button("Save"))
                        {
                            waymark.Name = editingName;
                            waymark.Color = editingColor;
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

                    // Delete button
                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete##{waymark.Id}"))
                    {
                        waymarkToDelete = waymark;
                    }
                }

                // Process deletion outside the loop to avoid collection modification issues
                if (waymarkToDelete != null)
                {
                    plugin.Configuration.Waymarks.Remove(waymarkToDelete);
                    plugin.Configuration.Save();
                }

                ImGui.EndTable();
            }
        }
    }
    private string GetLocationName(ushort territoryId)
    {
        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            return territoryRow.PlaceName.Value.Name.ToString();
        }
        return $"Unknown (ID: {territoryId})";
    }
}

