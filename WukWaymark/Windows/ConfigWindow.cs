using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;
using WukWaymark.Models;

namespace WukWaymark.Windows;

/// <summary>
/// Configuration window for customizing waymark display behavior and managing data.
/// </summary>
public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    /// <summary>Tracks whether the "Clear All" confirmation dialog is shown</summary>
    private bool showClearConfirmation = false;

    public ConfigWindow(Plugin plugin) : base("WukWaymark Settings###ConfigWindow")
    {
        Size = new Vector2(400, 300);
        configuration = plugin.Configuration;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Waymark Display Settings");
        ImGui.Separator();
        ImGui.Spacing();

        // Enable/Disable waymark map display
        var waymarksMapEnabled = configuration.WaymarksMapEnabled;
        if (ImGui.Checkbox("Enable Waymark Display on Map", ref waymarksMapEnabled))
        {
            configuration.WaymarksMapEnabled = waymarksMapEnabled;
            configuration.Save();
        }

        // Enable/Disable waymark minimap display
        var waymarksMinimapEnabled = configuration.WaymarksMinimapEnabled;
        if (ImGui.Checkbox("Enable Waymark Display on Minimap", ref waymarksMinimapEnabled))
        {
            configuration.WaymarksMinimapEnabled = waymarksMinimapEnabled;
            configuration.Save();
        }

        ImGui.Spacing();

        // Marker size slider
        var markerSize = configuration.WaymarkMarkerSize;
        ImGui.Text("Marker Size:");
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("##MarkerSize", ref markerSize, 4.0f, 20.0f, "%.1f"))
        {
            configuration.WaymarkMarkerSize = markerSize;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.Text("Default Shape for New Waymarks:");
        ImGui.SetNextItemWidth(200);
        var shapeIndex = (int)configuration.DefaultWaymarkShape;
        if (ImGui.Combo("##DefaultShape", ref shapeIndex, "Circle\0Square\0Triangle\0Diamond\0Star\0", 5))
        {
            configuration.DefaultWaymarkShape = (WaymarkShape)shapeIndex;
            configuration.Save();
        }

        // Show tooltips
        var showTooltips = configuration.ShowWaymarkTooltips;
        if (ImGui.Checkbox("Show Tooltips on Hover", ref showTooltips))
        {
            configuration.ShowWaymarkTooltips = showTooltips;
            configuration.Save();
        }

        ImGui.Spacing();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Data Management");
        ImGui.Spacing();

        // Clear all waymarks button with confirmation
        if (ImGui.Button("Clear All Waymarks"))
        {
            showClearConfirmation = true;
            ImGui.OpenPopup("ClearConfirmation");
        }

        // Confirmation popup
        if (ImGui.BeginPopupModal("ClearConfirmation", ref showClearConfirmation, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Are you sure you want to delete all {configuration.Waymarks.Count} waymarks?");
            ImGui.Text("This action cannot be undone!");
            ImGui.Spacing();

            if (ImGui.Button("Yes, Delete All", new Vector2(150, 0)))
            {
                configuration.Waymarks.Clear();
                configuration.Save();
                Plugin.ChatGui.Print("[WukWaymark] All waymarks have been deleted.");
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(150, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
