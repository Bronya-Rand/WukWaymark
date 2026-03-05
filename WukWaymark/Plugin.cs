using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using WukWaymark.Windows;
using System.Numerics;
using WukWaymark.Models;
using System;
using Lumina.Excel.Sheets;

namespace WukWaymark;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private const string CommandName = "/pmycommand";
    private const string WaymarkCommandName = "/waymark";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("WukWaymark");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private WaymarkWindow WaymarkWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);
        WaymarkWindow = new WaymarkWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });

        CommandManager.AddHandler(WaymarkCommandName, new CommandInfo(OnWaymarkCommand)
        {
            HelpMessage = "Manage custom waymarks. Use '/waymark here' to save current location, or '/waymark' to view saved waymarks."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Add a simple message to the log with level set to information
        // Use /xllog to open the log window in-game
        // Example Output: 00:57:54.959 | INF | [WukWaymark] ===A cool log message from WukWaymark===
        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        WaymarkWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(WaymarkCommandName);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }

    private void OnWaymarkCommand(string command, string args)
    {
        var argsTrimmed = args.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(argsTrimmed))
        {
            // No arguments - open the main window to view waymarks
            MainWindow.Toggle();
            return;
        }

        if (argsTrimmed == "here" || argsTrimmed == "save")
        {
            // Save current location as a waymark
            SaveCurrentLocation();
            return;
        }

        // Unknown argument
        ChatGui.Print($"[WukWaymark] Unknown command. Use '/waymark' to view waymarks or '/waymark here' to save current location.");
    }

    private void SaveCurrentLocation()
    {
        var player = ClientState.LocalPlayer;
        if (player == null)
        {
            ChatGui.PrintError("[WukWaymark] You must be logged in to save a waymark.");
            return;
        }

        var territoryId = ClientState.TerritoryType;
        if (territoryId == 0)
        {
            ChatGui.PrintError("[WukWaymark] Unable to determine current location.");
            return;
        }

        // Get the current map ID from the territory
        uint mapId = 0;
        if (DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territoryRow))
        {
            mapId = territoryRow.Map.RowId;
        }

        // Create a new waymark
        var waymark = new Waymark
        {
            Position = player.Position,
            TerritoryId = territoryId,
            MapId = mapId,
            Name = $"Waymark {Configuration.Waymarks.Count + 1}",
            CreatedAt = DateTime.Now
        };

        Configuration.Waymarks.Add(waymark);
        Configuration.Save();

        ChatGui.Print($"[WukWaymark] Saved waymark '{waymark.Name}' at current location.");
        Log.Information($"Saved waymark: {waymark.Name} at {waymark.Position} (Territory: {territoryId}, Map: {mapId})");
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
