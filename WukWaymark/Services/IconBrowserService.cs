using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public bool IsLoaded { get; private set; }
        public List<IconInfo> AvailableIcons { get; private set; } = [];
        private readonly HashSet<uint> mapSymbolIds = [];

        public bool IsMapSymbol(uint iconId) => mapSymbolIds.Contains(iconId);

        public IconBrowserService(IDataManager dataManager)
        {
            this.dataManager = dataManager;
            Task.Run(LoadIconsAsync);
        }

        private void LoadIconsAsync()
        {
            try
            {
                var uniqueIcons = new Dictionary<uint, IconInfo>();

                void AddIcons<T>(IEnumerable<T> sheet, Func<T, uint> getIcon, Func<T, string> getName, string source)
                {
                    foreach (var row in sheet)
                    {
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
                    var iconId = (uint)row.Icon;
                    if (iconId == 0 || uniqueIcons.ContainsKey(iconId)) continue;
                    uniqueIcons[iconId] = new IconInfo { IconId = iconId, Name = $"Map Symbol {iconId}", Source = "Map" };
                    mapSymbolIds.Add(iconId);
                }

                AvailableIcons = uniqueIcons.Values.OrderBy(i => i.IconId).ToList();
                IsLoaded = true;
                Plugin.Log.Information($"Loaded {AvailableIcons.Count} unique icons for the UI picker.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to load icon database.");
            }
        }

        public void Dispose()
        {
            AvailableIcons.Clear();
            mapSymbolIds.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
