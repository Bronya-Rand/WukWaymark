using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukLamark.Services;

namespace WukLamark.Windows.Components;

internal class IconPickerModal(Plugin plugin)
{
    private static readonly (string? Id, string Name)[] IconCategories =
    [
        (null, "All Icons"),
        ("Map", "Map Symbols"), ("Quest", "Quest Markers"), ("Item", "Items"), ("Action", "Actions"), ("Status", "Status Effects"),
        ("Macro", "Macros"), ("Emote", "Emotes"), ("Perform", "Performance"), ("General", "General"),
        ("Main", "Main Commands"), ("Extra", "Extra")
    ];

    private readonly Plugin plugin = plugin;
    private string searchFilter = string.Empty;
    private bool isOpen;

    public Action<uint?>? OnIconSelected { get; set; }

    public void Open()
    {
        isOpen = true;
        searchFilter = string.Empty;
    }

    public void Draw(string markerName, string identifier)
    {
        if (!isOpen) return;

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);

        using var iconPickerModal = ImRaii.PopupModal($"Marker Icon Picker ({markerName})###{identifier}", ref isOpen, ImGuiWindowFlags.NoSavedSettings);
        if (!iconPickerModal) return;

        if (!plugin.IconBrowserService.IsLoaded)
        {
            ImGui.TextDisabled("Loading game icons...");
            return;
        }

        // Search box
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##IconSearch", "Search by name or ID...", ref searchFilter, 50);

        if (ImGui.Button("None (Use Shape)"))
        {
            OnIconSelected?.Invoke(null);
            isOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        using var iconCategoryTabBar = ImRaii.TabBar("IconCategoryTabs");
        if (iconCategoryTabBar)
        {
            foreach (var category in IconCategories)
            {
                using var tabItem = ImRaii.TabItem(category.Name);
                if (tabItem)
                {
                    DrawIconGrid(plugin.IconBrowserService.AvailableIcons, category.Id, searchFilter);
                }
            }
        }
    }

    public void OpenPopup(string markerName, string identifier)
    {
        Open();
        ImGui.OpenPopup($"Marker Icon Picker ({markerName})###{identifier}");
    }

    private void DrawIconGrid(IEnumerable<IconInfo> allIcons, string? category, string searchStr)
    {
        var searchLower = searchStr.ToLowerInvariant();
        var icons = allIcons;

        if (category != null)
            icons = icons
            .Where(i => i.Source == category);

        var query = icons
            .Where(i => string.IsNullOrEmpty(searchLower) ||
                        i.Name.ToLowerInvariant().Contains(searchLower) ||
                        i.IconId.ToString().Contains(searchLower));

        var totalCount = query.Count();
        var filteredIcons = query.Take(200).ToList();

        if (filteredIcons.Count == 0)
        {
            ImGui.TextDisabled("No matching icons found.");
            return;
        }

        if (totalCount > 200)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"Showing 200 of {totalCount} icons. Please refine your search.");
            ImGui.Spacing();
        }

        using var childVisible = ImRaii.Child($"IconGrid_{category}", new Vector2(0, -1), true);
        if (!childVisible) return;

        var contentRegion = ImGui.GetContentRegionAvail().X;
        var iconSize = 40f * ImGuiHelpers.GlobalScale;
        var padding = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)(contentRegion / (iconSize + padding)));

        using var iconTable = ImRaii.Table($"IconTable_{category}", columns);
        if (!iconTable) return;

        foreach (var icon in filteredIcons)
        {
            ImGui.TableNextColumn();

            IDalamudTextureWrap? tex;
            try
            {
                tex = Plugin.TextureProvider.GetFromGameIcon(icon.IconId).GetWrapOrEmpty();
            }
            catch (IconNotFoundException)
            {
                continue;
            }

            if (tex != null && tex.Handle != nint.Zero)
            {
                using (ImRaii.PushId($"IconBtn_{icon.IconId}"))
                {
                    if (ImGui.ImageButton(tex.Handle, new Vector2(iconSize, iconSize)))
                    {
                        OnIconSelected?.Invoke(icon.IconId);
                        isOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.IsItemHovered())
                    {
                        using var tooltip = ImRaii.Tooltip();
                        if (tooltip)
                        {
                            ImGui.TextUnformatted(icon.Name);
                            ImGui.TextDisabled($"ID: {icon.IconId}");
                        }
                    }
                }
            }
        }
    }
}
