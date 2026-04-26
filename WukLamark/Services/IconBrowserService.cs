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

                void AddIcons<T>(IEnumerable<T> sheet, Func<T, uint> getIcon, Func<T, string> getName, string source, bool isIcon = false)
                {
                    foreach (var row in sheet)
                    {
                        if (token.IsCancellationRequested) return;

                        var iconId = getIcon(row);
                        if (iconId == 0) continue;

                        var name = getName(row);
                        if (name.IsNullOrEmpty()) continue;

                        // Check if loadable
                        try
                        {
                            Plugin.TextureProvider.GetFromGameIcon(iconId).GetWrapOrEmpty();
                        }
                        catch (IconNotFoundException)
                        {
                            Plugin.Log.Debug($"Icon {iconId} ({name}) from {source} is not loadable, skipping.");
                        }

                        if (!uniqueIcons.ContainsKey(iconId))
                            uniqueIcons[iconId] = new IconInfo { IconId = iconId, Name = name, Source = source };
                    }
                }

                var macroSheet = dataManager.GetExcelSheet<MacroIcon>()!;
                foreach (var row in macroSheet)
                {
                    if (token.IsCancellationRequested) return;

                    var iconId = (uint)row.Icon;
                    if (iconId == 0 || uniqueIcons.ContainsKey(iconId)) continue;
                    uniqueIcons[iconId] = new IconInfo { IconId = iconId, Name = $"Macro Icon {iconId}", Source = "Macro" };
                }

                var items = dataManager.GetExcelSheet<Item>()!;
                AddIcons(items, i => i.Icon, i => i.Name.ToString(), "Item");

                var actions = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()!;
                AddIcons(actions, a => a.Icon, a => a.Name.ToString(), "Action");

                var emotes = dataManager.GetExcelSheet<Emote>()!;
                AddIcons(emotes, e => e.Icon, e => e.Name.ToString(), "Emote");

                // Sheets with int icon IDs - filter out negatives before casting to uint to prevent crashes
                var perform = dataManager.GetExcelSheet<Perform>()!;
                AddIcons(perform, p => p.Icon >= 0 ? (uint)p.Icon : 0, p => p.Name.ToString(), "Perform");

                var generalActions = dataManager.GetExcelSheet<GeneralAction>()!;
                AddIcons(generalActions, a => a.Icon >= 0 ? (uint)a.Icon : 0, a => a.Name.ToString(), "General");

                var mainCommands = dataManager.GetExcelSheet<MainCommand>()!;
                AddIcons(mainCommands, m => m.Icon >= 0 ? (uint)m.Icon : 0, m => m.Name.ToString(), "Main");

                var extras = dataManager.GetExcelSheet<ExtraCommand>()!;
                AddIcons(extras, e => e.Icon >= 0 ? (uint)e.Icon : 0, e => e.Name.ToString(), "Extra");

                var statuses = dataManager.GetExcelSheet<Status>()!;
                AddIcons(statuses, s => s.Icon, s => s.Name.ToString(), "Status", true);

                var questMarkers = dataManager.GetExcelSheet<QuestLinkMarkerIcon>()!;
                AddIcons(questMarkers, q => q.Icon, q => $"Quest Icon {q.RowId}", "Quest", true);

                var mapSymbols = dataManager.GetExcelSheet<MapSymbol>()!;
                foreach (var row in mapSymbols)
                {
                    if (token.IsCancellationRequested) return;

                    var iconId = (uint)row.Icon;
                    if (iconId == 0 || uniqueIcons.ContainsKey(iconId)) continue;
                    uniqueIcons[iconId] = new IconInfo { IconId = iconId, Name = row.PlaceName.Value.Name.ToString() ?? $"Map Symbol {iconId}", Source = "Map" };
                }

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
        public void Dispose()
        {
            cts.Cancel();
            cts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
