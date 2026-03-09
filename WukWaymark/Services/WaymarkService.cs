using System;
using System.Collections.Generic;
using System.Text;
using WukWaymark.Models;
using WukWaymark.Utils;

namespace WukWaymark.Services;

/// <summary>
/// Service class containing business logic for waymark operations.
/// Handles waymark creation, deletion, undo, and persistence.
/// </summary>
public class WaymarkService(Configuration configuration, WaymarkStorageService storageService)
{
    private readonly Configuration configuration = configuration;
    private readonly WaymarkStorageService storageService = storageService;

    /// <summary>
    /// Stack-based undo buffer for recently deleted waymarks.
    /// Kept in memory only — not persisted across sessions.
    /// </summary>
    private readonly Stack<Waymark> deletedWaymarks = new();

    /// <summary>Maximum number of deletions to remember for undo.</summary>
    private const int MaxUndoHistory = 10;

    /// <summary>Whether there are any deletions that can be undone.</summary>
    public bool CanUndo => deletedWaymarks.Count > 0;

    /// <summary>Number of deletions available for undo.</summary>
    public int UndoCount => deletedWaymarks.Count;

    /// <summary>
    /// Saves the player's current location as a new waymark.
    /// 
    /// This method:
    /// 1. Validates the player is logged in
    /// 2. Retrieves current location (position, territory, map, world)
    /// 3. Creates a waymark with auto-generated name and color
    /// 4. Persists the waymark to configuration
    /// 5. Provides user feedback via chat message
    /// 
    /// Validation errors are reported to the player via error messages.
    /// </summary>
    /// <param name="groupId">Optional group to assign the waymark to.</param>
    /// <param name="scope">The scope of the waymark (Personal or Shared).</param>
    /// <returns>The created waymark if successful, null if validation failed</returns>
    public Waymark? SaveCurrentLocation(WaymarkGroup? group = null, WaymarkScope scope = WaymarkScope.Personal)
    {
        // Verify player is logged in
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return null;

        // Get current territory ID
        var territoryId = Plugin.ClientState.TerritoryType;
        if (territoryId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current location.");
            return null;
        }

        // Look up the map ID from the territory data
        var mapId = Plugin.ClientState.MapId;
        if (mapId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current map.");
            return null;
        }

        // Get current world ID (used to differentiate waymarks across data centers)
        var currentWorldId = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.RowId ?? 0;
        if (currentWorldId == 0)
        {
            Plugin.ChatGui.PrintError("[WukWaymark] Unable to determine current world.");
            return null;
        }

        // Create a new waymark with current location data
        var waymark = new Waymark
        {
            Position = player.Position,
            TerritoryId = territoryId,
            MapId = mapId,
            WorldId = currentWorldId,
            Name = $"Waymark {configuration.Waymarks.Count + 1}",
            Color = Colors.GetNextColor(configuration.Waymarks.Count),
            Shape = configuration.DefaultWaymarkShape,
            CreatedAt = DateTime.Now,
            GroupId = group?.Id,
            Scope = scope,
            CharacterHash = scope == WaymarkScope.Personal ? storageService.CurrentCharacterHash : null,
        };

        // Persist to correct storage based on scope
        if (scope == WaymarkScope.Shared)
        {
            storageService.SharedWaymarks.Add(waymark);
            storageService.SaveSharedWaymarks();
        }
        else
        {
            configuration.Waymarks.Add(waymark);
            configuration.Save();
        }

        // Provide user feedback
        if (group != null)
        {
            Plugin.ChatGui.Print($"[WukWaymark] Saved waymark '{waymark.Name}' at current location in group '{group.Name}'.");
        }
        else
        {
            Plugin.ChatGui.Print($"[WukWaymark] Saved waymark '{waymark.Name}' at current location.");
        }
        Plugin.Log.Information($"Saved waymark: {waymark.Name} at {waymark.Position} (Territory: {territoryId}, Map: {mapId}, Scope: {scope})");

        return waymark;
    }

    /// <summary>
    /// Deletes a waymark, pushing it onto the undo stack first.
    /// </summary>
    /// <param name="waymark">The waymark to delete.</param>
    public void DeleteWaymark(Waymark waymark)
    {
        // Push to undo stack before removing
        if (deletedWaymarks.Count >= MaxUndoHistory)
        {
            // Remove the oldest entry while preserving LIFO order (newest on top)
            var items = deletedWaymarks.ToArray(); // items[0] is current top, items[^1] is bottom
            var keepCount = Math.Min(items.Length, MaxUndoHistory - 1);
            deletedWaymarks.Clear();
            // Rebuild the stack so that items[0] (original top) remains on top
            for (var i = keepCount - 1; i >= 0; i--)
                deletedWaymarks.Push(items[i]);
        }

        deletedWaymarks.Push(waymark);

        // Remove from whichever storage contains it
        var removedFromConfig = configuration.Waymarks.Remove(waymark);
        var removedFromShared = storageService.SharedWaymarks.Remove(waymark);

        if (removedFromConfig)
            configuration.Save();
        if (removedFromShared)
            storageService.SaveSharedWaymarks();

        Plugin.Log.Information($"Deleted waymark: {waymark.Name} (undo available)");
    }

    /// <summary>
    /// Undoes the most recent waymark deletion, restoring the waymark.
    /// </summary>
    /// <returns>The restored waymark, or null if nothing to undo.</returns>
    public Waymark? UndoDelete()
    {
        if (!CanUndo)
            return null;

        var waymark = deletedWaymarks.Pop();

        if (waymark.Scope == WaymarkScope.Shared)
        {
            storageService.SharedWaymarks.Add(waymark);
            storageService.SaveSharedWaymarks();
        }
        else
        {
            configuration.Waymarks.Add(waymark);
            configuration.Save();
        }

        Plugin.ChatGui.Print($"[WukWaymark] Restored waymark '{waymark.Name}'.");
        Plugin.Log.Information($"Undo delete: restored waymark '{waymark.Name}' (Scope: {waymark.Scope})");

        return waymark;
    }

    /// <summary>
    /// Finds a group by name (case-insensitive).
    /// </summary>
    /// <param name="name">The group name to search for.</param>
    /// <returns>The matching group, or null if not found.</returns>
    public WaymarkGroup? FindGroupByName(string name)
    {
        foreach (var group in configuration.WaymarkGroups)
        {
            if (group.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return group;
        }
        return null;
    }

    /// <summary>
    /// Returns a list of all group names, for error messages.
    /// </summary>
    public string GetGroupNamesList()
    {
        if (configuration.WaymarkGroups.Count == 0)
            return "(no groups exist)";

        var output = new StringBuilder();
        // Format as a bullet list with each name on a new line
        foreach (var group in configuration.WaymarkGroups)
            output.AppendLine($"- {group.Name}");

        return output.ToString().TrimEnd();
    }
}
