using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using WukLamark.Helpers;
using WukLamark.Services;
using WukLamark.Windows;

namespace WukLamark;

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

    /// <summary>Primary slash command: /wlmark</summary>
    private const string MarkerCommandName = "/wlmark";
    private const string MarkerCommandAlias = "/wuklamark";

    /// <summary>Persistent configuration storage for map markers and settings</summary>
    public Configuration Configuration { get; init; }

    /// <summary>Business logic service for marker operations (save, load, delete)</summary>
    public MarkerService MarkerService { get; init; }

    /// <summary>Background service for loading and caching game icons</summary>
    public IconBrowserService IconBrowserService { get; init; }

    public GameStateReaderService GameStateReaderService { get; init; } = new();

    /// <summary>Handles cross-character marker persistence and scoping</summary>
    public MarkerStorageService MarkerStorageService { get; init; }

    /// <summary>Manages rendering of all plugin windows</summary>
    public readonly WindowSystem WindowSystem = new("WukLamark");

    /// <summary>Settings window for configuring marker display behavior</summary>
    private ConfigWindow ConfigWindow { get; init; }

    /// <summary>Main window displaying list of saved map markers and management UI</summary>
    private MainWindow MainWindow { get; init; }

    /// <summary>Service for rendering map markers on the full-screen area map</summary>
    private MarkerMapService MarkerMapService { get; init; }

    /// <summary>Service for rendering map markers on the minimap</summary>
    private MarkerMinimapService MarkerMinimapService { get; init; }

    public Plugin()
    {
        // Load or create configuration
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize services
        var pluginConfigDir = PluginInterface.GetPluginConfigDirectory();
        MarkerStorageService = new MarkerStorageService(pluginConfigDir);
        MarkerService = new MarkerService(Configuration, MarkerStorageService);
        IconBrowserService = new IconBrowserService(DataManager);
        GameStateReaderService = new GameStateReaderService();

        // Preload world/DC/territory lookup caches
        LocationHelper.InitializeWorldCache();
        LocationHelper.InitializeTerritoryCache();

        // Set character hash if player is already logged in
        // Note: We can't access ObjectTable.LocalPlayer in the constructor
        // so we pass content ID directly from PlayerState
        if (PlayerState.ContentId != 0)
        {
            MarkerStorageService.SetCharacterHash(PlayerState.ContentId);
        }

        // Subscribe to login/logout events for character hash management
        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;
        ClientState.TerritoryChanged += OnTerritoryChange;

        // Initialize UI windows
        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        MarkerMapService = new MarkerMapService(this);
        MarkerMinimapService = new MarkerMinimapService(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        // Register the /wlmark command
        CommandManager.AddHandler(MarkerCommandName, new CommandInfo(OnMarkerCommand)
        {
            HelpMessage = $"""
            Manage and view your custom map markers.
            {MarkerCommandName} here → Save your current location as a map marker.
            {MarkerCommandName} here <group> → Save to a specific group.
            """, ShowInHelp = true
        });
        // Also register an alias for the command
        CommandManager.AddHandler(MarkerCommandAlias, new CommandInfo(OnMarkerCommand)
        {
            HelpMessage = $"""
            Alias for {MarkerCommandName}.
            {MarkerCommandAlias} here → Alias for `{MarkerCommandName} here`.
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
        ClientState.TerritoryChanged -= OnTerritoryChange;

        // Unregister all event handlers to prevent memory leaks
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        // Clean up windows
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        MarkerMapService.Dispose();
        MarkerMinimapService.Dispose();
        GameStateReaderService.Dispose();
        IconBrowserService.Dispose();

        // Unregister the slash command
        CommandManager.RemoveHandler(MarkerCommandName);
        CommandManager.RemoveHandler(MarkerCommandAlias);
    }

    /// <summary>
    /// Called when the player logs in
    /// -- Sets the character hash for personal marker scoping.
    /// -- Updates the current world ID for location-based marker display.
    /// </summary>
    private void OnLogin()
    {
        if (PlayerState.ContentId != 0)
        {
            MarkerStorageService.SetCharacterHash(PlayerState.ContentId);
            LocationHelper.UpdateCurrentWorldId();
        }
    }

    /// <summary>Called when the player logs out — clear character hash.</summary>
    private void OnLogout(int kind, int flags)
    {
        MarkerStorageService.ClearCharacterHash();
    }

    /// <summary>
    /// Called when the player changes territory (zone) in the game.
    /// -- Updates the current world ID in the LocationHelper to the current world ID.
    /// </summary>
    private void OnTerritoryChange(ushort _) => LocationHelper.UpdateCurrentWorldId();

    /// <summary>
    /// Handles the /wlmark slash command with optional arguments.
    /// <param name="command">The command string (e.g., "/wlmark").</param>
    /// <param name="args">The arguments provided with the command.</param>
    /// </summary>
    /// <remarks>
    /// Supported commands:
    /// /wlmark                  - Opens the main map marker list window
    /// /wlmark here             - Saves current location as a new map marker (ungrouped)
    /// /wlmark here [group]     - Saves current location to the specified group
    /// </remarks>
    private void OnMarkerCommand(string command, string args)
    {
        var argsTrimmed = args.Trim();

        if (string.IsNullOrEmpty(argsTrimmed))
        {
            // No arguments - open the main window to view map markers
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
                // /wlmark here — save ungrouped
                Log.Information("Saving current location as a map marker...");
                MarkerService.SaveCurrentLocation();
                return;
            }

            // /wlmark here <group> — save to the specified group
            var group = MarkerService.FindGroupByName(remainder);
            if (group == null || !MarkerService.CanAddMarkerToGroup(group))
            {
                ChatGui.PrintError($"[WukLamark] Group '{remainder}' not found or you lack permission to modify it. Available groups:\n{MarkerService.GetGroupNamesList()}");
                return;
            }

            Log.Information($"Saving current location to group '{group.Name}'...");
            MarkerService.SaveCurrentLocation(group, group.Scope);
            return;
        }

        // Unknown argument
        ChatGui.Print($"[WukLamark] Unknown command. Use '{MarkerCommandName}' to view map markers or '{MarkerCommandName} here [group]' to save current location.");
    }

    /// <summary>Toggles the visibility of the configuration window</summary>
    public void ToggleConfigUi() => ConfigWindow.Toggle();

    /// <summary>Toggles the visibility of the main map marker management window</summary>
    public void ToggleMainUi() => MainWindow.Toggle();
}
