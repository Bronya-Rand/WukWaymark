using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using WukWaymark.Models;
using System.Linq;

namespace WukWaymark.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private Waymark? selectedWaymark;
    private string editingName = string.Empty;
    private Vector4 editingColor = Vector4.One;

    public MainWindow(Plugin plugin, string goatImagePath)
        : base("WukWaymark - Saved Locations##MainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Header section
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Custom Waymark Locations");
        ImGui.Separator();

        // Quick save button
        if (ImGui.Button("Save Current Location"))
        {
            SaveCurrentLocation();
        }

        ImGui.SameLine();

        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }

        ImGui.Spacing();

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
            if (ImGui.BeginTable("WaymarkTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
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

    private void SaveCurrentLocation()
    {
        var player = Plugin.ClientState.LocalPlayer;
        if (player == null)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] You must be logged in to save a waymark.");
            return;
        }

        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current location.");
            return;
        }

        uint mapId = 0;
        if (Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            mapId = territoryRow.Map.RowId;
        }

        var waymark = new Waymark
        {
            Position = player.Position,
            TerritoryId = territoryId,
            MapId = mapId,
            Name = $"Waymark {plugin.Configuration.Waymarks.Count + 1}",
            CreatedAt = DateTime.Now
        };

        plugin.Configuration.Waymarks.Add(waymark);
        plugin.Configuration.Save();

        Plugin.ChatGui.Print($"[WukWaymark] Saved waymark '{waymark.Name}' at current location.");
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

