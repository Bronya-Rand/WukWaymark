using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures.Internal;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using WukLamark.Models;

namespace WukLamark.Windows.Components;

internal sealed record IconPickerResult(
    MarkerIconType SourceType = MarkerIconType.Shape,
    uint? GameIconId = null,
    string? CustomIconName = null);
internal sealed class IconPickerModal(Plugin plugin)
{
    private static readonly (string? Id, string Name)[] GameCategories =
    [
        (null, "All Icons"),
        ("Map", "Map Symbols"), ("Quest", "Quest Markers"), ("Item", "Items"), ("Action", "Actions"),
        ("Status", "Status Effects"), ("Macro", "Macros"), ("Emote", "Emotes"), ("Perform", "Performance"),
        ("General", "General"), ("Main", "Main Commands"), ("Extra", "Extra")
    ];

    private sealed record PickerEntry(
        MarkerIconType SourceType,
        string DisplayName,
        string SearchText,
        uint? GameIconId,
        string? CustomIconName,
        IDalamudTextureWrap? Texture);

    private readonly Plugin plugin = plugin;
    private string searchFilter = string.Empty;
    private bool isOpen;
    private MarkerIconType preferredTab = MarkerIconType.Game;

    // Caches
    private string cacheKey = string.Empty;
    private List<PickerEntry> cachedEntries = [];
    private bool cachedTruncated;
    public Action<IconPickerResult?>? OnIconSelected { get; set; }

    public void OpenPopup(string markerName, string identifier, MarkerIconType preferred)
    {
        isOpen = true;
        searchFilter = string.Empty;
        preferredTab = preferred;
        cacheKey = string.Empty;
        cachedEntries.Clear();
        cachedTruncated = false;
        ImGui.OpenPopup($"Marker Icon Picker ({markerName})###{identifier}");
    }
    public void Draw(string markerName, string identifier, MarkerIconType markerIconType)
    {
        if (!isOpen) return;

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);

        using var iconPickerModal = ImRaii.PopupModal($"Marker Icon Picker ({markerName})###{identifier}", ref isOpen, ImGuiWindowFlags.NoSavedSettings);
        if (!iconPickerModal) return;

        // Search box
        ImGui.SetNextItemWidth(-1);

        var searchPlaceholder = preferredTab == MarkerIconType.Game
            ? "Search by name or ID..."
            : "Search by name...";
        ImGui.InputTextWithHint("##IconSearch", searchPlaceholder, ref searchFilter, 50);
        ImGui.SameLine();
        if (ImGuiComponents.IconButton(Dalamud.Interface.FontAwesomeIcon.Circle))
            Plugin.CustomIconService.ReloadCustomIcons();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Reload custom icons from disk.");

        if (!plugin.IconBrowserService.IsLoaded || !Plugin.CustomIconService.IsLoaded)
        {
            ImGui.TextDisabled("Loading icons...");
            return;
        }

        if (ImGui.Button("None (Use Shape)"))
        {
            OnIconSelected?.Invoke(null);
            Close();
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        if (preferredTab == MarkerIconType.Game)
        {
            using var iconCategoryTabBar = ImRaii.TabBar("IconCategoryTabs", ImGuiTabBarFlags.FittingPolicyScroll);
            if (!iconCategoryTabBar) return;

            DrawGameTabs();
        }
        else if (preferredTab == MarkerIconType.Custom)
        {
            DrawIconGrid(BuildCustomEntries(), "custom");
        }
    }
    private void DrawGameTabs()
    {
        foreach (var category in GameCategories)
        {
            using var gameTab = ImRaii.TabItem(category.Name);
            if (!gameTab) continue;

            DrawIconGrid(BuildGameEntries(category.Id), $"game:{category.Id ?? "all"}");
        }
    }
    private List<PickerEntry> BuildCustomEntries()
    {
        if (!Plugin.CustomIconService.IsLoaded) return [];

        return Plugin.CustomIconService.AvailableIcons
            .Select(icon => new PickerEntry(
                MarkerIconType.Custom,
                icon.FileName,
                icon.FileName.ToLowerInvariant(),
                null,
                icon.FileName,
                null)) // Texture resolved lazily at render time
            .ToList();
    }
    private List<PickerEntry> BuildGameEntries(string? category)
    {
        if (!plugin.IconBrowserService.IsLoaded) return [];

        var query = plugin.IconBrowserService.AvailableIcons.AsEnumerable();
        if (!string.IsNullOrEmpty(category))
            query = query.Where(i => i.Source == category);

        return query.Select(i => new PickerEntry(
            MarkerIconType.Game,
            i.Name,
            $"{i.Name} {i.IconId}",
            i.IconId,
            null,
            null)).ToList();
    }
    private void DrawIconGrid(List<PickerEntry> pickerEntries, string tabKey)
    {
        var searchLower = searchFilter.Trim().ToLowerInvariant();
        var newKey = $"{tabKey}:{searchLower}";

        if (newKey != cacheKey)
        {
            cacheKey = newKey;
            var q = pickerEntries.Where(e =>
                    searchLower.IsNullOrEmpty() ||
                    e.SearchText.Contains(searchLower, StringComparison.OrdinalIgnoreCase));

            var snapshot = q.Take(201).ToList();
            cachedTruncated = snapshot.Count > 200;
            cachedEntries = snapshot.Take(200).ToList();
        }

        if (cachedEntries.Count == 0)
        {
            ImGui.TextDisabled("No matching icons found.");
            return;
        }

        if (cachedTruncated)
        {
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"Truncated results to 200 icons. Please refine your search.");
            ImGui.Spacing();
        }

        using var childVisible = ImRaii.Child($"IconGrid_{tabKey}", new Vector2(0, -1), true);
        if (!childVisible) return;

        var size = 40f * ImGuiHelpers.GlobalScale;
        var cols = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / (size + ImGui.GetStyle().ItemSpacing.X)));

        using var table = ImRaii.Table($"table_{tabKey}", cols);
        if (!table) return;

        foreach (var e in cachedEntries)
        {
            ImGui.TableNextColumn();

            var tex = e.SourceType == MarkerIconType.Custom
                ? (Plugin.CustomIconService.TryGetCustomIcon(e.CustomIconName, out var customTex) ? customTex : null)
                : GetGameTexture(e.GameIconId);
            if (tex == null || tex.Handle == nint.Zero) continue;

            using var id = ImRaii.PushId($"{e.SourceType}_{e.GameIconId}_{e.CustomIconName}");
            if (ImGui.ImageButton(tex.Handle, new Vector2(size, size)))
            {
                OnIconSelected?.Invoke(new IconPickerResult(e.SourceType, e.GameIconId, e.CustomIconName));
                Close();
                return;
            }

            if (ImGui.IsItemHovered())
            {
                using var tt = ImRaii.Tooltip();
                ImGui.TextUnformatted(e.DisplayName);
                if (e.GameIconId is uint idVal) ImGui.TextDisabled($"ID: {idVal}");
            }
        }
    }
    private static IDalamudTextureWrap? GetGameTexture(uint? id)
    {
        if (id is null or 0) return null;
        try { return Plugin.TextureProvider.GetFromGameIcon(id.Value).GetWrapOrEmpty(); }
        catch (IconNotFoundException) { return null; }
    }
    private void Close()
    {
        isOpen = false;
        ImGui.CloseCurrentPopup();
    }
}
