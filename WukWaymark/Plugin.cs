using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
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

    /// <summary>Provides access to game textures and icons</summary>
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    /// <summary>Provides access to the player's state (content ID, etc.)</summary>
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;

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

    /// <summary>Background service for loading and caching game icons</summary>
    public IconBrowserService IconBrowserService { get; init; }

    /// <summary>Handles cross-character waymark persistence and scoping</summary>
    public WaymarkStorageService WaymarkStorageService { get; init; }

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

        // Initialize services
        var pluginConfigDir = PluginInterface.GetPluginConfigDirectory();
        WaymarkStorageService = new WaymarkStorageService(Configuration, pluginConfigDir);
        WaymarkService = new WaymarkService(Configuration, WaymarkStorageService);
        IconBrowserService = new IconBrowserService(DataManager);

        // Set character hash if player is already logged in
        if (PlayerState.ContentId != 0)
            WaymarkStorageService.SetCharacterHash(PlayerState.ContentId);

        // Subscribe to login/logout events for character hash management
        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;

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
            {WaymarkCommandName} here <group> → Save to a specific group.
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
        // Unregister login/logout events
        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;

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
        CommandManager.RemoveHandler(WaymarkCommandAlias);
    }

    /// <summary>Called when the player logs in — set character hash for personal waymark scoping.</summary>
    private void OnLogin()
    {
        if (PlayerState.ContentId != 0)
            WaymarkStorageService.SetCharacterHash(PlayerState.ContentId);
    }

    /// <summary>Called when the player logs out — clear character hash.</summary>
    private void OnLogout(int kind, int flags)
    {
        WaymarkStorageService.ClearCharacterHash();
    }

    /// <summary>
    /// Handles the /wwmark slash command with optional arguments.
    /// 
    /// Usage:
    /// /wwmark                  - Opens the main waymark list window
    /// /wwmark here             - Saves current location as a new waymark (ungrouped)
    /// /wwmark here [group]     - Saves current location to the specified group
    /// </summary>
    private void OnWaymarkCommand(string command, string args)
    {
        var argsTrimmed = args.Trim();

        if (string.IsNullOrEmpty(argsTrimmed))
        {
            // No arguments - open the main window to view waymarks
            MainWindow.Toggle();
            return;
        }

        var tokens = argsTrimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstToken = tokens[0];

        // Check for "here" command (with optional group name)
        if (firstToken.Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = tokens.Length > 1 ? tokens[1].Trim() : string.Empty;

            if (string.IsNullOrEmpty(remainder))
            {
                // /wwmark here — save ungrouped
                Log.Information("Saving current location as a waymark...");
                WaymarkService.SaveCurrentLocation();
                return;
            }

            // /wwmark here <group> — save to the specified group
            var group = WaymarkService.FindGroupByName(remainder);
            if (group == null)
            {
                ChatGui.PrintError($"[WukWaymark] Group '{remainder}' not found. Available groups:\n{WaymarkService.GetGroupNamesList()}");
                return;
            }

            Log.Information($"Saving current location to group '{group.Name}'...");
            WaymarkService.SaveCurrentLocation(group);
            return;
        }

        // Unknown argument
        ChatGui.Print($"[WukWaymark] Unknown command. Use '{WaymarkCommandName}' to view waymarks or '{WaymarkCommandName} here [group]' to save current location.");
    }

    /// <summary>Toggles the visibility of the configuration window</summary>
    public void ToggleConfigUi() => ConfigWindow.Toggle();

    /// <summary>Toggles the visibility of the main waymark management window</summary>
    public void ToggleMainUi() => MainWindow.Toggle();
}
