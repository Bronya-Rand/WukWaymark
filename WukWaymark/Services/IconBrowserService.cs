using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WukWaymark.Services
{
    public class IconInfo
    {
        public uint IconId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
    }

    public class IconBrowserService : IDisposable
    {
        private readonly IDataManager dataManager;
        private readonly CancellationTokenSource cts = new();

        public bool IsLoaded { get; private set; }
        public IReadOnlyList<IconInfo> AvailableIcons { get; private set; } = [];
        private HashSet<uint> mapSymbolIds = [];

        public bool IsMapSymbol(uint iconId) => mapSymbolIds.Contains(iconId);

        public IconBrowserService(IDataManager dataManager)
        {
            this.dataManager = dataManager;
            Task.Run(() => LoadIconsAsync(cts.Token));
        }

        private void LoadIconsAsync(CancellationToken token)
        {
            try
            {
                var uniqueIcons = new Dictionary<uint, IconInfo>();
                var localMapSymbolIds = new HashSet<uint>();

                void AddIcons<T>(IEnumerable<T> sheet, Func<T, uint> getIcon, Func<T, string> getName, string source)
                {
                    foreach (var row in sheet)
                    {
                        if (token.IsCancellationRequested) return;

                        var iconId = getIcon(row);
                        if (iconId == 0) continue;

                        var name = getName(row);
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        if (!uniqueIcons.ContainsKey(iconId))
                        {
                            uniqueIcons[iconId] = new IconInfo { IconId = iconId, Name = name, Source = source };
                        }
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

                // TODO: Add sheets for Performance, General, Main Commands and Extras icons
                var items = dataManager.GetExcelSheet<Item>()!;
                AddIcons(items, i => i.Icon, i => i.Name.ToString(), "Item");

                var actions = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()!;
                AddIcons(actions, a => a.Icon, a => a.Name.ToString(), "Action");

                var emotes = dataManager.GetExcelSheet<Emote>()!;
                AddIcons(emotes, e => e.Icon, e => e.Name.ToString(), "Emote");

                var statuses = dataManager.GetExcelSheet<Status>()!;
                AddIcons(statuses, s => s.Icon, s => s.Name.ToString(), "Status");

                var mapSymbols = dataManager.GetExcelSheet<MapSymbol>()!;
                foreach (var row in mapSymbols)
                {
                    if (token.IsCancellationRequested) return;

                    var iconId = (uint)row.Icon;
                    if (iconId == 0 || uniqueIcons.ContainsKey(iconId)) continue;
                    uniqueIcons[iconId] = new IconInfo { IconId = iconId, Name = $"Map Symbol {iconId}", Source = "Map" };
                    localMapSymbolIds.Add(iconId);
                }

                if (token.IsCancellationRequested) return;

                // Atomically assign collections here to prevent race conditions with main thread readers
                AvailableIcons = uniqueIcons.Values.OrderBy(i => i.IconId).ToList();
                mapSymbolIds = localMapSymbolIds;

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
