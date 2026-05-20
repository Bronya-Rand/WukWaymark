using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using WukLamark.Services;
using WukLamark.Windows.Tabs.MarkerList;
using WukLamark.Windows.Tabs.Settings;
using WukLamark.Windows.Tabs.Template;

namespace WukLamark.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly MarkerListTab markerListTab;
    private readonly TemplateTab templateTab;
    private readonly SettingsTab settingsTab;
    private bool forceOpenSettingsTab;

    public MainWindow(Plugin plugin, GameStateReaderService gameStateReaderService)
        : base("WukLamark##WWMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(575, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        markerListTab = new MarkerListTab(plugin);
        templateTab = new TemplateTab(plugin, gameStateReaderService);
        settingsTab = new SettingsTab(plugin);
    }
    public void OpenSettingsTab()
    {
        IsOpen = true;
        forceOpenSettingsTab = true;
    }

    public override void Draw()
    {
        using var tabBar = ImRaii.TabBar("MainTabBar");
        if (tabBar)
        {
            var settingsFlags = forceOpenSettingsTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

            using (var markersTab = ImRaii.TabItem("Markers"))
            {
                if (markersTab)
                    markerListTab.Draw();
            }
            using (var templatesTab = ImRaii.TabItem("Templates"))
            {
                if (templatesTab)
                    templateTab.Draw();
            }
            using (var settingsTabItem = ImRaii.TabItem("Settings", settingsFlags))
            {
                if (settingsTabItem)
                    settingsTab.Draw();
            }

            // Clear the flag after it's been consumed
            if (forceOpenSettingsTab)
                forceOpenSettingsTab = false;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
