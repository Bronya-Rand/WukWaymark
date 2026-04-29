using Dalamud.Interface.Textures.Internal;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WukLamark.Services
{
    public class IconInfo
    {
        public uint IconId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
    }

    public sealed class IconBrowserService : IDisposable
    {
        private readonly IDataManager dataManager;
        private readonly CancellationTokenSource cts = new();

        public bool IsLoaded { get; private set; }
        public IReadOnlyList<IconInfo> AvailableIcons { get; private set; } = [];

        public IconBrowserService(IDataManager dataManager)
        {
            this.dataManager = dataManager;
            Plugin.Framework.RunOnFrameworkThread(() => LoadIconsAsync(cts.Token));
        }
        private void LoadIconsAsync(CancellationToken token)
        {
            try
            {
                var uniqueIcons = new Dictionary<uint, IconInfo>();

                // Load from sheets with uint icon IDs
                LoadIconsFromLumina(dataManager.GetExcelSheet<Item>()!, row => row.Icon, row => row.Name.ToString(), "Item", uniqueIcons, token);
                LoadIconsFromLumina(dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()!, row => row.Icon, row => row.Name.ToString(), "Action", uniqueIcons, token);
                LoadIconsFromLumina(dataManager.GetExcelSheet<Emote>()!, row => row.Icon, row => row.Name.ToString(), "Emote", uniqueIcons, token);
                LoadIconsFromLumina(dataManager.GetExcelSheet<Status>()!, row => row.Icon, row => row.Name.ToString(), "Status", uniqueIcons, token);
                LoadIconsFromLumina(dataManager.GetExcelSheet<QuestLinkMarkerIcon>()!, row => row.Icon, row => $"Quest Icon {row.RowId}", "Quest", uniqueIcons, token);
                // Load from sheets with int icon IDs   
                LoadIconsFromLuminaInt(dataManager.GetExcelSheet<Perform>()!, row => row.Icon, row => row.Name.ToString(), "Perform", uniqueIcons, token);
                LoadIconsFromLuminaInt(dataManager.GetExcelSheet<GeneralAction>()!, row => row.Icon, row => row.Name.ToString(), "General", uniqueIcons, token);
                LoadIconsFromLuminaInt(dataManager.GetExcelSheet<MainCommand>()!, row => row.Icon, row => row.Name.ToString(), "Main", uniqueIcons, token);
                LoadIconsFromLuminaInt(dataManager.GetExcelSheet<ExtraCommand>()!, row => row.Icon, row => row.Name.ToString(), "Extra", uniqueIcons, token);

                LoadMacroIcons(dataManager.GetExcelSheet<MacroIcon>()!, uniqueIcons, token);
                LoadMapSymbols(dataManager.GetExcelSheet<MapSymbol>()!, uniqueIcons, token);

                if (token.IsCancellationRequested) return;

                // Atomically assign collections here to prevent race conditions with main thread readers
                AvailableIcons = uniqueIcons.Values.OrderBy(i => i.IconId).ToList();
                IsLoaded = true;
                Plugin.Log.Information($"Loaded {AvailableIcons.Count} unique icons for the UI picker.");
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException) return;
                Plugin.Log.Error(ex, "Failed to load icon database.");
            }
        }
        private static void LoadIconsFromLumina<T>(IEnumerable<T> excelSheet, Func<T, uint> getIconId, Func<T, string> getName, string source, Dictionary<uint, IconInfo> uniqueIcons, CancellationToken token)
        {
            foreach (var row in excelSheet)
            {
                if (token.IsCancellationRequested) return;

                var iconId = getIconId(row);
                if (iconId == 0 || uniqueIcons.ContainsKey(iconId)) continue;

                var name = getName(row);
                if (name.IsNullOrEmpty()) continue;

                // Check if loadable
                if (!TryAddIcon(iconId, name, source, uniqueIcons))
                    Plugin.Log.Debug($"Icon {iconId} ({name}) from {source} is not loadable, skipping.");
            }
        }
        private static void LoadIconsFromLuminaInt<T>(IEnumerable<T> excelSheet, Func<T, int> getIconId, Func<T, string> getName, string source, Dictionary<uint, IconInfo> uniqueIcons, CancellationToken token)
        {
            foreach (var row in excelSheet)
            {
                if (token.IsCancellationRequested) return;

                var iconIdInt = getIconId(row);
                if (iconIdInt < 0) continue;

                var iconId = (uint)iconIdInt;
                if (iconId == 0 || uniqueIcons.ContainsKey(iconId)) continue;

                var name = getName(row);
                if (name.IsNullOrEmpty()) continue;

                // Check if loadable
                if (!TryAddIcon(iconId, name, source, uniqueIcons))
                    Plugin.Log.Debug($"Icon {iconId} ({name}) from {source} is not loadable, skipping.");
            }
        }
        private static void LoadMacroIcons(IEnumerable<MacroIcon> macroSheet, Dictionary<uint, IconInfo> uniqueIcons, CancellationToken token)
        {
            foreach (var row in macroSheet)
            {
                if (token.IsCancellationRequested) return;
                var iconId = (uint)row.Icon;

                if (iconId == 0 || uniqueIcons.ContainsKey(iconId)) continue;
                var name = $"Macro Icon {iconId}";

                // Check if loadable
                if (!TryAddIcon(iconId, name, "Macro", uniqueIcons))
                    Plugin.Log.Debug($"Macro Icon {iconId} is not loadable, skipping.");
            }
        }
        private static void LoadMapSymbols(IEnumerable<MapSymbol> mapSymbols, Dictionary<uint, IconInfo> uniqueIcons, CancellationToken token)
        {
            foreach (var row in mapSymbols)
            {
                if (token.IsCancellationRequested) return;
                var iconId = (uint)row.Icon;

                if (iconId == 0 || uniqueIcons.ContainsKey(iconId)) continue;
                var name = row.PlaceName.Value.Name.ToString() ?? $"Map Symbol {iconId}";

                // Check if loadable
                if (!TryAddIcon(iconId, name, "Map", uniqueIcons))
                    Plugin.Log.Debug($"Map Symbol {iconId} is not loadable, skipping.");
            }
        }
        private static bool TryAddIcon(uint iconId, string name, string source, Dictionary<uint, IconInfo> uniqueIcons)
        {
            try
            {
                Plugin.TextureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
                uniqueIcons[iconId] = new IconInfo { IconId = iconId, Name = name, Source = source };
                return true;
            }
            catch (IconNotFoundException)
            {
                return false;
            }
        }
        public void Dispose()
        {
            cts.Cancel();
            cts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
