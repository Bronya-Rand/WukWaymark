using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using WukLamark.Models;
using WukLamark.Utils;

namespace WukLamark.Windows.Tabs.Settings
{
    internal sealed class SettingsTab(Plugin plugin)
    {
        private readonly Configuration configuration = plugin.Configuration;
        private readonly Plugin plugin = plugin;

        private const float DefaultMapMarkerSize = 8.0f;

        /// <summary>Tracks whether the "Clear All" confirmation dialog is shown</summary>
        private bool showClearConfirmation = false;

        public void Draw()
        {
            var avail = ImGui.GetContentRegionAvail();

            using (var child = ImRaii.Child("##SettingsTabChild", avail))
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

                ImGui.Spacing();

                // Map marker size slider (for Map)
                var mapMarkerSize = configuration.MapMarkerMapSize;
                ImGui.Text("Default Marker Size on Map:");
                ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
                if (ImGui.SliderFloat("###MapMarkerSize", ref mapMarkerSize, 1.0f, 24.0f, "%.1f"))
                {
                    if (mapMarkerSize < 1.0f) mapMarkerSize = 1.0f;
                    if (mapMarkerSize > 24.0f) mapMarkerSize = 24.0f;

                    configuration.MapMarkerMapSize = mapMarkerSize;
                    configuration.Save();
                }
                ImGui.SameLine();
                // Reset to default button for map marker size
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
                    configuration.MapMarkerMapSize = DefaultMapMarkerSize; // Default size
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Reset to default size");

                ImGui.Spacing();

                // Map marker size slider (for Minimap)
                var minimapMarkerSize = configuration.MapMarkerMinimapSize;
                ImGui.Text("Default Marker Size on Minimap:");
                ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
                if (ImGui.SliderFloat("###MinimapMarkerSize", ref minimapMarkerSize, 1.0f, 24.0f, "%.1f"))
                {
                    if (minimapMarkerSize < 1.0f) minimapMarkerSize = 1.0f;
                    if (minimapMarkerSize > 24.0f) minimapMarkerSize = 24.0f;

                    configuration.MapMarkerMinimapSize = minimapMarkerSize;
                    configuration.Save();
                }
                ImGui.SameLine();
                // Reset to default button for map marker size
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Undo))
                    configuration.MapMarkerMinimapSize = DefaultMapMarkerSize; // Default size
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Reset to default size");

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
                    if (ImGui.SliderFloat("###EdgeFadeAlpha", ref edgeFadeAlpha, 0.3f, 1.0f, "%.2f"))
                    {
                        if (edgeFadeAlpha < 0.3f) edgeFadeAlpha = 0.3f;
                        if (edgeFadeAlpha > 1.0f) edgeFadeAlpha = 1.0f;

                        configuration.MapEdgeFadeAlpha = edgeFadeAlpha;
                        configuration.Save();
                    }
                }

                ImGui.Spacing();

                ImGui.Text("Default Shape for New Markers:");
                ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
                var shapeIndex = (int)configuration.DefaultWaymarkShape;
                if (ImGui.Combo("###DefaultShape", ref shapeIndex, "Circle\0Square\0Triangle\0Diamond\0Star\0", 5))
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
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Data Management");
                ImGui.Spacing();

                // Clear all markers button with confirmation
                if (ImGui.Button("Erase All Created Markers"))
                {
                    showClearConfirmation = true;
                    ImGui.OpenPopup("Erase All Created Markers###WWClearConfirmation");
                }

                // Confirmation popup
                using var eraseMarkersModal = ImRaii.PopupModal("Erase All Created Markers###WWClearConfirmation", ref showClearConfirmation, ImGuiWindowFlags.AlwaysAutoResize);
                if (eraseMarkersModal)
                {
                    var totalMarkers = plugin.MarkerStorageService.GetVisibleMarkers().Count;
                    ImGui.Text($"Are you sure you want to delete all {totalMarkers} markers?");
                    ImGui.Text("This action cannot be undone!");
                    ImGui.Spacing();

                    if (ImGui.Button("Yes, Delete All", new Vector2(150, 0)))
                    {
                        plugin.MarkerStorageService.ErasePersonalMarkers();
                        plugin.MarkerStorageService.EraseCreatedSharedMarkers();
                        Plugin.ChatGui.Print(ResultNotifications.BuildChatSuccessMessage("All markers have been deleted."));
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
    }
}
