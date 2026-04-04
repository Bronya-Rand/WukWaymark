using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;
using WukLamark.Models;

namespace WukLamark.Windows;

/// <summary>
/// Configuration window for customizing marker display behavior and managing data.
/// </summary>
public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;

    /// <summary>Tracks whether the "Clear All" confirmation dialog is shown</summary>
    private bool showClearConfirmation = false;

    public ConfigWindow(Plugin plugin) : base("WukLamark Settings##WWSettings")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 450),
            MaximumSize = new Vector2(450, 550)
        };
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.0f, 1.0f), "Map Marker Display Settings");
        ImGui.Separator();
        ImGui.Spacing();

        // Enable/Disable marker map display
        var markersMapEnabled = configuration.WaymarksMapEnabled;
        if (ImGui.Checkbox("Enable Marker Display on Map", ref markersMapEnabled))
        {
            configuration.WaymarksMapEnabled = markersMapEnabled;
            configuration.Save();
        }

        // Enable/Disable marker minimap display
        var markersMinimapEnabled = configuration.WaymarksMinimapEnabled;
        if (ImGui.Checkbox("Enable Marker Display on Minimap", ref markersMinimapEnabled))
        {
            configuration.WaymarksMinimapEnabled = markersMinimapEnabled;
            configuration.Save();
        }

        // Enable/Disable marker display on Aethernet map overlay (TelepotTown)
        var markersAethernetMapEnabled = configuration.ShowMarkersOnAethernet;
        if (ImGui.Checkbox("Show Markers on Teleport (Aethernet) Map", ref markersAethernetMapEnabled))
        {
            configuration.ShowMarkersOnAethernet = markersAethernetMapEnabled;
            configuration.Save();
        }

        ImGui.Spacing();

        // Marker size slider
        var markerSize = configuration.WaymarkMarkerSize;
        ImGui.Text("Marker Size:");
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderFloat("##MarkerSize", ref markerSize, 4.0f, 20.0f, "%.1f"))
        {
            configuration.WaymarkMarkerSize = markerSize;
            configuration.Save();
        }

        ImGui.Spacing();

        // Minimap edge fading
        var fadeOnMinimapEdge = configuration.FadeWaymarkOnMinimapEdge;
        if (ImGui.Checkbox("Fade Markers on Minimap Edge", ref fadeOnMinimapEdge))
        {
            configuration.FadeWaymarkOnMinimapEdge = fadeOnMinimapEdge;
            configuration.Save();
        }

        // Map edge fading
        var fadeOnMapEdge = configuration.FadeWaymarkOnMapEdge;
        if (ImGui.Checkbox("Fade Markers on Map Edge", ref fadeOnMapEdge))
        {
            configuration.FadeWaymarkOnMapEdge = fadeOnMapEdge;
            configuration.Save();
        }

        using (ImRaii.Disabled(!fadeOnMapEdge && !fadeOnMinimapEdge))
        {
            var edgeFadeAlpha = configuration.MapEdgeFadeAlpha;
            ImGui.Text("Edge Fade Opacity:");
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.SliderFloat("##EdgeFadeAlpha", ref edgeFadeAlpha, 0.3f, 1.0f, "%.2f"))
            {
                configuration.MapEdgeFadeAlpha = edgeFadeAlpha;
                configuration.Save();
            }
        }

        ImGui.Spacing();

        ImGui.Text("Default Shape for New Markers:");
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        var shapeIndex = (int)configuration.DefaultWaymarkShape;
        if (ImGui.Combo("##DefaultShape", ref shapeIndex, "Circle\0Square\0Triangle\0Diamond\0Star\0", 5))
        {
            configuration.DefaultWaymarkShape = (MarkerShape)shapeIndex;
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

        // Clear all markers button with confirmation
        if (ImGui.Button("Erase All Created Markers"))
        {
            showClearConfirmation = true;
            ImGui.OpenPopup("Erase All Created Markers##WWClearConfirmation");
        }

        // Confirmation popup
        using var eraseMarkersModal = ImRaii.PopupModal("Erase All Created Markers##WWClearConfirmation", ref showClearConfirmation, ImGuiWindowFlags.AlwaysAutoResize);
        if (eraseMarkersModal)
        {
            var totalMarkers = plugin.MarkerStorageService.PersonalMarkers.Count +
                               plugin.MarkerStorageService.GetSharedCreatedMarkersCount();
            ImGui.Text($"Are you sure you want to delete all {totalMarkers} markers?");
            ImGui.Text("This action cannot be undone!");
            ImGui.Spacing();

            if (ImGui.Button("Yes, Delete All", new Vector2(150, 0)))
            {
                plugin.MarkerStorageService.PersonalMarkers.Clear();
                plugin.MarkerStorageService.EraseCreatedSharedMarkers();
                plugin.MarkerStorageService.SavePersonalMarkers();
                plugin.MarkerStorageService.SaveSharedMarkers();
                Plugin.ChatGui.Print("[WukLamark] All markers have been deleted.");
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(150, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
        }
    }
}
