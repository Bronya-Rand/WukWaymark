using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using WukWaymark.Services;
using WukWaymark.Windows;

namespace WukWaymark;

public sealed class Plugin : IDalamudPlugin
{
    // ═══════════════════════════════════════════════════════════════
    // DALAMUD PLUGIN SERVICES
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Main plugin interface for accessing Dalamud services</summary>
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    /// <summary>Manages in-game slash commands</summary>
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

    /// <summary>Provides current game world and client state information</summary>
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    /// <summary>Provides access to game data via Lumina Excel sheets</summary>
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    /// <summary>Plugin logging service for debug output to /xllog</summary>
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    /// <summary>Provides access to in-game chat and notifications</summary>
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    /// <summary>Provides access to game UI addons (AreaMap, etc.)</summary>
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    /// <summary>Provides access to game objects and entities in the world</summary>
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;

    /// <summary>Provides access to Dalamud framework functionality</summary>
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    // ═══════════════════════════════════════════════════════════════
    // PLUGIN CONFIGURATION & SERVICES
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Primary slash command: /wwmark</summary>
    private const string WaymarkCommandName = "/wwmark";
    private const string WaymarkCommandAlias = "/wukwaymark";

    /// <summary>Persistent configuration storage for waymarks and settings</summary>
    public Configuration Configuration { get; init; }

    /// <summary>Business logic service for waymark operations (save, load, delete)</summary>
    public WaymarkService WaymarkService { get; init; }

    /// <summary>Manages rendering of all plugin windows</summary>
    public readonly WindowSystem WindowSystem = new("WukWaymark");

    /// <summary>Settings window for configuring waymark display behavior</summary>
    private ConfigWindow ConfigWindow { get; init; }

    /// <summary>Main window displaying list of saved waymarks and management UI</summary>
    private MainWindow MainWindow { get; init; }

    /// <summary>Service for rendering waymarks on the full-screen area map</summary>
    private WaymarkMapService WaymarkMapService { get; init; }

    /// <summary>Service for rendering waymarks on the minimap</summary>
    private WaymarkMinimapService WaymarkMinimapService { get; init; }

    public Plugin()
    {
        // Load or create configuration
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        WaymarkService = new WaymarkService(Configuration);

        // Initialize UI windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        WaymarkMapService = new WaymarkMapService(this);
        WaymarkMinimapService = new WaymarkMinimapService(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        // Register the /waymark command
        CommandManager.AddHandler(WaymarkCommandName, new CommandInfo(OnWaymarkCommand)
        {
            HelpMessage = $"""
            Manage and view your custom waymarks.
            {WaymarkCommandName} here → Save your current location as a waymark.
            """, ShowInHelp = true
        });
        // Also register an alias for the command
        CommandManager.AddHandler(WaymarkCommandAlias, new CommandInfo(OnWaymarkCommand)
        {
            HelpMessage = $"""
            Alias for {WaymarkCommandName}.
            {WaymarkCommandAlias} here → Alias for `{WaymarkCommandName} here`.
            """, ShowInHelp = false
        });

        // Register UI drawing handlers
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // Register plugin UI toggle buttons in the plugin installer
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information($"===Speak to Wuk Lamat [Ready]===");
    }
    public void Dispose()
    {
        // Unregister all event handlers to prevent memory leaks
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        // Clean up windows
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        WaymarkMapService.Dispose();
        WaymarkMinimapService.Dispose();

        // Unregister the slash command
        CommandManager.RemoveHandler(WaymarkCommandName);
    }

    /// <summary>
    /// Handles the /wwmark slash command with optional arguments.
    /// 
    /// Usage:
    /// /wwmark           - Opens the main waymark list window
    /// /wwmark here      - Saves current location as a new waymark
    /// </summary>
    private void OnWaymarkCommand(string command, string args)
    {
        var argsTrimmed = args.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(argsTrimmed))
        {
            // No arguments - open the main window to view waymarks
            MainWindow.Toggle();
            return;
        }

        if (argsTrimmed == "here")
        {
            Plugin.Log.Information("Saving current location as a waymark...");
            // Save current location as a waymark
            WaymarkService.SaveCurrentLocation();
            return;
        }

        // Unknown argument
        ChatGui.Print($"[WukWaymark] Unknown command. Use '{WaymarkCommandName}' to view waymarks or '{WaymarkCommandName} here' to save current location.");
    }

    /// <summary>Toggles the visibility of the configuration window</summary>
    public void ToggleConfigUi() => ConfigWindow.Toggle();

    /// <summary>Toggles the visibility of the main waymark management window</summary>
    public void ToggleMainUi() => MainWindow.Toggle();
}
